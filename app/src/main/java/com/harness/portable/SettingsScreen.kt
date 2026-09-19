package com.harness.portable

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import android.app.Activity
import android.os.Handler
import android.os.Looper
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MenuAnchorType
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    tunnels: List<TunnelProfile>,
    directs: List<String>,
    runningTunnelId: String?,
    onConnectTunnel: (TunnelProfile) -> Unit,
    onAddTunnel: (TunnelProfile, String?, String?) -> Unit,
    onUpdateTunnel: (TunnelProfile, String?, String?) -> Unit,
    onDeleteTunnel: (TunnelProfile) -> Unit,
    onConnectDirect: (String) -> Unit,
    onAddDirect: (String) -> Unit,
    onDeleteDirect: (String) -> Unit
) {
    val ctx = LocalContext.current
    val activity = ctx as? Activity
    val scope = rememberCoroutineScope()
    val mainHandler = remember { Handler(Looper.getMainLooper()) }
    var editorFor by remember { mutableStateOf<TunnelProfile?>(null) }
    var showEditor by remember { mutableStateOf(false) }
    var directInput by remember { mutableStateOf("") }
    val initialSources = remember {
        // Fold the pre-two-slot single setting into the new pair once, so an
        // existing install keeps checking wherever the user had aimed it.
        UpdateSourceStore.migrateLegacy(ctx, ApkUpdate.storedServer(ctx))
        UpdateSourceStore.load(ctx)
    }
    var updateSources by remember { mutableStateOf(initialSources) }
    var updateUrl by remember { mutableStateOf(initialSources.url()) }
    var updateStatus by remember { mutableStateOf("尚未检查更新") }
    var updateNotes by remember { mutableStateOf("") }
    var updateBusy by remember { mutableStateOf(false) }
    var updateProgress by remember { mutableStateOf(-1) }
    var pendingFeed by remember { mutableStateOf<ApkFeed?>(null) }
    val versionName = remember { ApkUpdate.currentVersionName(ctx) }
    val versionCode = remember { ApkUpdate.currentVersionCode(ctx) }

    /** Writes the edited URL into its slot, so switching sources keeps both. */
    fun storeSources(): UpdateSources {
        val next = when (updateSources.selected) {
            UpdateSourceKind.GitHub -> updateSources.copy(github = updateUrl.trim())
            UpdateSourceKind.File -> updateSources.copy(home = updateUrl.trim())
        }
        UpdateSourceStore.save(ctx, next)
        updateSources = next
        return next
    }

    fun checkUpdate() {
        val sources = storeSources()
        updateBusy = true
        updateProgress = -1
        updateStatus = "正在检查更新…"
        pendingFeed = null
        scope.launch {
            try {
                val feed = withContext(Dispatchers.IO) { ApkUpdate.fetchFeed(sources.url()) }
                pendingFeed = feed
                updateNotes = feed.notes
                updateStatus = if (feed.versionCode > versionCode) {
                    "发现新版本 ${feed.versionName}（当前 $versionName）"
                } else {
                    "当前版本 $versionName，已是最新"
                }
            } catch (e: Exception) {
                updateStatus = "检查更新失败：${e.message}"
            } finally {
                updateBusy = false
            }
        }
    }

    fun installUpdate() {
        val feed = pendingFeed ?: return
        if (feed.versionCode <= versionCode) return
        if (activity != null && !ApkUpdate.canInstallPackages(ctx)) {
            updateStatus = "请允许本应用安装未知来源应用，然后再点立即安装"
            ApkUpdate.requestInstallPermission(activity)
            return
        }
        updateBusy = true
        updateProgress = 0
        updateStatus = "正在下载 ${feed.versionName}…"
        scope.launch {
            try {
                val dest = ApkUpdate.updateFile(ctx)
                withContext(Dispatchers.IO) {
                    ApkUpdate.download(feed.apkUrl, dest) { p ->
                        mainHandler.post { updateProgress = p }
                    }
                }
                updateStatus = "正在打开系统安装界面…"
                ApkUpdate.install(ctx, dest)
            } catch (e: Exception) {
                updateStatus = "下载/安装失败：${e.message}"
            } finally {
                updateBusy = false
                updateProgress = -1
            }
        }
    }

    fun addDirect() {
        val norm = normalizeUrl(directInput) ?: return
        onAddDirect(norm)
        directInput = ""
    }

    val dsh = LocalDsh.current
    Scaffold(
        containerColor = dsh.base,
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        "Harness Portable",
                        fontWeight = FontWeight.SemiBold,
                        fontSize = 17.sp
                    )
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = dsh.base,
                    titleContentColor = dsh.textPrimary
                )
            )
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .background(dsh.base)
                .padding(padding),
            contentPadding = PaddingValues(16.dp, 8.dp, 16.dp, 24.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            // ---------- SSH tunnels ----------
            item {
                SectionHeader(
                    text = "SSH 隧道",
                    trailing = "${tunnels.size}",
                    action = {
                        IconButton(onClick = { editorFor = null; showEditor = true }) {
                            Icon(
                                Icons.Filled.Add,
                                contentDescription = "添加隧道",
                                tint = dsh.textSecondary
                            )
                        }
                    }
                )
            }

            if (tunnels.isEmpty()) {
                item {
                    Text(
                        "还没有隧道。点右上角 + 添加：填服务器 IP / 域名 / NetBIOS 名、" +
                                "SSH 端口、用户名（如 administrator），转发到 127.0.0.1:3080。",
                        color = dsh.textTertiary,
                        fontSize = 13.sp,
                        modifier = Modifier.padding(horizontal = 2.dp)
                    )
                }
            }

            items(tunnels, key = { it.id }) { p ->
                TunnelRow(
                    profile = p,
                    running = p.id == runningTunnelId,
                    onConnect = { onConnectTunnel(p) },
                    onEdit = { editorFor = p; showEditor = true },
                    onDelete = { onDeleteTunnel(p) }
                )
            }

            // ---------- direct URLs ----------
            item {
                SectionHeader(text = "直连", modifier = Modifier.padding(top = 12.dp))
                LanMachineField(
                    value = directInput,
                    onValueChange = { directInput = it },
                    label = "IP、机器名或完整 URL",
                    placeholder = "192.168.0.104",
                    keyboardOptions = KeyboardOptions(
                        keyboardType = KeyboardType.Uri,
                        imeAction = ImeAction.Done
                    ),
                    keyboardActions = KeyboardActions(onDone = { addDirect() }),
                    trailingAction = {
                        IconButton(onClick = { addDirect() }) {
                            Icon(
                                Icons.Filled.Add,
                                contentDescription = "添加",
                                tint = dsh.accent
                            )
                        }
                    },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 6.dp)
                )
            }

            if (directs.isEmpty()) {
                item {
                    Text(
                        "也可直接添加 http(s) 地址，不经过 SSH 隧道访问。",
                        color = dsh.textTertiary,
                        fontSize = 13.sp,
                        modifier = Modifier.padding(horizontal = 2.dp)
                    )
                }
            }

            items(directs, key = { it }) { url ->
                ServerRow(
                    url = url,
                    onConnect = { onConnectDirect(url) },
                    onDelete = { onDeleteDirect(url) }
                )
            }

            item {
                Column(
                    modifier = Modifier
                        .padding(top = 12.dp)
                        .fillMaxWidth()
                        .dshCard()
                        .padding(16.dp)
                ) {
                    SectionLabel("后台保活")
                    val exempt = BatteryExemption.isExempt(ctx)
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier.padding(top = 8.dp)
                    ) {
                        StatusDot(if (exempt) dsh.success else dsh.warning)
                        Spacer(Modifier.size(8.dp))
                        Text(
                            if (exempt) "已忽略电池优化" else "未忽略电池优化",
                            fontSize = 14.sp,
                            fontWeight = FontWeight.Medium,
                            color = dsh.textPrimary
                        )
                    }
                    Text(
                        if (exempt)
                            "已忽略电池优化，SSH 隧道可以在切到后台后继续运行。"
                        else
                            "未忽略电池优化。系统可能在切到后台后几分钟内杀掉隧道，" +
                                "下次打开就会看到「隧道未连接」。",
                        color = dsh.textTertiary,
                        fontSize = 12.sp,
                        modifier = Modifier.padding(top = 6.dp, bottom = 4.dp)
                    )
                    if (!exempt) {
                        BrandButton(
                            text = "允许后台运行",
                            onClick = { BatteryExemption.request(ctx) },
                            modifier = Modifier.padding(top = 10.dp)
                        )
                    }
                }
            }

            item {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .dshCard()
                        .padding(16.dp)
                ) {
                    SectionLabel("自动更新")
                    Text(
                        "当前版本 $versionName ($versionCode)",
                        color = dsh.textTertiary,
                        fontSize = 12.sp,
                        fontFamily = DshMonoFontFamily,
                        modifier = Modifier.padding(top = 6.dp, bottom = 10.dp)
                    )
                    OutlinedTextField(
                        value = updateUrl,
                        onValueChange = { updateUrl = it },
                        label = {
                            Text(
                                if (updateSources.selected == UpdateSourceKind.GitHub)
                                    "GitHub 仓库"
                                else "家庭目录（局域网路径或 http 地址）"
                            )
                        },
                        singleLine = true,
                        shape = DshControlShape,
                        textStyle = dsh.monoStyle(),
                        colors = dshFieldColors(),
                        modifier = Modifier.fillMaxWidth()
                    )
                    Text(
                        "使用：" + updateSources.selected.let {
                            if (it == UpdateSourceKind.GitHub) "GitHub" else "家庭目录"
                        },
                        color = dsh.textTertiary,
                        fontSize = 12.sp,
                        modifier = Modifier.padding(top = 8.dp)
                    )
                    Row(
                        modifier = Modifier.padding(top = 8.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        UpdateSourceKind.entries.forEach { kind ->
                            if (kind == updateSources.selected) {
                                Button(
                                    onClick = {},
                                    enabled = !updateBusy,
                                    shape = DshControlShape,
                                    colors = ButtonDefaults.buttonColors(
                                        containerColor = dsh.brandFill,
                                        contentColor = dsh.brandText
                                    )
                                ) { Text(kind.label()) }
                            } else {
                                OutlinedButton(
                                    onClick = {
                                        // Keep what is typed against the source it
                                        // belongs to, then swap the field over.
                                        val kept = storeSources()
                                        updateSources = kept.copy(selected = kind)
                                        updateUrl = kept.url(kind)
                                        UpdateSourceStore.save(ctx, updateSources)
                                        updateStatus = "已切换到${kind.label()}"
                                        pendingFeed = null
                                    },
                                    enabled = !updateBusy,
                                    shape = DshControlShape,
                                    colors = ButtonDefaults.outlinedButtonColors(
                                        contentColor = dsh.textSecondary
                                    )
                                ) { Text(kind.label()) }
                            }
                        }
                    }
                    Text(
                        "将请求：" + runCatching { updateSources.feedUrl() }.getOrDefault(""),
                        color = dsh.textTertiary,
                        fontSize = 11.sp,
                        fontFamily = DshMonoFontFamily,
                        modifier = Modifier.padding(top = 8.dp)
                    )
                    Row(
                        modifier = Modifier.padding(top = 10.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        OutlinedButton(
                            onClick = {
                                storeSources()
                                updateStatus = "已保存更新地址"
                            },
                            enabled = !updateBusy,
                            shape = DshControlShape,
                            colors = ButtonDefaults.outlinedButtonColors(
                                contentColor = dsh.textSecondary
                            )
                        ) { Text("保存") }
                        BrandButton(
                            text = "检查更新",
                            onClick = { checkUpdate() },
                            enabled = !updateBusy
                        )
                        BrandButton(
                            text = "立即安装",
                            onClick = { installUpdate() },
                            enabled = !updateBusy && (pendingFeed?.versionCode ?: 0) > versionCode
                        )
                    }
                    if (updateProgress >= 0) {
                        LinearProgressIndicator(
                            progress = { updateProgress / 100f },
                            color = dsh.accent,
                            trackColor = dsh.layer3,
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(top = 10.dp)
                        )
                    }
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier.padding(top = 10.dp)
                    ) {
                        StatusDot(updateStatusColor(updateStatus, dsh))
                        Spacer(Modifier.size(8.dp))
                        Text(
                            updateStatus,
                            color = dsh.textSecondary,
                            fontSize = 12.sp
                        )
                    }
                    if (updateNotes.isNotBlank()) {
                        Text(
                            updateNotes,
                            fontSize = 12.sp,
                            color = dsh.textSecondary,
                            modifier = Modifier.padding(top = 8.dp)
                        )
                    }
                    Text(
                        "两个地址都保留，切换不丢。家庭目录是局域网共享路径" +
                            "（\\\\server-home\\public\\Software\\HarnessPortable-Releases），" +
                            "手机读不了 UNC，要在手机上用它请填一个同目录的 http 地址。" +
                            "手机默认走 GitHub。",
                        color = dsh.textTertiary,
                        fontSize = 12.sp,
                        modifier = Modifier.padding(top = 10.dp)
                    )
                }
            }
        }
    }

    if (showEditor) {
        // Stored value is read once per opened editor: editing a profile must
        // not re-run keystore decryption on every recomposition.
        val initialAuthInput = remember(editorFor) {
            editorFor?.let { SecureStore.getAuthInput(ctx, it.id) }
        }
        TunnelEditorDialog(
            initial = editorFor,
            initialAuthInput = initialAuthInput,
            onDismiss = { showEditor = false },
            onSave = { profile, password, authInput ->
                if (editorFor == null) onAddTunnel(profile, password, authInput)
                else onUpdateTunnel(profile, password, authInput)
                showEditor = false
            }
        )
    }
}

