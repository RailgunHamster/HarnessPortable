package com.harness.portable

import org.json.JSONObject
import java.net.URI

/** Wire allowlist shared with Windows and macOS. No credential store is read here. */
internal object SharedConfiguration {
    const val LIMIT = 1024 * 1024
    const val FILE = "harness-settings-v1.json"
    fun empty() = JSONObject().put("format", "harness-portable-settings").put("version", 1).put("entries", JSONObject())
    fun entries(doc: JSONObject) = doc.getJSONObject("entries")
    fun canonical(value: Any?): String = when (value) {
        null, JSONObject.NULL -> "null"
        is JSONObject -> value.keys().asSequence().toList().sorted().joinToString(",", "{", "}") { JSONObject.quote(it) + ":" + canonical(value.get(it)) }
        is String -> JSONObject.quote(value)
        else -> value.toString()
    }
    fun same(a: Any?, b: Any?) = canonical(a) == canonical(b)
    fun copy(value: JSONObject) = JSONObject(value.toString())
    fun portableUrl(raw: String): String {
        val u = URI(raw)
        require(u.scheme?.lowercase() in listOf("http", "https") && !u.host.isNullOrBlank()) { "直连地址无效" }
        if (u.rawUserInfo == null && u.rawQuery == null && u.rawFragment == null) return raw
        val host = if (u.host.contains(':') && !u.host.startsWith('[')) "[${u.host}]" else u.host
        val port = if (u.port == -1 || u.port == 80 && u.scheme == "http" || u.port == 443 && u.scheme == "https") "" else ":${u.port}"
        return "${u.scheme.lowercase()}://${host.lowercase()}$port${u.rawPath.ifEmpty { "/" }}"
    }
    val booleanSettings = setOf("windows:restoreLastLayoutOnStartup", "windows:reconnectRestoredWorkspace", "windows:connectionSidebarCollapsed", "macos:restoreLastLayoutOnStartup", "macos:reconnectRestoredWorkspace", "macos:connectionSidebarCollapsed", "android:browserFullscreen")
    private val tunnelFields = setOf("id", "name", "sshHost", "sshPort", "user", "remoteHost", "remotePort", "localPort", "authMode", "systemdUnit", "systemdScope", "systemdLogPath", "favorite", "group", "label", "rank")
    private val directFields = setOf("url", "favorite", "group", "label", "rank")
    fun validate(doc: JSONObject) {
        require(doc.keys().asSequence().toSet() == setOf("format", "version", "entries") && doc.optString("format") == "harness-portable-settings" && doc.opt("version") is Int && doc.getInt("version") == 1) { "不支持的同步文件格式" }
        require(doc.toString().toByteArray().size <= LIMIT) { "同步文件超过 1 MiB" }
        val entries = entries(doc); require(entries.length() <= 2000) { "同步项目超过上限" }
        for (key in entries.keys()) {
            require(key.length <= 9000 && key.none { it.isISOControl() }) { "同步项目标识无效" }
            val value = entries.get(key)
            if (key.startsWith("tunnel:") || key.startsWith("direct:")) {
                require(value is JSONObject) { "连接格式无效" }
                val tunnel = key.startsWith("tunnel:"); val fields = if (tunnel) tunnelFields else directFields
                require(value.keys().asSequence().toSet() == fields) { "连接含未知或敏感字段" }
                for (field in fields - setOf("favorite", "rank", "sshPort", "remotePort", "localPort")) {
                    val text = value.get(field); require(text is String && text.length <= 8192 && text.none { it.isISOControl() }) { "连接字段无效" }
                }
                require(value.get("favorite") is Boolean && value.get("rank") is Int && value.getInt("rank") in 0..1000000) { "收藏或排序无效" }
                if (tunnel) {
                    require(key == "tunnel:" + value.getString("id") && value.getString("id").matches(Regex("[A-Za-z0-9_-]{1,120}")) && value.getString("sshHost").isNotBlank() && value.getString("user").isNotBlank()) { "SSH 标识无效" }
                    for (port in listOf("sshPort", "remotePort", "localPort")) require(value.get(port) is Int && value.getInt(port) in 1..65535) { "SSH 端口无效" }
                    require(value.getString("authMode") in listOf("nssm", "manual", "systemd", "launchd")) { "认证模式无效" }
                } else require(key == "direct:" + value.getString("url") && portableUrl(value.getString("url")) == value.getString("url")) { "直连地址含凭据或登录参数" }
            } else if (key in booleanSettings) require(value is Boolean) { "偏好类型无效" }
            else {
                val choices = when (key) { "shared:appearance" -> listOf("system", "light", "dark"); "android:orientation" -> listOf("auto", "portrait", "landscape"); "windows:closeBehavior", "macos:closeBehavior" -> listOf("exit", "tray"); else -> emptyList() }
                require(value is String && value in choices) { "未知或无效的同步偏好" }
            }
        }
    }
    data class Merge(val document: JSONObject, val conflicts: List<String>)
    fun merge(base: JSONObject, local: JSONObject, remote: JSONObject, choice: String? = null): Merge {
        listOf(base, local, remote).forEach(::validate)
        val b = entries(base); val l = entries(local); val r = entries(remote); val out = empty(); val conflicts = mutableListOf<String>()
        val keys = (b.keys().asSequence() + l.keys().asSequence() + r.keys().asSequence()).toSortedSet()
        for (key in keys) {
            val bv = b.opt(key); val lv = l.opt(key); val rv = r.opt(key)
            val selected = when { same(lv, rv) -> lv; same(lv, bv) -> rv; same(rv, bv) -> lv; else -> { conflicts.add(key); if (choice == "remote") rv else lv } }
            if (selected != null) entries(out).put(key, if (selected is JSONObject) copy(selected) else selected)
        }
        validate(out); return Merge(out, conflicts)
    }
}
