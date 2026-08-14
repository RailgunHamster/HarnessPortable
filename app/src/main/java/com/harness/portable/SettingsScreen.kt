package com.harness.portable

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
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    tunnels: List<TunnelProfile>,
    directs: List<String>,
    runningTunnelId: String?,
    onConnectTunnel: (TunnelProfile) -> Unit,
    onAddTunnel: (TunnelProfile, String?) -> Unit,
    onUpdateTunnel: (TunnelProfile, String?) -> Unit,
    onDeleteTunnel: (TunnelProfile) -> Unit,
    onConnectDirect: (String) -> Unit,
    onAddDirect: (String) -> Unit,
    onDeleteDirect: (String) -> Unit
) {
    var editorFor by remember { mutableStateOf<TunnelProfile?>(null) }
    var showEditor by remember { mutableStateOf(false) }
    var directInput by remember { mutableStateOf("") }

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
                        "还没有隧道。点 + 添加，例如 administrator@winserver 转发到 127.0.0.1:3080。",
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
                OutlinedTextField(
                    value = directInput,
                    onValueChange = { directInput = it },
                    label = { Text("IP 或完整 URL，如 192.168.0.104") },
                    placeholder = { Text("192.168.0.104") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(
                        keyboardType = KeyboardType.Uri,
                        imeAction = ImeAction.Done
                    ),
                    keyboardActions = KeyboardActions(
                        onDone = {
                            val norm = normalizeUrl(directInput)
                            if (norm != null) {
                                onAddDirect(norm)
                                directInput = ""
                            }
                        }
                    ),
                    trailingIcon = {
                        IconButton(onClick = {
                            val norm = normalizeUrl(directInput)
                            if (norm != null) {
                                onAddDirect(norm)
                                directInput = ""
                            }
                        }) { Icon(Icons.Filled.Add, contentDescription = "添加") }
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
        }
    }

    if (showEditor) {
        TunnelEditorDialog(
            initial = editorFor,
            onDismiss = { showEditor = false },
            onSave = { profile, password ->
                if (editorFor == null) onAddTunnel(profile, password)
                else onUpdateTunnel(profile, password)
                showEditor = false
            }
        )
    }
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
    onDismiss: () -> Unit,
    onSave: (TunnelProfile, String?) -> Unit
) {
    var name by remember { mutableStateOf(initial?.name ?: "") }
    var host by remember { mutableStateOf(initial?.sshHost ?: "") }
    var port by remember { mutableStateOf((initial?.sshPort ?: 22).toString()) }
    var user by remember { mutableStateOf(initial?.user ?: "") }
    var pass by remember { mutableStateOf("") }
    var passVisible by remember { mutableStateOf(false) }
    var remoteHost by remember { mutableStateOf(initial?.remoteHost ?: "127.0.0.1") }
    var remotePort by remember { mutableStateOf((initial?.remotePort ?: 3080).toString()) }
    var localPort by remember { mutableStateOf((initial?.localPort ?: 3080).toString()) }

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
                OutlinedTextField(host, { host = it }, label = { Text("服务器地址") }, singleLine = true)
                OutlinedTextField(
                    port, { port = it }, label = { Text("SSH 端口") }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
                )
                OutlinedTextField(user, { user = it }, label = { Text("用户名") }, singleLine = true)
                OutlinedTextField(
                    pass, { pass = it },
                    label = {
                        Text(if (initial == null) "密码（可稍后输入）" else "密码（留空保持不变）")
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
                Text(
                    "等价于 ssh -N -L 本地端口:远程地址:远程端口 用户名@服务器\n密码用 Android Keystore 加密存储。",
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.outline
                )
            }
        },
        confirmButton = {
            TextButton(
                enabled = valid,
                onClick = {
                    onSave(
                        TunnelProfile(
                            id = initial?.id ?: java.util.UUID.randomUUID().toString(),
                            name = name.trim().ifBlank { host.trim() },
                            sshHost = host.trim(),
                            sshPort = portOr(port, 22),
                            user = user.trim(),
                            remoteHost = remoteHost.trim(),
                            remotePort = portOr(remotePort, 3080),
                            localPort = portOr(localPort, 3080)
                        ),
                        pass.takeIf { it.isNotEmpty() }
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
