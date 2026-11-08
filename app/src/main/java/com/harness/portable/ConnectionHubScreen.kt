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
import com.harness.portable.automation.*

private data class PhoneConnection(val key: String, val name: String, val address: String,
    val profile: TunnelProfile? = null, val url: String? = null, val group: String = "")

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
    var settingsTab by rememberSaveable { mutableStateOf(0) }
    val settingsScrollStates = listOf(rememberLazyListState(), rememberLazyListState(), rememberLazyListState(), rememberLazyListState())
    val settingsScroll = settingsScrollStates[settingsTab]
    fun syncedMetadata() = runCatching { JSONObject(prefs.getString("sync_base", "{}")!!).optJSONObject("entries") ?: JSONObject() }.getOrElse { JSONObject() }
    fun syncedOrder() = runCatching { JSONArray(prefs.getString("sync_connection_order", "[]")) }.getOrElse { JSONArray() }
    var libraryMetadata by remember { mutableStateOf(syncedMetadata()) }
    var libraryOrder by remember { mutableStateOf(syncedOrder()) }
    DisposableEffect(prefs) {
        val listener = android.content.SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
            if (key == UiPreferences.FAVORITES) favorites = prefs.getStringSet(UiPreferences.FAVORITES, emptySet()).orEmpty().toSet()
            if (key == "sync_base") libraryMetadata = syncedMetadata()
            if (key == "sync_connection_order") libraryOrder = syncedOrder()
        }
        prefs.registerOnSharedPreferenceChangeListener(listener)
        onDispose { prefs.unregisterOnSharedPreferenceChangeListener(listener) }
    }

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
    fun exportTo(uri: android.net.Uri?) {
        if (uri != null) scope.launch {
            runCatching {
                withContext(Dispatchers.IO) {
                    val config = ConnectionExport.document(tunnels, directs)
                    ctx.contentResolver.openOutputStream(uri)?.bufferedWriter()?.use { it.write(config.toString(2)) }
                        ?: error("无法写入所选文件")
                }
            }.onSuccess { updateStatus = "连接配置已导出，不包含凭据" }.onFailure { updateStatus = "导出失败：${it.message}" }
        }
    }
    val export = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/json")) { exportTo(it) }
    fun requestExport() {
        if (!AutomationDialogs.create("harness-connections.json", ::exportTo)) export.launch("harness-connections.json")
    }
    fun createEntry(kind: String) {
        onManageConnections(); showAddMenu = false
        when(kind) {
            "ssh" -> { editorFor = null; connectAfterSave = false; showEditor = true }
            "direct" -> { directFor = null; directInput = ""; directError = null; showDirectEditor = true }
            else -> fail("invalid_argument", "Unknown connection kind")
        }
    }
    fun entryFor(key: String): PhoneConnection {
        tunnels.firstOrNull { "tunnel:" + it.id == key }?.let { return PhoneConnection(key, it.name.ifBlank { it.sshHost }, it.sshHost, profile = it) }
        directs.firstOrNull { AndroidAutomation.directKey(it) == key }?.let { return PhoneConnection("direct:$it", hostOf(it), it, url = it) }
        fail("not_found", "Connection is unavailable")
    }
    fun editEntry(entry: PhoneConnection) {
        onManageConnections()
        if (entry.profile != null) { editorFor = entry.profile; connectAfterSave = false; showEditor = true }
        else { directFor = entry.url; directInput = entry.url.orEmpty(); directError = null; showDirectEditor = true }
    }
    fun setFavorite(key: String, value: Boolean) {
        val next = favorites.toMutableSet(); if (value) next.add(key) else next.remove(key)
        favorites = next; prefs.edit().putStringSet(UiPreferences.FAVORITES, next).apply()
    }
    fun askDelete(entry: PhoneConnection) { onManageConnections(); deleting = entry }
    AutomationOperations(
        AndroidOperation("library.create", "Open the normal new-connection editor", setOf("kind")) { createEntry(it.optString("kind", "ssh")); true },
        AndroidOperation("library.edit", "Edit an existing connection through its normal dialog", setOf("key")) { editEntry(entryFor(it.requiredString("key"))); true },
        AndroidOperation("library.delete", "Ask for confirmation before deleting a connection", setOf("key")) { askDelete(entryFor(it.requiredString("key"))); true },
        AndroidOperation("library.favorite", "Set the saved favorite state", setOf("key", "value")) { val entry = entryFor(it.requiredString("key")); setFavorite(entry.key, it.requiredBoolean("value")); true },
        AndroidOperation("settings.tab", "Select a settings category", setOf("value")) { args ->
            settingsTab = listOf("general", "security", "sync", "about").indexOf(args.requiredString("value")).takeIf { it >= 0 } ?: fail("invalid_argument", "Unknown settings category")
            onOpenSettings(); true
        },
        AndroidOperation("config.export", "Export through the normal file-selection flow") { requestExport(); true },
        AndroidOperation("updates.check", "Check the saved update source") { checkUpdate(); true },
        AndroidOperation("updates.apply", "Download the selected update and hand it to Android's installer") { installUpdate(); true },
        AndroidReadOperation("updates.status", "Return update progress and saved source") { JSONObject().put("busy", updateBusy).put("message", updateStatus).put("progress", updateProgress).put("source", savedSources.selected.name).put("version", versionName) }
    )

    if (!visible) return
    Scaffold(containerColor = dsh.base, topBar = {
        Column(Modifier.fillMaxWidth().background(dsh.layer1).statusBarsPadding()) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                if (settingsOnly) IconButton(onClick = onBackSettings, modifier = Modifier.automationId("Settings.Back")) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回", tint = dsh.textPrimary) }
                Column(Modifier.weight(1f)) {
                    Text(if (settingsOnly) "设置" else "Harness Portable", style = MaterialTheme.typography.titleLarge, color = dsh.textPrimary)
                    Text(if (settingsOnly) "外观、同步与更新" else "连接", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                }
                if (!settingsOnly) IconButton(onClick = onOpenSettings, modifier = Modifier.automationId("Library.Settings")) { Icon(Icons.Filled.Settings, "设置", tint = dsh.textPrimary) }
            }
            if (settingsOnly) ScrollableTabRow(selectedTabIndex = settingsTab, edgePadding = 16.dp, containerColor = dsh.layer1, contentColor = dsh.accent, modifier = Modifier.automationId("Settings.Categories").automationScrollOwner()) {
                listOf("通用", "数据与安全", "WebDAV 同步", "更新与关于").forEachIndexed { index, label ->
                    Tab(selected = settingsTab == index, onClick = { settingsTab = index }, text = { Text(label, maxLines = 1) }, modifier = Modifier.automationId("Settings.Category." + listOf("general", "security", "sync", "about")[index]))
                }
            }
        }
    }) { padding ->
        Box(Modifier.fillMaxSize().padding(padding).imePadding(), contentAlignment = Alignment.TopCenter) {
            LazyColumn(state = if (settingsOnly) settingsScroll else homeScroll,
                modifier = Modifier.widthIn(max = 720.dp).fillMaxSize().automationId(if (settingsOnly) "Settings.Content" else "Library.Content"), contentPadding = PaddingValues(20.dp, 18.dp, 20.dp, 28.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)) {
                if (!settingsOnly) {
                    if (activeLabel != null) item("current") {
                        HubHeading("当前服务")
                        Column(Modifier.fillMaxWidth().dshCard().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            Text(activeLabel, style = MaterialTheme.typography.titleMedium, color = dsh.textPrimary)
                            Text(activeStatus, style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            Button(onClick = onResumeCurrent, modifier = Modifier.fillMaxWidth().automationId("Library.Resume")) { Text("继续使用") }
                            TextButton(onClick = onDisconnectCurrent, modifier = Modifier.automationId("Library.Disconnect")) { Text("断开当前连接", color = dsh.danger) }
                        }
                    }
                    item("heading") { HubHeading("我的连接") }
                    val orderIndex = (0 until libraryOrder.length()).associate { libraryOrder.getString(it) to it }
                    val entries = (tunnels.map { p ->
                        val meta = libraryMetadata.optJSONObject("tunnel:" + p.id)
                        val label = meta?.optString("label").orEmpty().takeIf { meta?.optString("name") == p.name }.orEmpty()
                        PhoneConnection("tunnel:" + p.id, label.ifBlank { p.name.ifBlank { p.sshHost } }, "${p.user}@${p.sshHost}:${p.sshPort}", profile = p, group = meta?.optString("group").orEmpty())
                    } + directs.map { url ->
                        val meta = libraryMetadata.optJSONObject("direct:" + runCatching { SharedConfiguration.portableUrl(url) }.getOrDefault(url))
                        PhoneConnection("direct:" + url, meta?.optString("label").orEmpty().ifBlank { hostOf(url) }, url, url = url, group = meta?.optString("group").orEmpty())
                    }).sortedWith(compareBy<PhoneConnection> { orderIndex[it.key] ?: Int.MAX_VALUE }.thenByDescending { it.key in favorites })
                    if (entries.isEmpty()) item("empty") {
                        ShellEmptyState(icon = Icons.Filled.Dns, title = "添加第一个连接", message = "通过 SSH 隧道或直连地址打开服务网页。", actionLabel = "添加连接", onAction = { showAddMenu = true }, actionId = "Library.Empty.Add")
                    }
                    items(entries, key = { it.key }) { entry ->
                        PhoneConnectionRow(entry, entry.key in favorites, entry.profile != null && entry.profile.id == runningTunnelId,
                            onOpen = { entry.profile?.let(onConnectTunnel) ?: entry.url?.let(onConnectDirect) },
                            onEdit = { editEntry(entry) }, onDelete = { askDelete(entry) },
                            onFavorite = { setFavorite(entry.key, entry.key !in favorites) })
                    }
                    item("add") {
                        Box {
                            OutlinedButton(onClick = { showAddMenu = true }, modifier = Modifier.automationId("Library.Add")) { Icon(Icons.Filled.Add, null); Text("添加连接") }
                            AutomationDropdownMenu(expanded = showAddMenu, onDismissRequest = { showAddMenu = false }, modifier = Modifier.automationId("Library.AddMenu")) {
                            DropdownMenuItem(text = { Text("SSH 隧道") }, onClick = { createEntry("ssh") }, modifier = Modifier.automationId("Library.AddSsh"))
                            DropdownMenuItem(text = { Text("直连网页") }, onClick = { createEntry("direct") }, modifier = Modifier.automationId("Library.AddDirect"))
                            }
                        }
                    }
                } else {
                    if (settingsTab == 0) item("general") {
                        HubHeading("通用")
                        Column(Modifier.fillMaxWidth().dshCard().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            Text("外观", color = dsh.textPrimary)
                            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                listOf("system" to "跟随系统", "light" to "浅色", "dark" to "深色").forEach { (value, label) ->
                                    FilterChip(selected = ui.appearance == value, onClick = { UiPreferences.setAppearance(ctx, value) }, label = { Text(label) }, modifier = Modifier.automationId("Settings.Appearance." + value))
                                }
                            }
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) { Text("进入服务后全屏", color = dsh.textPrimary); Text("列表和设置保留系统导航", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary) }
                                Switch(checked = ui.browserFullscreen, onCheckedChange = { UiPreferences.setFullscreen(ctx, it) }, modifier = Modifier.automationId("Settings.Fullscreen"))
                            }
                            Text("偏好立即保存", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                        }
                    }
                    if (settingsTab == 1) item("security") {
                        HubHeading("连接与安全")
                        Column(Modifier.fillMaxWidth().dshCard().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            Text("连接凭据", color = dsh.textPrimary)
                            Text("从连接菜单编辑对应服务器，SSH 登录与网页登录分别配置。保存后下次连接生效。", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            OutlinedButton(onClick = onManageConnections, modifier = Modifier.automationId("Settings.ManageConnections")) { Text("管理连接") }
                            OutlinedButton(onClick = { requestExport() }, modifier = Modifier.automationId("Settings.Export")) { Text("导出配置（不含凭据）") }
                            if (!BatteryExemption.isExempt(ctx)) TextButton(onClick = { BatteryExemption.request(ctx) }, modifier = Modifier.automationId("Settings.Battery")) { Text("允许 SSH 隧道在后台运行") }
                        }
                    }
                    if (settingsTab == 2) item("sync") { ConfigurationSyncSettings() }
                    if (settingsTab == 3) item("updates") {
                        HubHeading("更新与关于")
                        Column(Modifier.fillMaxWidth().dshCard().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            Text("Harness Portable · $versionName", color = dsh.textPrimary)
                            Text("正在使用：${savedSources.selected.label()}", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                UpdateSourceKind.entries.forEach { kind ->
                                    FilterChip(selected = savedSources.selected == kind, enabled = !updateBusy,
                                        onClick = { selectSource(kind) }, label = { Text(kind.label()) }, modifier = Modifier.automationId("Updates.Source." + kind.name))
                                }
                            }
                            Text(savedSources.url(), style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)

                            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Button(onClick = { checkUpdate() }, enabled = !updateBusy, modifier = Modifier.automationId("Updates.Check")) { Text("检查更新") }
                                if ((pendingFeed?.versionCode ?: 0) > versionCode) OutlinedButton(onClick = { installUpdate() }, enabled = !updateBusy, modifier = Modifier.automationId("Updates.Apply")) { Text("下载并安装") }
                            }
                            if (updateBusy) LinearProgressIndicator(progress = { (updateProgress.coerceAtLeast(0) / 100f) }, modifier = Modifier.fillMaxWidth())
                            Text(updateStatus, style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            HorizontalDivider(color = dsh.borderL1)
                            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                                Text("更新使用代理", modifier = Modifier.weight(1f), color = dsh.textPrimary)
                                Switch(checked = draftProxy.enabled, onCheckedChange = { draftProxy = draftProxy.copy(enabled = it); proxyStatus = "修改后请保存代理设置" }, enabled = !updateBusy, modifier = Modifier.automationId("Updates.ProxyEnabled"))
                            }
                            Text("用于检查更新和下载安装包 · 默认关闭", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                            OutlinedTextField(value = draftProxy.address, onValueChange = { draftProxy = draftProxy.copy(address = it); proxyStatus = "修改后请保存代理设置" },
                                label = { Text("HTTP 代理地址") }, singleLine = true, enabled = draftProxy.enabled && !updateBusy,
                                modifier = Modifier.fillMaxWidth().automationId("Updates.ProxyAddress"), shape = DshControlShape, colors = dshFieldColors())
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
                                }, enabled = !updateBusy, modifier = Modifier.automationId("Updates.SaveProxy")) { Text("保存代理设置") }
                                TextButton(onClick = { draftProxy = savedProxy; proxyStatus = "" }, enabled = !updateBusy, modifier = Modifier.automationId("Updates.ResetProxy")) { Text("撤销") }
                            }
                            TextButton(onClick = { advanced = !advanced }, modifier = Modifier.automationId("Updates.Advanced")) { Text(if (advanced) "收起高级设置" else "高级：自定义更新源") }
                            if (advanced) {
                                OutlinedTextField(value = updateUrl, onValueChange = { updateUrl = it }, label = { Text("${draftSources.selected.label()} 仓库") },
                                    modifier = Modifier.fillMaxWidth().automationId("Updates.Address"), shape = DshControlShape, colors = dshFieldColors())
                                Text("切换更新源立即生效；地址修改需要保存，检查不会保存地址草稿。", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
                                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                    Button(onClick = { saveSources() }, enabled = !updateBusy, modifier = Modifier.automationId("Updates.SaveSource")) { Text("保存并使用") }
                                    TextButton(onClick = { draftSources = savedSources; updateUrl = savedSources.url() }, modifier = Modifier.automationId("Updates.ResetSource")) { Text("撤销") }
                                    TextButton(onClick = { savedSources = savedSources.copy(selected = UpdateSourceKind.CNB, cnb = ApkUpdate.DEFAULT_SERVER); UpdateSourceStore.save(ctx, savedSources); draftSources = savedSources; updateUrl = savedSources.url(); pendingFeed = null }, enabled = !updateBusy, modifier = Modifier.automationId("Updates.OfficialSource")) { Text("官方源") }
                                }
                            }
                            if (updateNotes.isNotBlank()) {
                                TextButton(onClick = { notesExpanded = !notesExpanded }, modifier = Modifier.automationId("Updates.Notes")) { Text("版本说明") }
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
    if (showDirectEditor) AutomationAlertDialog(onDismissRequest = { showDirectEditor = false }, title = { Text(if (directFor == null) "添加直连网页" else "编辑直连网页") },
        text = { Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            LanMachineField(value = directInput, onValueChange = { directInput = it; directError = null }, label = "IP、机器名或完整 URL", automationId = "DirectEditor.Address", placeholder = "server.example.test", modifier = Modifier.fillMaxWidth().automationSensitive())
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
        }, modifier = Modifier.automationId("DirectEditor.Save")) { Text("保存") } }, dismissButton = { TextButton(onClick = { showDirectEditor = false }, modifier = Modifier.automationId("DirectEditor.Cancel")) { Text("取消") } }, modifier = Modifier.automationId("DirectEditor.Dialog"))
    deleting?.let { entry -> AutomationAlertDialog(onDismissRequest = { deleting = null }, title = { Text("删除 ${entry.name}？") },
        text = { Text("删除保存的连接和对应凭据。若此服务正在使用，也会断开当前连接。") },
        confirmButton = { TextButton(onClick = {
            entry.profile?.let(onDeleteTunnel) ?: entry.url?.let(onDeleteDirect)
            favorites = favorites - entry.key
            prefs.edit().putStringSet(UiPreferences.FAVORITES, favorites).apply()
            deleting = null
        }, modifier = Modifier.automationId("Library.DeleteConfirm")) { Text("删除", color = dsh.danger) } },
        dismissButton = { TextButton(onClick = { deleting = null }, modifier = Modifier.automationId("Library.DeleteCancel")) { Text("取消") } }, modifier = Modifier.automationId("Library.DeleteDialog")) }
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
            .automationId("Library.Entry.Open", AndroidAutomation.dataKey(entry.key)).clickable(role = Role.Button, onClickLabel = if (running || entry.profile == null) "打开" else "连接", onClick = onOpen)
            .padding(start = 16.dp, end = 8.dp, top = 12.dp, bottom = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(if (entry.profile == null) Icons.Filled.Language else Icons.Filled.Dns, null, tint = dsh.textSecondary)
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(entry.name, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis, color = dsh.textPrimary)
                Text((if (entry.profile == null) "直连" else "SSH") + (if (favorite) " · 常用" else "") + (if (running) " · 已连接" else "") + (if (entry.group.isNotBlank()) " · ${entry.group}" else ""), style = MaterialTheme.typography.bodySmall, color = if (running) dsh.success else dsh.textSecondary)
                Text(entry.address, style = MaterialTheme.typography.bodySmall, maxLines = 2, overflow = TextOverflow.Ellipsis, color = dsh.textTertiary, modifier = if (entry.profile == null) Modifier.automationSensitive() else Modifier)
            }
        }
        Box(Modifier.padding(end = 4.dp)) {
            IconButton(onClick = { menu = true }, modifier = Modifier.automationId("Library.Entry.More", AndroidAutomation.dataKey(entry.key))) { Icon(Icons.Filled.MoreVert, "${entry.name}菜单", tint = dsh.textSecondary) }
            AutomationDropdownMenu(expanded = menu, onDismissRequest = { menu = false }, modifier = Modifier.automationId("Library.Entry.Menu", AndroidAutomation.dataKey(entry.key))) {
                DropdownMenuItem(text = { Text("编辑连接") }, onClick = { menu = false; onEdit() }, modifier = Modifier.automationId("Library.Entry.Edit", AndroidAutomation.dataKey(entry.key)))
                DropdownMenuItem(text = { Text(if (favorite) "移出常用" else "加入常用") }, onClick = { menu = false; onFavorite() }, modifier = Modifier.automationId("Library.Entry.Favorite", AndroidAutomation.dataKey(entry.key)))
                DropdownMenuItem(text = { Text("删除连接", color = dsh.danger) }, onClick = { menu = false; onDelete() }, modifier = Modifier.automationId("Library.Entry.Delete", AndroidAutomation.dataKey(entry.key)))
            }
        }
    }
}