// ---------------------------------------------------------------------------
// Token building blocks
// ---------------------------------------------------------------------------

/** Small tertiary label used for every section heading. */
@Composable
private fun SectionLabel(text: String, modifier: Modifier = Modifier) {
    Text(
        text,
        color = LocalDsh.current.textTertiary,
        fontSize = 12.sp,
        fontWeight = FontWeight.SemiBold,
        letterSpacing = 0.6.sp,
        modifier = modifier
    )
}

/** Section heading with an optional count and an optional trailing control. */
@Composable
private fun SectionHeader(
    text: String,
    trailing: String? = null,
    modifier: Modifier = Modifier,
    action: (@Composable () -> Unit)? = null
) {
    val dsh = LocalDsh.current
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = modifier.fillMaxWidth()
    ) {
        SectionLabel(text)
        if (trailing != null) {
            Spacer(Modifier.size(8.dp))
            Text(
                trailing,
                color = dsh.textTertiary,
                fontSize = 11.sp,
                fontFamily = DshMonoFontFamily
            )
        }
        Spacer(Modifier.weight(1f))
        action?.invoke()
    }
}

/** Status as a small colour dot plus label, never as coloured prose. */
@Composable
private fun StatusDot(color: Color, size: Int = 7) {
    Box(
        modifier = Modifier
            .size(size.dp)
            .background(color, CircleShape)
    )
}

