package com.harness.portable

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
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
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MenuAnchorType
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
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
    var updateServer by remember { mutableStateOf(ApkUpdate.storedServer(ctx)) }
    var updateStatus by remember { mutableStateOf("尚未检查更新") }
    var updateNotes by remember { mutableStateOf("") }
    var updateBusy by remember { mutableStateOf(false) }
    var updateProgress by remember { mutableStateOf(-1) }
    var pendingFeed by remember { mutableStateOf<ApkFeed?>(null) }
    val versionName = remember { ApkUpdate.currentVersionName(ctx) }
    val versionCode = remember { ApkUpdate.currentVersionCode(ctx) }

    fun checkUpdate() {
        ApkUpdate.saveServer(ctx, updateServer)
        updateBusy = true
        updateProgress = -1
        updateStatus = "正在检查更新…"
        pendingFeed = null
        scope.launch {
            try {
                val feed = withContext(Dispatchers.IO) { ApkUpdate.fetchFeed(updateServer) }
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

    Scaffold(
        topBar = { TopAppBar(title = { Text("Harness Portable") }) }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            // ---------- SSH tunnels ----------
            item {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("SSH 隧道", fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                    Spacer(Modifier.size(8.dp))
                    Text(
                        "${tunnels.size}",
                        color = MaterialTheme.colorScheme.outline,
                        fontSize = 12.sp
                    )
                    Spacer(Modifier.weight(1f))
                    IconButton(onClick = { editorFor = null; showEditor = true }) {
                        Icon(Icons.Filled.Add, contentDescription = "添加隧道")
                    }
                }
            }

            if (tunnels.isEmpty()) {
                item {
                    Text(
                        "还没有隧道。点右上角 + 添加：填服务器 IP / 域名 / NetBIOS 名、" +
                                "SSH 端口、用户名（如 administrator），转发到 127.0.0.1:3080。",
                        color = MaterialTheme.colorScheme.outline,
                        fontSize = 13.sp
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
                Spacer(Modifier.height(8.dp))
                HorizontalDivider()
                Spacer(Modifier.height(8.dp))
                Text("直连", fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                Spacer(Modifier.height(6.dp))
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
                            Icon(Icons.Filled.Add, contentDescription = "添加")
                        }
                    },
                    modifier = Modifier.fillMaxWidth()
                )
            }

            if (directs.isEmpty()) {
                item {
                    Text(
                        "也可直接添加 http(s) 地址，不经过 SSH 隧道访问。",
                        color = MaterialTheme.colorScheme.outline,
                        fontSize = 13.sp
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
                Spacer(Modifier.height(8.dp))
                HorizontalDivider()
                Spacer(Modifier.height(8.dp))
                Text("自动更新", fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                Text(
                    "当前版本 $versionName ($versionCode)",
                    color = MaterialTheme.colorScheme.outline,
                    fontSize = 12.sp,
                    modifier = Modifier.padding(top = 4.dp, bottom = 8.dp)
                )
                OutlinedTextField(
                    value = updateServer,
                    onValueChange = { updateServer = it },
                    label = { Text("更新服务器（GitHub 仓库或 android.json 地址）") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                Row(
                    modifier = Modifier.padding(top = 8.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedButton(
                        onClick = {
                            ApkUpdate.saveServer(ctx, updateServer)
                            updateStatus = "已保存更新服务器"
                        },
                        enabled = !updateBusy
                    ) { Text("保存") }
                    Button(onClick = { checkUpdate() }, enabled = !updateBusy) {
                        Text("检查更新")
                    }
                    Button(
                        onClick = { installUpdate() },
                        enabled = !updateBusy && (pendingFeed?.versionCode ?: 0) > versionCode
                    ) { Text("立即安装") }
                }
                if (updateProgress >= 0) {
                    LinearProgressIndicator(
                        progress = { updateProgress / 100f },
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(top = 8.dp)
                    )
                }
                Text(
                    updateStatus,
                    color = MaterialTheme.colorScheme.outline,
                    fontSize = 12.sp,
                    modifier = Modifier.padding(top = 8.dp)
                )
                if (updateNotes.isNotBlank()) {
                    Text(
                        updateNotes,
                        fontSize = 12.sp,
                        modifier = Modifier.padding(top = 8.dp)
                    )
                }
                Text(
                    "安装包发布在 GitHub Release 与局域网 HarnessPortable-Releases。手机默认走 GitHub；若有 http 目录，把地址填到上面。",
                    color = MaterialTheme.colorScheme.outline,
                    fontSize = 12.sp,
                    modifier = Modifier.padding(top = 8.dp)
                )
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
            keyboardOptions = keyboardOptions,
            keyboardActions = keyboardActions,
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
            onDismissRequest = { expanded = false }
        ) {
            if (discovering) {
                DropdownMenuItem(
                    text = { Text("正在搜索局域网机器…") },
                    onClick = {},
                    enabled = false
                )
            }

            if (!discovering && filtered.isEmpty()) {
                DropdownMenuItem(
                    text = {
                        Text(
                            if (hasScanned) "未发现机器名，可继续手动输入"
                            else "打开菜单开始搜索"
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
                            Text(machine.name)
                            if (machine.ip.isNotBlank()) {
                                Text(
                                    machine.ip,
                                    fontSize = 11.sp,
                                    color = MaterialTheme.colorScheme.outline
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
                text = { Text("刷新局域网机器") },
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

@Composable
private fun TunnelRow(
    profile: TunnelProfile,
    running: Boolean,
    onConnect: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit
) {
    Surface(
        onClick = onConnect,
        shape = RoundedCornerShape(12.dp),
        tonalElevation = 1.dp,
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            modifier = Modifier.padding(start = 16.dp, end = 4.dp, top = 12.dp, bottom = 12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        profile.name.ifBlank { profile.sshHost },
                        fontWeight = FontWeight.Medium,
                        fontSize = 15.sp
                    )
                    if (running) {
                        Spacer(Modifier.size(8.dp))
                        Text(
                            "● 已连接",
                            color = MaterialTheme.colorScheme.primary,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.SemiBold
                        )
                    }
                }
                Text(
                    "${profile.user}@${profile.sshHost}:${profile.sshPort} → " +
                            "${profile.remoteHost}:${profile.remotePort}",
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.outline
                )
                Text(
                    "本地端口 ${profile.localPort}",
                    fontSize = 11.sp,
                    color = MaterialTheme.colorScheme.outline
                )
            }
            IconButton(onClick = onEdit) {
                Icon(Icons.Filled.Edit, contentDescription = "编辑", tint = MaterialTheme.colorScheme.outline)
            }
            IconButton(onClick = onDelete) {
                Icon(Icons.Filled.Delete, contentDescription = "删除", tint = MaterialTheme.colorScheme.outline)
            }
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
    Surface(
        onClick = onConnect,
        shape = RoundedCornerShape(12.dp),
        tonalElevation = 1.dp,
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(hostOf(url), fontWeight = FontWeight.Medium, fontSize = 15.sp)
                Text(url, fontSize = 12.sp, color = MaterialTheme.colorScheme.outline)
            }
            IconButton(onClick = onDelete) {
                Icon(
                    Icons.Filled.Delete,
                    contentDescription = "删除",
                    tint = MaterialTheme.colorScheme.outline
                )
            }
        }
    }
}

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

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(if (initial == null) "添加 SSH 隧道" else "编辑 SSH 隧道") },
        text = {
            Column(
                modifier = Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                OutlinedTextField(name, { name = it }, label = { Text("名称（可选）") }, singleLine = true)
                LanMachineField(
                    value = host,
                    onValueChange = { host = it },
                    label = "服务器地址",
                    placeholder = "IP、域名或机器名",
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    port, { port = it }, label = { Text("SSH 端口") }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
                )
                OutlinedTextField(user, { user = it }, label = { Text("用户名") }, singleLine = true)
                OutlinedTextField(
                    pass, { pass = it },
                    label = {
                        Text(
                            if (initial == null) "密码（可稍后输入；密钥登录可留空）"
                            else "密码（留空保持不变；密钥登录可留空）"
                        )
                    },
                    singleLine = true,
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
                    modifier = Modifier.fillMaxWidth()
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TextButton(onClick = { pickKey.launch(arrayOf("*/*")) }) {
                        Text("选择私钥")
                    }
                    if (identityFile.isNotBlank()) {
                        TextButton(onClick = {
                            identityFile = ""
                            pendingIdentityUri = null
                        }) { Text("清除") }
                    }
                }
                OutlinedTextField(
                    remoteHost, { remoteHost = it },
                    label = { Text("远程地址（服务器侧）") }, singleLine = true
                )
                OutlinedTextField(
                    remotePort, { remotePort = it }, label = { Text("远程端口") }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
                )
                OutlinedTextField(
                    localPort, { localPort = it }, label = { Text("本地端口") }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
                )
                Text("Web 登录方式", fontSize = 12.sp, color = MaterialTheme.colorScheme.outline)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(
                        selected = authMode == TunnelProfile.AUTH_MODE_NSSM,
                        onClick = { authMode = TunnelProfile.AUTH_MODE_NSSM },
                        label = { Text("NSSM 自动") }
                    )
                    FilterChip(
                        selected = authMode == TunnelProfile.AUTH_MODE_MANUAL,
                        onClick = { authMode = TunnelProfile.AUTH_MODE_MANUAL },
                        label = { Text("手动输入") }
                    )
                }
                if (authMode == TunnelProfile.AUTH_MODE_MANUAL) {
                    OutlinedTextField(
                        value = authInput,
                        onValueChange = { authInput = it },
                        label = { Text("Web 认证 URL 或 token") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    Text(
                        "粘贴服务器上 dsh web 打印的带 token 的 URL，或直接粘贴 token 本身。" +
                                "留空则连接后在页面上粘贴。",
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.outline
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
                    color = MaterialTheme.colorScheme.outline
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
            TextButton(onClick = onDismiss) { Text("取消") }
        }
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
