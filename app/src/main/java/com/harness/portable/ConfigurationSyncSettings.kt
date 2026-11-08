package com.harness.portable

import com.harness.portable.automation.*

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch
import java.text.DateFormat
import java.util.Date

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun ConfigurationSyncSettings() {
    val context = LocalContext.current
    val sync = remember { ConfigurationSync.get(context) }
    val state by sync.state.collectAsState()
    val scope = rememberCoroutineScope()
    val dsh = LocalDsh.current
    var url by rememberSaveable { mutableStateOf(state.options.url) }
    var username by rememberSaveable { mutableStateOf(state.options.username) }
    var autoSync by rememberSaveable { mutableStateOf(state.options.autoSync) }
    var allowHttp by rememberSaveable { mutableStateOf(state.options.allowHttp) }
    var password by remember { mutableStateOf("") }
    var clearPassword by remember { mutableStateOf(false) }
    var draftMessage by remember { mutableStateOf("") }
    Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Column(Modifier.fillMaxWidth().dshCard().padding(18.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("WebDAV 配置同步", style = MaterialTheme.typography.titleMedium, color = dsh.textPrimary)
            Text("三端共用链接库、收藏、分组、排序与外观。各平台界面偏好分别保存；连接密码、私钥和网页登录令牌始终留在本机。", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
            OutlinedTextField(value = url, onValueChange = { url = it }, label = { Text("WebDAV 私有目录") }, placeholder = { Text("https://nas.example/dav/Harness/") }, singleLine = true, modifier = Modifier.fillMaxWidth().automationId("ConfigurationSync.Address"), enabled = !state.busy, shape = DshControlShape, colors = dshFieldColors())
            OutlinedTextField(value = username, onValueChange = { username = it }, label = { Text("WebDAV 用户名") }, singleLine = true, modifier = Modifier.fillMaxWidth().automationId("ConfigurationSync.Username"), enabled = !state.busy, shape = DshControlShape, colors = dshFieldColors())
            OutlinedTextField(value = password, onValueChange = { password = it }, label = { Text(if (state.hasPassword) "WebDAV 密码 · 已保存" else "WebDAV 密码") }, supportingText = { Text("留空保留已有密码；账号由每台设备分别保存") }, visualTransformation = PasswordVisualTransformation(), singleLine = true, modifier = Modifier.fillMaxWidth().automationId("ConfigurationSync.Password").automationSensitive(), enabled = !state.busy, shape = DshControlShape, colors = dshFieldColors())
            Row(verticalAlignment = Alignment.CenterVertically) { Checkbox(checked = clearPassword, onCheckedChange = { clearPassword = it }, enabled = !state.busy, modifier = Modifier.automationId("ConfigurationSync.ClearPassword")); Text("清除本机 WebDAV 密码", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary) }
            Row(verticalAlignment = Alignment.CenterVertically) { Column(Modifier.weight(1f)) { Text("自动同步", color = dsh.textPrimary); Text("启动、回到前台、配置变更及前台每 5 分钟", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary) }; Switch(checked = autoSync, onCheckedChange = { autoSync = it }, enabled = !state.busy, modifier = Modifier.automationId("ConfigurationSync.AutoSync")) }
            Row(verticalAlignment = Alignment.CenterVertically) { Checkbox(checked = allowHttp, onCheckedChange = { allowHttp = it }, enabled = !state.busy, modifier = Modifier.automationId("ConfigurationSync.AllowHttp")); Text("允许可信局域网明文 HTTP（凭据会明文传输）", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary) }
            Text("请先创建私有目录。同步文件含地址和账号名，不含凭据；直连地址中的登录参数只留在本机。", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = {
                    runCatching { sync.configure(WebDavOptions(url, username, autoSync, allowHttp), password.takeIf { it.isNotEmpty() }, clearPassword) }
                        .onSuccess { password = ""; clearPassword = false; draftMessage = "同步设置已保存" }
                        .onFailure { draftMessage = it.message ?: "保存失败" }
                }, enabled = !state.busy, modifier = Modifier.automationId("ConfigurationSync.Save")) { Text("保存同步设置") }
                TextButton(onClick = { url = state.options.url; username = state.options.username; autoSync = state.options.autoSync; allowHttp = state.options.allowHttp; password = ""; clearPassword = false; draftMessage = "已撤销草稿" }, enabled = !state.busy, modifier = Modifier.automationId("ConfigurationSync.Reset")) { Text("撤销") }
            }
            if (draftMessage.isNotEmpty()) Text(draftMessage, style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
        }
        Column(Modifier.fillMaxWidth().dshCard().padding(18.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("同步状态", style = MaterialTheme.typography.titleMedium, color = dsh.textPrimary)
            if (state.busy) LinearProgressIndicator(Modifier.fillMaxWidth())
            Text(state.message, style = MaterialTheme.typography.bodyMedium, color = if (state.phase == "failed") dsh.danger else dsh.textSecondary)
            Text(if (state.lastSuccess == 0L) "尚无成功同步记录" else "上次成功：${DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT).format(Date(state.lastSuccess))}", style = MaterialTheme.typography.bodySmall, color = dsh.textTertiary)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { scope.launch { sync.test() } }, enabled = !state.busy && state.options.url.isNotEmpty(), modifier = Modifier.automationId("ConfigurationSync.Test")) { Text("测试目录") }
                Button(onClick = { scope.launch { sync.run() } }, enabled = !state.busy && state.options.url.isNotEmpty(), modifier = Modifier.automationId("ConfigurationSync.Run")) { Text("立即同步") }
                if (state.busy) TextButton(onClick = { sync.cancel() }, modifier = Modifier.automationId("ConfigurationSync.Cancel")) { Text("取消") }
            }
            if (state.phase == "conflict") {
                Text("${state.conflicts.size} 项冲突 · 仅处理下列冲突，其余修改仍合并", style = MaterialTheme.typography.bodyMedium, color = dsh.warning)
                Text(state.conflicts.take(15).joinToString("\n"), style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = { scope.launch { sync.run("local") } }, modifier = Modifier.automationId("ConfigurationSync.KeepLocal")) { Text("冲突项保留本机") }
                    OutlinedButton(onClick = { scope.launch { sync.run("remote") } }, modifier = Modifier.automationId("ConfigurationSync.KeepRemote")) { Text("冲突项保留云端") }
                }
            }
        }
    }
}