/**
 * Prefixes are stable across [SettingsScreen]'s own status strings, so the dot
 * colour is derived from the message rather than by changing any of them.
 */
private fun updateStatusColor(status: String, dsh: DshColors): Color = when {
    status.startsWith("检查更新失败") ||
        status.startsWith("下载/安装失败") -> dsh.danger
    status.startsWith("发现新版本") ||
        status.startsWith("请允许本应用安装") -> dsh.warning
    status.startsWith("正在") -> dsh.accent
    status.startsWith("当前版本") -> dsh.success
    else -> dsh.textTertiary
}

/** The one primary action style: brand fill with inverted label. */
@Composable
private fun BrandButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true
) {
    val dsh = LocalDsh.current
    Button(
        onClick = onClick,
        enabled = enabled,
        shape = DshControlShape,
        colors = ButtonDefaults.buttonColors(
            containerColor = dsh.brandFill,
            contentColor = dsh.brandText
        ),
        modifier = modifier
    ) { Text(text) }
}

// ---------------------------------------------------------------------------
// Rows
// ---------------------------------------------------------------------------

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun LanMachineField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    placeholder: String? = null,
    keyboardOptions: KeyboardOptions = KeyboardOptions.Default,
    keyboardActions: KeyboardActions = KeyboardActions.Default,
    trailingAction: (@Composable () -> Unit)? = null,
    modifier: Modifier = Modifier
) {
    var expanded by remember { mutableStateOf(false) }
    var machines by remember { mutableStateOf<List<HostResolver.LanMachine>>(emptyList()) }
    var discovering by remember { mutableStateOf(false) }
    var hasScanned by remember { mutableStateOf(false) }
    var refreshRequest by remember { mutableStateOf(0) }

    LaunchedEffect(expanded, refreshRequest) {
        if (!expanded || (hasScanned && refreshRequest == 0)) return@LaunchedEffect

        discovering = true
        try {
            val result = withContext(Dispatchers.IO) {
                HostResolver.discoverLanMachines()
            }
            machines = result
            hasScanned = true
            refreshRequest = 0
        } finally {
            discovering = false
        }
    }

    val filter = machineFilterText(value)
    val filtered = machines
        .filter { filter.isEmpty() || it.name.contains(filter, ignoreCase = true) }
        .take(12)
    val dsh = LocalDsh.current

    ExposedDropdownMenuBox(
        expanded = expanded,
        onExpandedChange = { expanded = !expanded },
        modifier = modifier
    ) {
        OutlinedTextField(
            value = value,
            onValueChange = {
                onValueChange(it)
                expanded = true
            },
            label = { Text(label) },
            placeholder = placeholder?.let { { Text(it) } },
            singleLine = true,
            shape = DshControlShape,
            keyboardOptions = keyboardOptions,
            keyboardActions = keyboardActions,
            textStyle = dsh.monoStyle(),
            colors = dshFieldColors(),
            trailingIcon = {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    trailingAction?.invoke()
                    ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded)
                }
            },
            modifier = Modifier
                .menuAnchor(MenuAnchorType.PrimaryEditable)
                .fillMaxWidth()
        )

        ExposedDropdownMenu(
            expanded = expanded,
            onDismissRequest = { expanded = false },
            containerColor = dsh.layer1,
            shape = DshControlShape,
            modifier = Modifier.border(
                1.dp,
                dsh.borderOver(dsh.borderL2, dsh.layer1),
                DshControlShape
            )
        ) {
            if (discovering) {
                DropdownMenuItem(
                    text = { Text("正在搜索局域网机器…", color = dsh.textTertiary) },
                    onClick = {},
                    enabled = false
                )
            }

            if (!discovering && filtered.isEmpty()) {
                DropdownMenuItem(
                    text = {
                        Text(
                            if (hasScanned) "未发现机器名，可继续手动输入"
                            else "打开菜单开始搜索",
                            color = dsh.textTertiary
                        )
                    },
                    onClick = {},
                    enabled = false
                )
            }

            filtered.forEach { machine ->
                DropdownMenuItem(
                    text = {
                        Column {
                            Text(
                                machine.name,
                                fontSize = 14.sp,
                                fontFamily = DshMonoFontFamily,
                                color = dsh.textPrimary
                            )
                            if (machine.ip.isNotBlank()) {
                                Text(
                                    machine.ip,
                                    fontSize = 11.sp,
                                    fontFamily = DshMonoFontFamily,
                                    color = dsh.textTertiary
                                )
                            }
                        }
                    },
                    onClick = {
                        onValueChange(machine.name)
                        expanded = false
                    }
                )
            }

            DropdownMenuItem(
                text = { Text("刷新局域网机器", color = dsh.accent) },
                onClick = {
                    refreshRequest++
                    expanded = true
                }
            )
        }
    }
}

