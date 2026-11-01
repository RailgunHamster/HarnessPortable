package com.harness.portable

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

data class ApkFeed(
    val versionCode: Int,
    val versionName: String,
    val apkUrl: String,
    val notes: String
)

object ApkUpdate {
    const val PREF_SERVER = "update_server_url"
    const val DEFAULT_SERVER = "https://cnb.cool/railgunhamster/HarnessPortable"
    const val DEFAULT_GITHUB = "https://github.com/RailgunHamster/HarnessPortable"


    fun currentVersionName(ctx: Context): String =
        ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: "0"

    fun currentVersionCode(ctx: Context): Int {
        val info = ctx.packageManager.getPackageInfo(ctx.packageName, 0)
        return if (Build.VERSION.SDK_INT >= 28) info.longVersionCode.toInt() else @Suppress("DEPRECATION") info.versionCode
    }

    /**
     * Whether an address is a GitHub repository (whose feed lives at
     * `releases/latest/download/`) rather than a plain path or directory URL.
     */
    fun looksLikeGitHub(url: String): Boolean = repository(url, "github.com") != null
    fun looksLikeCnb(url: String): Boolean = cnbRepository(url) != null
    fun isOfficialGitHub(url: String): Boolean = repository(url,"github.com")?.let {
        it.first.equals("RailgunHamster",true) && it.second.equals("HarnessPortable",true)
    } == true

    fun storedServer(ctx: Context): String =
        ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE)
            .getString(PREF_SERVER, "")?.trim().orEmpty()
            .ifEmpty { DEFAULT_SERVER }

    fun saveServer(ctx: Context, url: String) {
        ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE)
            .edit()
            .putString(PREF_SERVER, url.trim())
            .apply()
    }

    fun feedUrl(server: String): String {
        val raw = server.trim().ifEmpty { DEFAULT_SERVER }.trimEnd('/')
        cnbRepository(raw)?.let { return "https://cnb.cool/$it/-/git/raw/updates/android.json" }
        val github = repository(raw, "github.com")
        if (github != null) {
            return "https://github.com/${github.first}/${github.second}/releases/latest/download/android.json"
        }
        val uri=java.net.URI(raw)
        require(uri.scheme?.lowercase() in listOf("http","https") && !uri.host.isNullOrBlank()) { "更新只支持 CNB / GitHub 或 HTTP(S)" }
        if (raw.endsWith("android.json", ignoreCase = true)) return raw
        return "$raw/android.json"
    }

    fun apkUrl(feedUrl: String, apkField: String): String {
        val uri=java.net.URI(feedUrl).resolve(apkField.trim())
        require(uri.scheme?.lowercase() in listOf("http","https") && !uri.host.isNullOrBlank() && uri.userInfo==null) { "无效的安装包下载地址" }
        return uri.toString()
    }

    private fun cnbRepository(url: String): String? {
        val uri=runCatching { java.net.URI(url.trim()) }.getOrNull() ?: return null
        if(uri.scheme != "https" || !uri.host.orEmpty().equals("cnb.cool",true) || uri.port !in listOf(-1,443) || uri.userInfo!=null || uri.query!=null || uri.fragment!=null) return null
        val path=uri.path.orEmpty().substringBefore("/-/").trim('/').removeSuffix(".git")
        return path.takeIf { it.split('/').size>=2 && it.split('/').all { part -> part.matches(Regex("[A-Za-z0-9_.-]+")) && part !in listOf(".","..") } }
    }

    fun fetchFeed(server: String, proxy: UpdateProxy = UpdateProxy()): ApkFeed {
        val url = feedUrl(server)
        val body = httpGet(url, proxy)
        val json = JSONObject(body)
        val apk = json.optString("apk").ifBlank {
            throw IllegalStateException("android.json 缺少 apk")
        }
        return ApkFeed(
            versionCode = json.optInt("versionCode"),
            versionName = json.optString("versionName").ifBlank { json.optInt("versionCode").toString() },
            apkUrl = apkUrl(url, apk),
            notes = json.optString("notes")
        )
    }

    fun download(apkUrl: String, dest: File, proxy: UpdateProxy = UpdateProxy(), onProgress: (Int) -> Unit) {
        dest.parentFile?.mkdirs()
        val tmp = File(dest.parentFile, dest.name + ".part")
        val conn = open(apkUrl, proxy)
        try {
            val length = conn.contentLengthLong.takeIf { it > 0 } ?: conn.contentLength.toLong()
            conn.inputStream.use { input ->
                tmp.outputStream().use { output ->
                    val buf = ByteArray(64 * 1024)
                    var read = 0L
                    while (true) {
                        val n = input.read(buf)
                        if (n <= 0) break
                        output.write(buf, 0, n)
                        read += n
                        if (length > 0) onProgress(((read * 100) / length).toInt().coerceIn(0, 100))
                    }
                }
            }
            if (dest.exists()) dest.delete()
            if (!tmp.renameTo(dest)) {
                tmp.copyTo(dest, overwrite = true)
                tmp.delete()
            }
        } finally {
            conn.disconnect()
        }
    }

    fun updateFile(ctx: Context): File = File(ctx.filesDir, "updates/HarnessPortable.apk")

    fun canInstallPackages(ctx: Context): Boolean =
        Build.VERSION.SDK_INT < 26 || ctx.packageManager.canRequestPackageInstalls()

    fun requestInstallPermission(activity: Activity) {
        if (Build.VERSION.SDK_INT >= 26) {
            activity.startActivity(
                Intent(
                    Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                    Uri.parse("package:${activity.packageName}")
                )
            )
        }
    }

    fun install(ctx: Context, apk: File) {
        val uri = FileProvider.getUriForFile(ctx, "${ctx.packageName}.fileprovider", apk)
        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        ctx.startActivity(intent)
    }

    private fun repository(url: String, domain: String): Pair<String, String>? {
        val uri = runCatching { java.net.URI(url.trim()) }.getOrNull() ?: return null
        val host = uri.host ?: return null
        if (uri.scheme?.lowercase() !in listOf("http", "https")) return null
        if (uri.userInfo!=null || uri.query!=null || uri.fragment!=null) return null
        if (!host.equals(domain, true) && !host.equals("www.$domain", true)) return null
        val parts = uri.path.orEmpty().trim('/').split('/').filter { it.isNotEmpty() }
        if (parts.size < 2) return null
        return parts[0] to parts[1].removeSuffix(".git")
    }

    private fun httpGet(url: String, proxy: UpdateProxy): String {
        val conn = open(url, proxy)
        try {
            val code = conn.responseCode
            val stream = if (code in 200..299) conn.inputStream else conn.errorStream
            val text = stream?.bufferedReader()?.readText().orEmpty()
            if (code !in 200..299) {
                throw IllegalStateException("HTTP $code ${text.take(200)}")
            }
            return text
        } finally {
            conn.disconnect()
        }
    }

    private fun open(url: String, proxy: UpdateProxy): HttpURLConnection {
        val conn = URL(url).openConnection(proxy.connectionProxy()) as HttpURLConnection
        conn.instanceFollowRedirects = true
        conn.connectTimeout = 20_000
        conn.readTimeout = 60_000
        conn.requestMethod = "GET"
        conn.setRequestProperty("User-Agent", "HarnessPortable")
        conn.setRequestProperty("Accept", "*/*")
        return conn
    }
}
