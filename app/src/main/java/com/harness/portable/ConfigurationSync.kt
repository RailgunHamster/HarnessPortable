package com.harness.portable

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject

internal data class ConfigurationSyncState(val options: WebDavOptions = WebDavOptions(), val hasPassword: Boolean = false, val busy: Boolean = false, val phase: String = "idle", val message: String = "尚未同步", val conflicts: List<String> = emptyList(), val lastSuccess: Long = 0)

internal class ConfigurationSync private constructor(private val ctx: Context) {
    companion object {
        private fun secret(options: WebDavOptions) = "#webdav-sync:" + java.security.MessageDigest.getInstance("SHA-256").digest((options.url + "\n" + options.username).toByteArray(Charsets.UTF_8)).joinToString("") { "%02x".format(it) }
        @Volatile private var instance: ConfigurationSync? = null
        fun get(context: Context): ConfigurationSync = instance ?: synchronized(this) { instance ?: ConfigurationSync(context.applicationContext).also { instance = it } }
        internal fun isolated(context: Context) = ConfigurationSync(context)
    }
    private val prefs = UiPreferences.prefs(ctx)
    private val client = WebDavConfigurationClient()
    private var conflictLocal: JSONObject? = null
    private var conflictRemote: JSONObject? = null
    private var conflictETag: String? = null
    private val mutableState = MutableStateFlow(ConfigurationSyncState())
    val state: StateFlow<ConfigurationSyncState> = mutableState
    init { refresh() }
    private fun options(): WebDavOptions {
        val o = JSONObject(prefs.getString("sync_options", "{}")!!)
        return WebDavOptions(o.optString("url"), o.optString("username"), o.optBoolean("autoSync"), o.optBoolean("allowHttp"))
    }
    private fun base(): JSONObject = JSONObject(prefs.getString("sync_base", null) ?: SharedConfiguration.empty().toString()).also(SharedConfiguration::validate)
    private fun refresh() { mutableState.value = mutableState.value.copy(options = options(), hasPassword = SecureStore.hasPassword(ctx, secret(options())), lastSuccess = prefs.getLong("sync_last_success", 0)) }
    fun configure(next: WebDavOptions, password: String? = null, clearPassword: Boolean = false) {
        check(!state.value.busy) { "请等待同步完成" }
        val value = next.copy(url = next.url.trim(), username = next.username.trim())
        if (value.url.isNotEmpty() || value.autoSync) WebDavConfigurationClient.folder(value)
        require((password?.length ?: 0) <= 8192) { "同步密码过长" }
        val same = value.url == options().url && value.username == options().username
        require(same || password != null || clearPassword || !SecureStore.hasPassword(ctx, secret(options()))) { "更换 WebDAV 地址或账号时，请重填同步密码或选择清除密码。" }
        if (clearPassword) SecureStore.clearPassword(ctx, secret(value)) else if (password != null) SecureStore.setPassword(ctx, secret(value), password)
        val editor = prefs.edit().putString("sync_options", JSONObject().put("url", value.url).put("username", value.username).put("autoSync", value.autoSync).put("allowHttp", value.allowHttp).toString())
        if (!same) editor.remove("sync_base").remove("sync_last_success")
        check(editor.commit()) { "同步设置未保存" }
        conflictLocal = null; mutableState.value = state.value.copy(phase = "idle", message = "同步设置已保存 · 连接密码始终只保存在本机", conflicts = emptyList()); refresh()
    }
    fun cancel() = client.cancel()
    suspend fun test() = execute { options, password -> withContext(Dispatchers.IO) { client.test(options, password) }; mutableState.value = state.value.copy(phase = "success", message = "WebDAV 目录可访问；首次同步会验证写入权限。") }
    suspend fun run(choice: String? = null, automatic: Boolean = false) {
        if (automatic && (!options().autoSync || state.value.busy || state.value.phase == "conflict")) return
        execute { options, password ->
            require(choice == null || choice in listOf("local", "remote") && conflictLocal != null) { "请先查看同步冲突再选择保留哪一端。" }
            val baseline = base(); val local = snapshot(baseline)
            require(choice == null || SharedConfiguration.same(local, conflictLocal)) { "本机已改变，请重新同步并确认冲突。" }
            repeat(3) {
                val remote = withContext(Dispatchers.IO) { client.read(options, password) }
                require(choice == null || remote.etag == conflictETag && SharedConfiguration.same(remote.document, conflictRemote)) { "云端已改变，请重新同步并确认冲突。" }
                val merge = SharedConfiguration.merge(if (remote.missing) SharedConfiguration.empty() else baseline, local, remote.document, choice)
                if (merge.conflicts.isNotEmpty() && choice == null) {
                    conflictLocal = local; conflictRemote = remote.document; conflictETag = remote.etag
                    mutableState.value = state.value.copy(phase = "conflict", message = "两端修改了相同项目，请选择冲突项保留哪一端；其余项目照常合并。", conflicts = merge.conflicts); return@execute
                }
                require(SharedConfiguration.same(local, snapshot(baseline))) { "同步期间本机配置已改变，未覆盖本机修改，请重试。" }
                if (remote.missing || !SharedConfiguration.same(merge.document, remote.document)) {
                    val written = withContext(Dispatchers.IO) { client.write(options, password, merge.document, remote.etag, remote.missing) }
                    if (!written) { require(choice == null) { "云端已改变，请重新确认冲突。" }; return@repeat }
                }
                require(SharedConfiguration.same(local, snapshot(baseline))) { "上传期间本机配置已改变；本机修改已保留，请重新同步。" }
                client.checkCancelled(); apply(merge.document)
                conflictLocal = null
                mutableState.value = state.value.copy(phase = "success", message = "链接库、收藏分组排序及偏好已同步 · 密码和私钥留在本机", conflicts = emptyList()); return@execute
            }
            error("其他设备正在修改云端配置，请稍后重试。")
        }
    }
    private suspend fun execute(operation: suspend (WebDavOptions, String) -> Unit) {
        if (state.value.busy) return
        mutableState.value = state.value.copy(busy = true, phase = "syncing", message = "正在同步…")
        client.begin()
        try { val options = options(); WebDavConfigurationClient.folder(options); operation(options, SecureStore.getPassword(ctx, secret(options)) ?: "") }
        catch (e: Exception) { mutableState.value = state.value.copy(phase = "failed", message = if (e is IllegalArgumentException || e is IllegalStateException) e.message ?: "同步失败" else "同步失败，未应用云端配置；请检查网络与 WebDAV 设置。") }
        finally { mutableState.value = state.value.copy(busy = false); refresh() }
    }
    internal fun snapshot(baseline: JSONObject): JSONObject {
        val document = SharedConfiguration.empty(); val entries = SharedConfiguration.entries(document); val prior = SharedConfiguration.entries(baseline)
        for (key in prior.keys()) if (key.startsWith("windows:") || key.startsWith("macos:")) entries.put(key, prior.get(key))
        val favorites = prefs.getStringSet(UiPreferences.FAVORITES, emptySet())!!.toSet()
        val tunnels = ProfileStore.loadTunnels(ctx); val directs = ProfileStore.loadDirect(ctx)
        val savedOrder = JSONArray(prefs.getString("sync_connection_order", "[]"))
        val keys = tunnels.map { "tunnel:${it.id}" } + directs.map { "direct:$it" }
        val order = ((0 until savedOrder.length()).map { savedOrder.getString(it) }.filter { it in keys } + keys).distinct()
        fun metadata(item: JSONObject, key: String, sharedKey: String) {
            val old = prior.optJSONObject(sharedKey)
            item.put("favorite", key in favorites).put("group", old?.optString("group") ?: "").put("label", old?.optString("label") ?: "").put("rank", order.indexOf(key))
        }
        for (p in tunnels) {
            val key = "tunnel:${p.id}"
            val item = JSONObject().put("id", p.id).put("name", p.name).put("sshHost", p.sshHost).put("sshPort", p.sshPort).put("user", p.user)
                .put("remoteHost", p.remoteHost).put("remotePort", p.remotePort).put("localPort", p.localPort).put("authMode", p.authMode)
                .put("systemdUnit", p.systemdUnit).put("systemdScope", p.systemdScope).put("systemdLogPath", p.systemdLogPath)
            metadata(item, key, key); entries.put(key, item)
        }
        for (url in directs) {
            val safe = SharedConfiguration.portableUrl(url); val key = "direct:$safe"
            require(!entries.has(key)) { "直连地址去除登录参数后重复，请使用不同网页路径。" }
            val item = JSONObject().put("url", safe); metadata(item, "direct:$url", key); entries.put(key, item)
        }
        val ui = UiPreferences.load(ctx)
        entries.put("shared:appearance", ui.appearance).put("android:browserFullscreen", ui.browserFullscreen).put("android:orientation", prefs.getString("orientation", "auto"))
        SharedConfiguration.validate(document); return document
    }
    private fun sameIdentity(a: TunnelProfile, b: TunnelProfile) = a.sshHost.equals(b.sshHost, true) && a.sshPort == b.sshPort && a.user == b.user
    private fun apply(document: JSONObject) {
        SharedConfiguration.validate(document)
        val entries = SharedConfiguration.entries(document); val before = ProfileStore.loadTunnels(ctx); val oldDirects = ProfileStore.loadDirect(ctx)
        val items = entries.keys().asSequence().filter { it.startsWith("tunnel:") || it.startsWith("direct:") }.toList().sortedWith(compareBy({ entries.getJSONObject(it).getInt("rank") }, { it }))
        val next = mutableListOf<TunnelProfile>(); val directs = mutableListOf<String>(); val favorites = mutableSetOf<String>(); val order = mutableListOf<String>()
        for (key in items) {
            val p = entries.getJSONObject(key); var localKey = key
            if (key.startsWith("tunnel:")) {
                var profile = TunnelProfile(p.getString("id"), p.getString("name"), p.getString("sshHost"), p.getInt("sshPort"), p.getString("user"), p.getString("remoteHost"), p.getInt("remotePort"), p.getInt("localPort"), p.getString("authMode"), systemdUnit = p.getString("systemdUnit"), systemdScope = p.getString("systemdScope"), systemdLogPath = p.getString("systemdLogPath"))
                before.firstOrNull { it.id == profile.id && sameIdentity(it, profile) }?.let { profile = profile.copy(identityFile = it.identityFile) }
                next.add(profile)
            } else {
                val safe = p.getString("url"); val retained = oldDirects.firstOrNull { SharedConfiguration.portableUrl(it) == safe } ?: safe
                directs.add(retained); localKey = "direct:$retained"
            }
            if (p.getBoolean("favorite")) favorites.add(localKey); order.add(localKey)
        }
        for (old in before) {
            val incoming = next.firstOrNull { it.id == old.id }
            if (incoming == null || !sameIdentity(old, incoming)) { SecureStore.clearPassword(ctx, old.id); SecureStore.clearAuthInput(ctx, old.id) }
            else if (old.remoteHost != incoming.remoteHost || old.remotePort != incoming.remotePort) SecureStore.clearAuthInput(ctx, old.id)
        }
        val tunnelsJson = JSONArray()
        for (p in next) tunnelsJson.put(JSONObject().put("id", p.id).put("name", p.name).put("host", p.sshHost).put("port", p.sshPort).put("user", p.user).put("remote_host", p.remoteHost).put("remote_port", p.remotePort).put("local_port", p.localPort).put("auth_mode", p.authMode).put("identity_file", p.identityFile).put("systemd_unit", p.systemdUnit).put("systemd_scope", p.systemdScope).put("systemd_log_path", p.systemdLogPath))
        val edit = prefs.edit().putString("tunnels", tunnelsJson.toString()).putString("direct_urls", JSONArray(directs).toString()).putStringSet(UiPreferences.FAVORITES, favorites)
            .putString("sync_connection_order", JSONArray(order).toString()).putString("sync_base", document.toString()).putLong("sync_last_success", System.currentTimeMillis()).putBoolean("seeded_v1", true)
        if (entries.has("shared:appearance")) edit.putString(UiPreferences.APPEARANCE, entries.getString("shared:appearance"))
        if (entries.has("android:browserFullscreen")) edit.putBoolean(UiPreferences.FULLSCREEN, entries.getBoolean("android:browserFullscreen"))
        if (entries.has("android:orientation")) edit.putString("orientation", entries.getString("android:orientation"))
        check(edit.commit()) { "本机配置未能保存，请重新同步。" }
    }
}