private fun machineFilterText(value: String): String {
    var host = value.trim()
    host = host.substringAfter("://", host)
    host = host.substringAfterLast('@')
    host = host.substringBefore('/')
    host = host.substringBefore(':')
    return host
}

/**
 * A list row rather than an elevated card: layer-1 fill, 1dp rank-1 border,
 * 12dp radius, 14-16dp padding, no shadow and no tonal elevation.
 */
@Composable
private fun TunnelRow(
    profile: TunnelProfile,
    running: Boolean,
    onConnect: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit
) {
    val dsh = LocalDsh.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .dshCard()
            .clickable(onClick = onConnect)
            .padding(start = 16.dp, end = 4.dp, top = 12.dp, bottom = 12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    profile.name.ifBlank { profile.sshHost },
                    fontWeight = FontWeight.Medium,
                    fontSize = 15.sp,
                    color = dsh.textPrimary
                )
                if (running) {
                    Spacer(Modifier.size(8.dp))
                    Box(
                        modifier = Modifier.size(7.dp).background(dsh.accent, CircleShape)
                    )
                    Spacer(Modifier.size(5.dp))
                    Text(
                        "已连接",
                        color = dsh.accent,
                        fontSize = 11.sp,
                        fontWeight = FontWeight.SemiBold
                    )
                }
            }
            Text(
                "${profile.user}@${profile.sshHost}:${profile.sshPort} → " +
                        "${profile.remoteHost}:${profile.remotePort}",
                fontSize = 12.sp,
                fontFamily = DshMonoFontFamily,
                color = dsh.textSecondary
            )
            Text(
                "本地端口 ${profile.localPort}",
                fontSize = 11.sp,
                fontFamily = DshMonoFontFamily,
                color = dsh.textTertiary,
                modifier = Modifier.padding(top = 2.dp)
            )
        }
        IconButton(onClick = onEdit) {
            Icon(Icons.Filled.Edit, contentDescription = "编辑", tint = dsh.textTertiary)
        }
        IconButton(onClick = onDelete) {
            Icon(Icons.Filled.Delete, contentDescription = "删除", tint = dsh.danger)
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ServerRow(
    url: String,
    onConnect: () -> Unit,
    onDelete: () -> Unit
) {
    val dsh = LocalDsh.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .dshCard()
            .clickable(onClick = onConnect)
            .padding(start = 16.dp, end = 4.dp, top = 12.dp, bottom = 12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                hostOf(url),
                fontWeight = FontWeight.Medium,
                fontSize = 15.sp,
                fontFamily = DshMonoFontFamily,
                color = dsh.textPrimary
            )
            Text(
                url,
                fontSize = 12.sp,
                fontFamily = DshMonoFontFamily,
                color = dsh.textTertiary
            )
        }
        IconButton(onClick = onDelete) {
            Icon(
                Icons.Filled.Delete,
                contentDescription = "删除",
                tint = dsh.danger
            )
        }
    }
}

