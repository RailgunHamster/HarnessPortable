package com.harness.portable

import android.app.Activity
import android.content.Context
import android.os.Handler
import android.os.Looper
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject

private data class PhoneConnection(val key: String, val name: String, val address: String,
    val profile: TunnelProfile? = null, val url: String? = null)

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun SettingsScreen(
    tunnels: List<TunnelProfile>, directs: List<String>, runningTunnelId: String?,
    onConnectTunnel: (TunnelProfile) -> Unit,
    onAddTunnel: (TunnelProfile, String?, String?) -> Unit,
    onUpdateTunnel: (TunnelProfile, String?, String?) -> Unit,
    onDeleteTunnel: (TunnelProfile) -> Unit,
    onConnectDirect: (String) -> Unit, onAddDirect: (String) -> Unit, onDeleteDirect: (String) -> Unit,
    visible: Boolean = true, settingsOnly: Boolean = false, activeLabel: String? = null,
    activeStatus: String = "", onResumeCurrent: () -> Unit = {}, onDisconnectCurrent: () -> Unit = {},
    onOpenSettings: () -> Unit = {}, onBackSettings: () -> Unit = {}, onManageConnections: () -> Unit = {},
    onUpdateDirect: (String, String) -> Unit = { old, new -> onDeleteDirect(old); onAddDirect(new) },
    onSaveAndConnectTunnel: (TunnelProfile) -> Unit = onConnectTunnel
) {
    val ctx = LocalContext.current
    val prefs = remember { UiPreferences.prefs(ctx) }
    val ui = rememberUiPreferences()
    val dsh = LocalDsh.current
    val scope = rememberCoroutineScope()
    val handler = remember { Handler(Looper.getMainLooper()) }
    var favorites by remember { mutableStateOf(prefs.getStringSet(UiPreferences.FAVORITES, emptySet()).orEmpty().toSet()) }
    var editorFor by remember { mutableStateOf<TunnelProfile?>(null) }
    var showEditor by remember { mutableStateOf(false) }
    var showAddMenu by remember { mutableStateOf(false) }
    var showDirectEditor by remember { mutableStateOf(false) }
    var directFor by remember { mutableStateOf<String?>(null) }
    var directInput by remember { mutableStateOf("") }
    var directError by remember { mutableStateOf<String?>(null) }
    var deleting by remember { mutableStateOf<PhoneConnection?>(null) }
    var connectAfterSave by remember { mutableStateOf(false) }
    val initialSources = remember { UpdateSourceStore.migrateLegacy(ctx, ApkUpdate.storedServer(ctx)); UpdateSourceStore.load(ctx) }
    var savedSources by remember { mutableStateOf(initialSources) }
    var draftSources by remember { mutableStateOf(initialSources) }
    var updateUrl by remember { mutableStateOf(initialSources.url()) }
    var savedProxy by remember { mutableStateOf(UpdateProxyStore.load(ctx)) }
    var draftProxy by remember { mutableStateOf(savedProxy) }
    var checkedProxy by remember { mutableStateOf(savedProxy) }
    var proxyStatus by remember { mutableStateOf("") }
    var updateStatus by remember { mutableStateOf("尚未检查更新") }
    var updateNotes by remember { mutableStateOf("") }
    var updateBusy by remember { mutableStateOf(false) }
    var updateProgress by remember { mutableStateOf(-1) }
    var pendingFeed by remember { mutableStateOf<ApkFeed?>(null) }
    var advanced by rememberSaveable { mutableStateOf(false) }
    var notesExpanded by rememberSaveable { mutableStateOf(false) }
    val versionName = remember { ApkUpdate.currentVersionName(ctx) }
    val versionCode = remember { ApkUpdate.currentVersionCode(ctx) }
    val homeScroll = rememberLazyListState()
    val settingsScroll = rememberLazyListState()

    fun typedSources() = draftSources.withUrl(draftSources.selected, updateUrl.trim())
    fun selectSource(kind: UpdateSourceKind) {
        val kept = typedSources()
        savedSources = UpdateSourceStore.select(ctx, kind)
        draftSources = kept.copy(selected = kind)
        updateUrl = draftSources.url()
        pendingFeed = null
        updateStatus = "已切换到${kind.label()} · 检查更新使用已保存地址"
    }
    fun checkUpdate() {
        val sources = savedSources
        val proxy = savedProxy
        updateBusy = true; updateProgress = -1; updateStatus = "正在检查更新…"; pendingFeed = null
        scope.launch {
            try {
                val feed = withContext(Dispatchers.IO) { ApkUpdate.fetchFeed(sources.url(), proxy) }
                checkedProxy = proxy
                pendingFeed = feed; updateNotes = feed.notes
                updateStatus = if (feed.versionCode > versionCode) "发现新版本 ${feed.versionName}（当前 $versionName）" else "当前版本 $versionName，已是最新"
            } catch (error: Exception) { updateStatus = "检查失败：${error.message}" }
            finally { updateBusy = false }
        }
    }
    fun saveSources() {
        val sources = typedSources()
        val url = sources.url()
        val usable = sources.selectedLooksUsable() && runCatching { sources.feedUrl() }.isSuccess
        if (url.isBlank() || !usable) { updateStatus = "地址未保存：请填写完整 CNB / GitHub 仓库地址。"; return }
        UpdateSourceStore.save(ctx, sources); savedSources = sources; draftSources = sources; pendingFeed = null
        updateStatus = "已保存 · 下次检查使用此更新源"
    }
    fun installUpdate() {
        val feed = pendingFeed ?: return
        val proxy = checkedProxy
        if (feed.versionCode <= versionCode) return
        val activity = ctx as? Activity
        if (activity != null && !ApkUpdate.canInstallPackages(ctx)) {
            updateStatus = "请在系统中允许安装应用，然后再点下载并安装"; ApkUpdate.requestInstallPermission(activity); return
        }
        updateBusy = true; updateProgress = 0; updateStatus = "正在下载 ${feed.versionName}…"
        scope.launch {
            try {
                val file = ApkUpdate.updateFile(ctx)
                withContext(Dispatchers.IO) { ApkUpdate.download(feed.apkUrl, file, proxy) { p -> handler.post { updateProgress = p } } }
                updateStatus = "正在打开系统安装界面…"; ApkUpdate.install(ctx, file)
            } catch (error: Exception) { updateStatus = "下载失败：${error.message}" }
            finally { updateBusy = false; updateProgress = -1 }
        }
    }
    val export = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/json")) { uri ->
        if (uri != null) scope.launch {
            runCatching {
                withContext(Dispatchers.IO) {
                    val config = JSONObject().put("version", 1).put("directs", JSONArray(directs)).put("tunnels", JSONArray(tunnels.map { p ->
                        JSONObject().put("id", p.id).put("name", p.name).put("sshHost", p.sshHost).put("sshPort", p.sshPort)
                            .put("user", p.user).put("remoteHost", p.remoteHost).put("remotePort", p.remotePort).put("localPort", p.localPort)
                            .put("authMode", p.authMode).put("identityFile", p.identityFile).put("systemdUnit", p.systemdUnit)
                            .put("systemdScope", p.systemdScope).put("systemdLogPath", p.systemdLogPath)
                    }))
                    ctx.contentResolver.openOutputStream(uri)?.bufferedWriter()?.use { it.write(config.toString(2)) }
                        ?: error("无法写入所选文件")
                }
            }.onSuccess { updateStatus = "连接配置已导出，不包含凭据" }.onFailure { updateStatus = "导出失败：${it.message}" }
        }
    }

    if (!visible) return
    Scaffold(containerColor = dsh.base, topBar = {
        Column(Modifier.fillMaxWidth().background(dsh.layer1).statusBarsPadding().padding(horizontal = 20.dp, vertical = 14.dp)) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                if (settingsOnly) IconButton(onClick = onBackSettings) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回", tint = dsh.textPrimary) }
                Column(Modifier.weight(1f)) {
                    Text(if (settingsOnly) "设置" else "Harness Portable", style = MaterialTheme.typography.titleLarge, color = dsh.textPrimary)
                    Text(if (settingsOnly) "本设备" else "连接", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                }
                if (!settingsOnly) IconButton(onClick = onOpenSettings) { Icon(Icons.Filled.Settings, "设置", tint = dsh.textPrimary) }
            }
        }
    }) { padding ->
        Box(Modifier.fillMaxSize().padding(padding).imePadding(), contentAlignment = Alignment.TopCenter) {
            LazyColumn(state = if (settingsOnly) settingsScroll else homeScroll,
                modifier = Modifier.widthIn(max = 720.dp).fillMaxSize(), contentPadding = PaddingValues(20.dp, 18.dp, 20.dp, 28.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)) {
                if (!settingsOnly) {
                    if (activeLabel != null) item("current") {
                        HubHeading("当前服务")
                        Column(Modifier.fillMaxWidth().dshCard().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            Text(activeLabel, style = MaterialTheme.typography.titleMedium, color = dsh.textPrimary)
                            Text(activeStatus, style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            Button(onClick = onResumeCurrent, modifier = Modifier.fillMaxWidth()) { Text("继续使用") }
                            TextButton(onClick = onDisconnectCurrent) { Text("断开当前连接", color = dsh.danger) }
                        }
                    }
                    item("heading") { HubHeading("我的连接") }
                    val entries = (tunnels.map { PhoneConnection("tunnel:" + it.id, it.name.ifBlank { it.sshHost }, "${it.user}@${it.sshHost}:${it.sshPort}", profile = it) } +
                        directs.map { PhoneConnection("direct:" + it, hostOf(it), it, url = it) }).sortedByDescending { it.key in favorites }
                    if (entries.isEmpty()) item("empty") {
                        ShellEmptyState(icon = Icons.Filled.Dns, title = "添加第一个连接", message = "通过 SSH 隧道或直连地址打开服务网页。", actionLabel = "添加连接", onAction = { showAddMenu = true })
                    }
                    items(entries, key = { it.key }) { entry ->
                        PhoneConnectionRow(entry, entry.key in favorites, entry.profile != null && entry.profile.id == runningTunnelId,
                            onOpen = { entry.profile?.let(onConnectTunnel) ?: entry.url?.let(onConnectDirect) },
                            onEdit = { if (entry.profile != null) { editorFor = entry.profile; connectAfterSave = false; showEditor = true } else { directFor = entry.url; directInput = entry.url.orEmpty(); directError = null; showDirectEditor = true } },
                            onDelete = { deleting = entry }, onFavorite = {
                                val next = favorites.toMutableSet(); if (!next.add(entry.key)) next.remove(entry.key)
                                favorites = next.toSet(); prefs.edit().putStringSet(UiPreferences.FAVORITES, next).apply()
                            })
                    }
                    item("add") {
                        Box {
                            OutlinedButton(onClick = { showAddMenu = true }) { Icon(Icons.Filled.Add, null); Text("添加连接") }
                            DropdownMenu(expanded = showAddMenu, onDismissRequest = { showAddMenu = false }) {
                                DropdownMenuItem(text = { Text("SSH 隧道") }, onClick = { showAddMenu = false; editorFor = null; connectAfterSave = false; showEditor = true })
                                DropdownMenuItem(text = { Text("直连网页") }, onClick = { showAddMenu = false; directFor = null; directInput = ""; directError = null; showDirectEditor = true })
                            }
                        }
                    }
                } else {
                    item("general") {
                        HubHeading("通用")
                        Column(Modifier.fillMaxWidth().dshCard().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            Text("外观", color = dsh.textPrimary)
                            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                listOf("system" to "跟随系统", "light" to "浅色", "dark" to "深色").forEach { (value, label) ->
                                    FilterChip(selected = ui.appearance == value, onClick = { UiPreferences.setAppearance(ctx, value) }, label = { Text(label) })
                                }
                            }
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) { Text("进入服务后全屏", color = dsh.textPrimary); Text("列表和设置保留系统导航", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary) }
                                Switch(checked = ui.browserFullscreen, onCheckedChange = { UiPreferences.setFullscreen(ctx, it) })
                            }
                            Text("偏好立即保存", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                        }
                    }
                    item("security") {
                        HubHeading("连接与安全")
                        Column(Modifier.fillMaxWidth().dshCard().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            Text("连接凭据", color = dsh.textPrimary)
                            Text("从连接菜单编辑对应服务器，SSH 登录与网页登录分别配置。保存后下次连接生效。", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            OutlinedButton(onClick = onManageConnections) { Text("管理连接") }
                            OutlinedButton(onClick = { export.launch("harness-connections.json") }) { Text("导出配置（不含凭据）") }
                            if (!BatteryExemption.isExempt(ctx)) TextButton(onClick = { BatteryExemption.request(ctx) }) { Text("允许 SSH 隧道在后台运行") }
                        }
                    }
                    item("updates") {
                        HubHeading("更新与关于")
                        Column(Modifier.fillMaxWidth().dshCard().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            Text("Harness Portable · $versionName", color = dsh.textPrimary)
                            Text("正在使用：${savedSources.selected.label()}", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                UpdateSourceKind.entries.forEach { kind ->
                                    FilterChip(selected = savedSources.selected == kind, enabled = !updateBusy,
                                        onClick = { selectSource(kind) }, label = { Text(kind.label()) })
                                }
                            }
                            Text(savedSources.url(), style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)

                            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Button(onClick = { checkUpdate() }, enabled = !updateBusy) { Text("检查更新") }
                                if ((pendingFeed?.versionCode ?: 0) > versionCode) OutlinedButton(onClick = { installUpdate() }, enabled = !updateBusy) { Text("下载并安装") }
                            }
                            if (updateBusy) LinearProgressIndicator(progress = { (updateProgress.coerceAtLeast(0) / 100f) }, modifier = Modifier.fillMaxWidth())
                            Text(updateStatus, style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            HorizontalDivider(color = dsh.borderL1)
                            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                                Text("更新使用代理", modifier = Modifier.weight(1f), color = dsh.textPrimary)
                                Switch(checked = draftProxy.enabled, onCheckedChange = { draftProxy = draftProxy.copy(enabled = it); proxyStatus = "修改后请保存代理设置" }, enabled = !updateBusy)
                            }
                            Text("用于检查更新和下载安装包 · 默认关闭", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            OutlinedTextField(value = draftProxy.address, onValueChange = { draftProxy = draftProxy.copy(address = it); proxyStatus = "修改后请保存代理设置" },
                                label = { Text("HTTP 代理地址") }, singleLine = true, enabled = draftProxy.enabled && !updateBusy,
                                modifier = Modifier.fillMaxWidth(), shape = DshControlShape, colors = dshFieldColors())
                            Text(proxyStatus.ifEmpty { if (savedProxy.enabled) "已启用 · ${savedProxy.address}" else "已关闭 · 更新直接连接" }, style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            Text("localhost 指这台手机；电脑上的代理请填写电脑的局域网 IP。", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                OutlinedButton(onClick = {
                                    val next = draftProxy.copy(address = draftProxy.address.trim().ifEmpty { UpdateProxy.DEFAULT_ADDRESS })
                                    val valid = !next.enabled || runCatching { UpdateProxy.parseAddress(next.address) }.isSuccess
                                    if (!valid) proxyStatus = "请输入 HTTP 代理地址，例如 localhost:7890。"
                                    else {
                                        UpdateProxyStore.save(ctx, next); savedProxy = next; draftProxy = next; pendingFeed = null
                                        proxyStatus = "已保存 · ${if (next.enabled) "代理已启用" else "代理已关闭"}"; updateStatus = "更新设置已更改，请重新检查"
                                    }
                                }, enabled = !updateBusy) { Text("保存代理设置") }
                                TextButton(onClick = { draftProxy = savedProxy; proxyStatus = "" }, enabled = !updateBusy) { Text("撤销") }
                            }
                            TextButton(onClick = { advanced = !advanced }) { Text(if (advanced) "收起高级设置" else "高级：自定义更新源") }
                            if (advanced) {
                                OutlinedTextField(value = updateUrl, onValueChange = { updateUrl = it }, label = { Text("${draftSources.selected.label()} 仓库") },
                                    modifier = Modifier.fillMaxWidth(), shape = DshControlShape, colors = dshFieldColors())
                                Text("切换更新源立即生效；地址修改需要保存，检查不会保存地址草稿。", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                    Button(onClick = { saveSources() }, enabled = !updateBusy) { Text("保存并使用") }
                                    TextButton(onClick = { draftSources = savedSources; updateUrl = savedSources.url() }) { Text("撤销") }
                                    TextButton(onClick = { savedSources = savedSources.copy(selected = UpdateSourceKind.CNB, cnb = ApkUpdate.DEFAULT_SERVER); UpdateSourceStore.save(ctx, savedSources); draftSources = savedSources; updateUrl = savedSources.url(); pendingFeed = null }, enabled = !updateBusy) { Text("官方源") }
                                }
                            }
                            if (updateNotes.isNotBlank()) {
                                TextButton(onClick = { notesExpanded = !notesExpanded }) { Text("版本说明") }
                                if (notesExpanded) Text(updateNotes, style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            }
                        }
                    }
                }
            }
        }
    }
    if (showEditor) {
        val initialAuth = remember(editorFor) { editorFor?.let { SecureStore.getAuthInput(ctx, it.id) } }
        TunnelEditorDialog(initial = editorFor, initialAuthInput = initialAuth, onDismiss = { showEditor = false },
            onSave = { profile, password, auth ->
                if (editorFor == null) onAddTunnel(profile, password, auth) else onUpdateTunnel(profile, password, auth)
                showEditor = false
                if (connectAfterSave) onSaveAndConnectTunnel(profile)
            }, onConnectAfterSaveChanged = { connectAfterSave = it })
    }
    if (showDirectEditor) AlertDialog(onDismissRequest = { showDirectEditor = false }, title = { Text(if (directFor == null) "添加直连网页" else "编辑直连网页") },
        text = { Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            LanMachineField(value = directInput, onValueChange = { directInput = it; directError = null }, label = "IP、机器名或完整 URL", placeholder = "server.example.test", modifier = Modifier.fillMaxWidth())
            Text("保存后下次打开生效，当前网页保持。", style = MaterialTheme.typography.bodySmall)
            directError?.let { Text(it, color = dsh.danger) }
        } }, confirmButton = { TextButton(onClick = {
            val normalized = normalizeUrl(directInput)
            val parsed = normalized?.let { runCatching { java.net.URI(it) }.getOrNull() }
            if (normalized == null || parsed?.scheme !in listOf("http", "https") || parsed?.host.isNullOrBlank()) directError = "请输入有效的 HTTP(S) 地址"
            else {
                directFor?.let { old ->
                    onUpdateDirect(old, normalized)
                    if (old != normalized && "direct:$old" in favorites) {
                        val next = favorites - "direct:$old" + "direct:$normalized"
                        favorites = next; prefs.edit().putStringSet(UiPreferences.FAVORITES, next).apply()
                    }
                } ?: onAddDirect(normalized)
                showDirectEditor = false
            }
        }) { Text("保存") } }, dismissButton = { TextButton(onClick = { showDirectEditor = false }) { Text("取消") } })
    deleting?.let { entry -> AlertDialog(onDismissRequest = { deleting = null }, title = { Text("删除 ${entry.name}？") },
        text = { Text("删除保存的连接和对应凭据。若此服务正在使用，也会断开当前连接。") },
        confirmButton = { TextButton(onClick = {
            entry.profile?.let(onDeleteTunnel) ?: entry.url?.let(onDeleteDirect)
            favorites = favorites - entry.key
            prefs.edit().putStringSet(UiPreferences.FAVORITES, favorites).apply()
            deleting = null
        }) { Text("删除", color = dsh.danger) } },
        dismissButton = { TextButton(onClick = { deleting = null }) { Text("取消") } }) }
}

