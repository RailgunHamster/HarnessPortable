package com.harness.portable

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.displayCutout
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
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
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Dns
import androidx.compose.material.icons.filled.Language
import androidx.compose.material.icons.filled.ArrowForward
import androidx.compose.material3.DropdownMenu
import androidx.compose.ui.text.style.TextOverflow
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
import androidx.compose.material3.Tab
import androidx.compose.material3.TabRow
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
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

// ---------------------------------------------------------------------------
// Token building blocks
// ---------------------------------------------------------------------------

/** Small tertiary label used for every section heading. */
@Composable
private fun SectionLabel(text: String, modifier: Modifier = Modifier) {
    Text(
        text,
        color = LocalDsh.current.textPrimary,
        fontSize = 16.sp,
        fontWeight = FontWeight.SemiBold,
        letterSpacing = 0.sp,
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
internal fun LanMachineField(
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
        ShellIcon(Icons.Filled.Dns)
        Spacer(Modifier.size(12.dp))
        Column(modifier = Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    profile.name.ifBlank { profile.sshHost },
                    fontWeight = FontWeight.Medium,
                    fontSize = 15.sp,
                    color = dsh.textPrimary,
                    modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis
                )
                if (running) {
                    Spacer(Modifier.size(8.dp))
                    ShellStatusPill("已连接", dsh.success)
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
        RowActions(onEdit = onEdit, onDelete = onDelete)
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
        ShellIcon(Icons.Filled.Language)
        Spacer(Modifier.size(12.dp))
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
        RowActions(onDelete = onDelete)
    }
}

// ---------------------------------------------------------------------------
// Editors
// ---------------------------------------------------------------------------

@Composable
internal fun TunnelEditorDialog(
    initial: TunnelProfile?,
    initialAuthInput: String?,
    onDismiss: () -> Unit,
    onSave: (TunnelProfile, String?, String?) -> Unit,
    onConnectAfterSaveChanged: (Boolean) -> Unit = {}
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
            when (initial?.authMode) {
                TunnelProfile.AUTH_MODE_MANUAL -> TunnelProfile.AUTH_MODE_MANUAL
                TunnelProfile.AUTH_MODE_SYSTEMD -> TunnelProfile.AUTH_MODE_SYSTEMD
                else -> TunnelProfile.AUTH_MODE_NSSM
            }
        )
    }
    var systemdUnit by remember { mutableStateOf(initial?.systemdUnit ?: "dsh-web.service") }
    var systemdScope by remember { mutableStateOf(initial?.systemdScope ?: "user") }
    var systemdLogPath by remember { mutableStateOf(initial?.systemdLogPath ?: "") }

    fun portOr(v: String, def: Int) = v.trim().toIntOrNull() ?: def
    val valid = host.isNotBlank() && user.isNotBlank() &&
            remoteHost.isNotBlank() &&
            portOr(port, 0) in 1..65535 &&
            portOr(remotePort, 0) in 1..65535 &&
            portOr(localPort, 0) in 1..65535

    fun saveProfile() {

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
                            identityFile = storedIdentity,
                            systemdUnit = systemdUnit.trim().ifBlank { "dsh-web.service" },
                            systemdScope = systemdScope,
                            systemdLogPath = systemdLogPath.trim()
                        ),
                        pass.takeIf { it.isNotEmpty() },
                        authInput.trim().takeIf { it.isNotEmpty() }
                    )
    }

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
            FilterChip(
                selected = authMode == TunnelProfile.AUTH_MODE_SYSTEMD,
                onClick = { authMode = TunnelProfile.AUTH_MODE_SYSTEMD },
                label = { Text("systemd 自动（Linux 服务器）") },
                shape = DshControlShape,
                colors = FilterChipDefaults.filterChipColors(
                    containerColor = dsh.inputBg,
                    labelColor = dsh.textSecondary,
                    selectedContainerColor = dsh.layer3,
                    selectedLabelColor = dsh.textPrimary
                ),
                border = FilterChipDefaults.filterChipBorder(
                    enabled = true,
                    selected = authMode == TunnelProfile.AUTH_MODE_SYSTEMD,
                    borderColor = dsh.borderOver(dsh.inputBorder, dsh.inputBg),
                    selectedBorderColor = dsh.borderOver(dsh.borderL2, dsh.layer3)
                )
            )
            if (authMode == TunnelProfile.AUTH_MODE_SYSTEMD) {
                OutlinedTextField(
                    systemdUnit, { systemdUnit = it }, label = { Text("systemd 服务名") },
                    singleLine = true, shape = DshControlShape,
                    textStyle = dsh.monoStyle(), colors = dshFieldColors()
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(
                        selected = systemdScope == "user", onClick = { systemdScope = "user" },
                        label = { Text("用户服务") }, shape = DshControlShape
                    )
                    FilterChip(
                        selected = systemdScope == "system", onClick = { systemdScope = "system" },
                        label = { Text("系统服务") }, shape = DshControlShape
                    )
                }
                OutlinedTextField(
                    systemdLogPath, { systemdLogPath = it }, label = { Text("日志路径（可选）") },
                    singleLine = true, shape = DshControlShape,
                    textStyle = dsh.monoStyle(), colors = dshFieldColors()
                )
                Text(
                    "用户服务对应 SSH 登录用户。日志路径留空时读取本次启动的 journal；服务将 stdout 重定向到文件时，填写该文件的完整路径。SSH 用户需要有日志读取权限。获取失败时回退到保存的认证输入或页面粘贴。",
                    fontSize = 12.sp, color = dsh.textTertiary
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
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                TextButton(enabled = valid, onClick = { onConnectAfterSaveChanged(false); saveProfile() }) { Text("保存") }
                TextButton(enabled = valid, onClick = { onConnectAfterSaveChanged(true); saveProfile() }) { Text("保存并连接") }
            }
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