// ---------------------------------------------------------------------------
// Editors
// ---------------------------------------------------------------------------

@Composable
private fun TunnelEditorDialog(
    initial: TunnelProfile?,
    initialAuthInput: String?,
    onDismiss: () -> Unit,
    onSave: (TunnelProfile, String?, String?) -> Unit
) {
    var name by remember { mutableStateOf(initial?.name ?: "") }
    var host by remember { mutableStateOf(initial?.sshHost ?: "") }
    var port by remember { mutableStateOf((initial?.sshPort ?: 22).toString()) }
    var user by remember { mutableStateOf(initial?.user ?: "") }
    var pass by remember { mutableStateOf("") }
    var passVisible by remember { mutableStateOf(false) }
    var identityFile by remember { mutableStateOf(initial?.identityFile ?: "") }
    var pendingIdentityUri by remember { mutableStateOf<Uri?>(null) }
    val ctx = LocalContext.current
    val pickKey = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri ->
        if (uri != null) {
            pendingIdentityUri = uri
            identityFile = uri.lastPathSegment
                ?.substringAfterLast(':')
                ?.substringAfterLast('/')
                ?: "已选择私钥"
        }
    }
    var authInput by remember { mutableStateOf(initialAuthInput ?: "") }
    var remoteHost by remember { mutableStateOf(initial?.remoteHost ?: "127.0.0.1") }
    var remotePort by remember { mutableStateOf((initial?.remotePort ?: 3080).toString()) }
    var localPort by remember { mutableStateOf((initial?.localPort ?: 3080).toString()) }
    var authMode by remember {
        mutableStateOf(
            if (initial?.authMode == TunnelProfile.AUTH_MODE_MANUAL)
                TunnelProfile.AUTH_MODE_MANUAL else TunnelProfile.AUTH_MODE_NSSM
        )
    }

    fun portOr(v: String, def: Int) = v.trim().toIntOrNull() ?: def
    val valid = host.isNotBlank() && user.isNotBlank() &&
            remoteHost.isNotBlank() &&
            portOr(port, 0) in 1..65535 &&
            portOr(remotePort, 0) in 1..65535 &&
            portOr(localPort, 0) in 1..65535

    val dsh = LocalDsh.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                if (initial == null) "添加 SSH 隧道" else "编辑 SSH 隧道",
                fontSize = 16.sp,
                fontWeight = FontWeight.SemiBold,
                color = dsh.textPrimary
            )
        },
        text = {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
            OutlinedTextField(
                name, { name = it },
                label = { Text("名称（可选）") },
                singleLine = true,
                shape = DshControlShape,
                colors = dshFieldColors()
            )
            LanMachineField(
                value = host,
                onValueChange = { host = it },
                label = "服务器地址",
                placeholder = "IP、域名或机器名",
                modifier = Modifier.fillMaxWidth()
            )
            OutlinedTextField(
                port, { port = it }, label = { Text("SSH 端口") }, singleLine = true,
                shape = DshControlShape,
                textStyle = dsh.monoStyle(),
                colors = dshFieldColors(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
            )
            OutlinedTextField(
                user, { user = it }, label = { Text("用户名") }, singleLine = true,
                shape = DshControlShape,
                colors = dshFieldColors()
            )
            OutlinedTextField(
                pass, { pass = it },
                label = {
                    Text(
                        if (initial == null) "密码（可稍后输入；密钥登录可留空）"
                        else "密码（留空保持不变；密钥登录可留空）"
                    )
                },
                singleLine = true,
                shape = DshControlShape,
                colors = dshFieldColors(),
                visualTransformation =
                    if (passVisible) VisualTransformation.None else PasswordVisualTransformation(),
                trailingIcon = {
                    TextButton(onClick = { passVisible = !passVisible }) {
                        Text(if (passVisible) "隐藏" else "显示")
                    }
                }
            )
            OutlinedTextField(
                identityFile,
                {
                    identityFile = it
                    if (it.isBlank()) pendingIdentityUri = null
                },
                label = { Text("私钥文件（可选）") },
                singleLine = true,
                shape = DshControlShape,
                textStyle = dsh.monoStyle(),
                colors = dshFieldColors(),
                modifier = Modifier.fillMaxWidth()
            )
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TextButton(onClick = { pickKey.launch(arrayOf("*/*")) }) {
                    Text("选择私钥", color = dsh.accent)
                }
                if (identityFile.isNotBlank()) {
                    TextButton(onClick = {
                        identityFile = ""
                        pendingIdentityUri = null
                    }) { Text("清除", color = dsh.danger) }
                }
            }
            OutlinedTextField(
                remoteHost, { remoteHost = it },
                label = { Text("远程地址（服务器侧）") }, singleLine = true,
                shape = DshControlShape,
                textStyle = dsh.monoStyle(),
                colors = dshFieldColors()
            )
            OutlinedTextField(
                remotePort, { remotePort = it }, label = { Text("远程端口") }, singleLine = true,
                shape = DshControlShape,
                textStyle = dsh.monoStyle(),
                colors = dshFieldColors(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
            )
            OutlinedTextField(
                localPort, { localPort = it }, label = { Text("本地端口") }, singleLine = true,
                shape = DshControlShape,
                textStyle = dsh.monoStyle(),
                colors = dshFieldColors(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
            )
            SectionLabel("Web 登录方式")
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilterChip(
                    selected = authMode == TunnelProfile.AUTH_MODE_NSSM,
                    onClick = { authMode = TunnelProfile.AUTH_MODE_NSSM },
                    label = { Text("NSSM 自动") },
                    shape = DshControlShape,
                    colors = FilterChipDefaults.filterChipColors(
                        containerColor = dsh.inputBg,
                        labelColor = dsh.textSecondary,
                        selectedContainerColor = dsh.layer3,
                        selectedLabelColor = dsh.textPrimary
                    ),
                    border = FilterChipDefaults.filterChipBorder(
                        enabled = true,
                        selected = authMode == TunnelProfile.AUTH_MODE_NSSM,
                        borderColor = dsh.borderOver(dsh.inputBorder, dsh.inputBg),
                        selectedBorderColor = dsh.borderOver(dsh.borderL2, dsh.layer3)
                    )
                )
                FilterChip(
                    selected = authMode == TunnelProfile.AUTH_MODE_MANUAL,
                    onClick = { authMode = TunnelProfile.AUTH_MODE_MANUAL },
                    label = { Text("手动输入") },
                    shape = DshControlShape,
                    colors = FilterChipDefaults.filterChipColors(
                        containerColor = dsh.inputBg,
                        labelColor = dsh.textSecondary,
                        selectedContainerColor = dsh.layer3,
                        selectedLabelColor = dsh.textPrimary
                    ),
                    border = FilterChipDefaults.filterChipBorder(
                        enabled = true,
                        selected = authMode == TunnelProfile.AUTH_MODE_MANUAL,
                        borderColor = dsh.borderOver(dsh.inputBorder, dsh.inputBg),
                        selectedBorderColor = dsh.borderOver(dsh.borderL2, dsh.layer3)
                    )
                )
            }
            if (authMode == TunnelProfile.AUTH_MODE_MANUAL) {
                OutlinedTextField(
                    value = authInput,
                    onValueChange = { authInput = it },
                    label = { Text("Web 认证 URL 或 token") },
                    singleLine = true,
                    shape = DshControlShape,
                    textStyle = dsh.monoStyle(),
                    colors = dshFieldColors(),
                    modifier = Modifier.fillMaxWidth()
                )
                Text(
                    "粘贴服务器上 dsh web 打印的带 token 的 URL，或直接粘贴 token 本身。" +
                            "留空则连接后在页面上粘贴。",
                    fontSize = 12.sp,
                    color = dsh.textTertiary
                )
            }
            Text(
                "等价于 ssh -N -L 本地端口:远程地址:远程端口 用户名@服务器\n" +
                        "服务器可填 IP / 域名 / NetBIOS 名（如 winserver）：局域网自动发现；" +
                        "装有 Tailscale 并开启 MagicDNS 时自动解析到 Tailscale IP。\n" +
                        "未填密码时可导入 OpenSSH 私钥登录。密码错误只尝试一次，不会反复重连。\n" +
                        "密码与认证 URL/token 用 Android Keystore 加密存储，不写入 tunnels 配置。\n" +
                        "NSSM 方式：每次连接后自动在服务器上定位 NSSM 托管的 dsh web 日志并自动登录；" +
                        "手动方式：连接时用上面保存的认证 URL/token，留空则页面提示需要认证时粘贴 URL，" +
                        "登录后凭 cookie 自动保持约 30 天。",
                fontSize = 12.sp,
                color = dsh.textTertiary
            )
            }
        },
        confirmButton = {
            TextButton(
                enabled = valid,
                onClick = {
                    val id = initial?.id ?: java.util.UUID.randomUUID().toString()
                    val storedIdentity = try {
                        when {
                            pendingIdentityUri != null ->
                                SshIdentityFiles.import(ctx, id, pendingIdentityUri!!)
                            identityFile.isBlank() -> {
                                SshIdentityFiles.deleteManaged(ctx, id)
                                ""
                            }
                            else -> identityFile.trim()
                        }
                    } catch (_: Exception) {
                        initial?.identityFile ?: ""
                    }
                    onSave(
                        TunnelProfile(
                            id = id,
                            name = name.trim().ifBlank { host.trim() },
                            sshHost = host.trim(),
                            sshPort = portOr(port, 22),
                            user = user.trim(),
                            remoteHost = remoteHost.trim(),
                            remotePort = portOr(remotePort, 3080),
                            localPort = portOr(localPort, 3080),
                            authMode = authMode,
                            identityFile = storedIdentity
                        ),
                        pass.takeIf { it.isNotEmpty() },
                        authInput.trim().takeIf { it.isNotEmpty() }
                    )
                }
            ) { Text("保存") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("取消", color = dsh.textSecondary)
            }
        },
        shape = DshCardShape,
        containerColor = MaterialTheme.colorScheme.surfaceContainerLow,
        titleContentColor = dsh.textPrimary,
        textContentColor = dsh.textSecondary
    )
}

internal fun normalizeUrl(raw: String): String? {
    val s = raw.trim()
    if (s.isEmpty()) return null
    return when {
        s.startsWith("http://") || s.startsWith("https://") || s.contains("://") -> s
        else -> {
            val withPort = if (s.contains(":")) s else "$s:4096"
            "http://$withPort"
        }
    }
}

internal fun hostOf(url: String): String {
    return try {
        val u = java.net.URI(url)
        u.host?.takeIf { it.isNotEmpty() } ?: url
    } catch (e: Exception) {
        url
    }
}