@Composable
private fun HubHeading(text: String) { Text(text, style = MaterialTheme.typography.titleMedium, color = LocalDsh.current.textPrimary, modifier = Modifier.padding(top = 4.dp, bottom = 10.dp)) }

@Composable
private fun PhoneConnectionRow(entry: PhoneConnection, favorite: Boolean, running: Boolean,
    onOpen: () -> Unit, onEdit: () -> Unit, onDelete: () -> Unit, onFavorite: () -> Unit) {
    val dsh = LocalDsh.current
    var menu by remember { mutableStateOf(false) }
    Row(Modifier.fillMaxWidth().height(IntrinsicSize.Min).dshCard(), verticalAlignment = Alignment.CenterVertically) {
        Row(Modifier.weight(1f).fillMaxHeight()
            .clickable(role = Role.Button, onClickLabel = if (running || entry.profile == null) "打开" else "连接", onClick = onOpen)
            .padding(start = 16.dp, end = 8.dp, top = 12.dp, bottom = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(if (entry.profile == null) Icons.Filled.Language else Icons.Filled.Dns, null, tint = dsh.textSecondary)
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(entry.name, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis, color = dsh.textPrimary)
                Text((if (entry.profile == null) "直连" else "SSH") + (if (favorite) " · 常用" else "") + (if (running) " · 已连接" else ""), style = MaterialTheme.typography.bodySmall, color = if (running) dsh.success else dsh.textSecondary)
                Text(entry.address, style = MaterialTheme.typography.bodySmall, maxLines = 2, overflow = TextOverflow.Ellipsis, color = dsh.textTertiary)
            }
        }
        Box(Modifier.padding(end = 4.dp)) {
            IconButton(onClick = { menu = true }) { Icon(Icons.Filled.MoreVert, "${entry.name}菜单", tint = dsh.textSecondary) }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                DropdownMenuItem(text = { Text("编辑连接") }, onClick = { menu = false; onEdit() })
                DropdownMenuItem(text = { Text(if (favorite) "移出常用" else "加入常用") }, onClick = { menu = false; onFavorite() })
                DropdownMenuItem(text = { Text("删除连接", color = dsh.danger) }, onClick = { menu = false; onDelete() })
            }
        }
    }
}
