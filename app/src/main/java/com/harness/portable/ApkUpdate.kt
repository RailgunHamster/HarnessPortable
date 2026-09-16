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
    const val DEFAULT_SERVER = "https://github.com/RailgunHamster/HarnessPortable"

    /** The LAN release directory the PC reads directly and phones cannot. */
    const val DEFAULT_HOME_DIR = "\\\\server-home\\public\\Software\\HarnessPortable-Releases"

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
    fun looksLikeGitHub(url: String): Boolean = githubRepo(url) != null

    /**
     * Whether an address is a filesystem path — the UNC form of the home
     * directory, or any Windows / POSIX path. A phone cannot read one.
     */
    fun looksLikeReadablePath(url: String): Boolean {
        val v = url.trim()
        if (v.isEmpty()) return false
        if (v.startsWith("http://", true) || v.startsWith("https://", true)) return false
        return v.startsWith("\\\\") || v.startsWith("//") ||
            v.matches(Regex("^[A-Za-z]:[\\\\/].*")) || v.startsWith("/")
    }

    /** Classifies an address the way the update feed expects to read it. */
    fun classify(url: String): UpdateSourceKind =
        if (looksLikeGitHub(url)) UpdateSourceKind.GitHub else UpdateSourceKind.File

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
        val github = githubRepo(raw)
        if (github != null) {
            return "https://github.com/${github.first}/${github.second}/releases/latest/download/android.json"
        }
        if (raw.endsWith("android.json", ignoreCase = true)) return raw
        // A directory path (UNC or local) or a plain http directory both just
        // get the feed name appended, with the right separator.
        return if (looksLikeReadablePath(raw)) "$raw\\android.json" else "$raw/android.json"
    }

    fun apkUrl(feedUrl: String, apkField: String): String {
        val apk = apkField.trim()
        if (apk.startsWith("http://", true) || apk.startsWith("https://", true)) return apk
        // Resolve a bare file name next to the feed. A UNC feed is
        // backslash-separated, so cutting at '/' alone would yield the whole
        // path rather than its directory and double the file name.
        val sep = if (looksLikeReadablePath(feedUrl)) maxOf(feedUrl.lastIndexOf('\\'), feedUrl.lastIndexOf('/'))
        else feedUrl.lastIndexOf('/')
        val base = if (sep >= 0) feedUrl.substring(0, sep + 1) else feedUrl
        return base + apk.trimStart('/', '\\')
    }

    fun fetchFeed(server: String): ApkFeed {
        val url = feedUrl(server)
        val body = httpGet(url)
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

    fun download(apkUrl: String, dest: File, onProgress: (Int) -> Unit) {
        dest.parentFile?.mkdirs()
        val tmp = File(dest.parentFile, dest.name + ".part")
        val conn = open(apkUrl)
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

    private fun githubRepo(url: String): Pair<String, String>? {
        val uri = runCatching { Uri.parse(url) }.getOrNull() ?: return null
        val host = uri.host ?: return null
        if (!host.equals("github.com", true) && !host.equals("www.github.com", true)) return null
        val parts = uri.path.orEmpty().trim('/').split('/').filter { it.isNotEmpty() }
        if (parts.size < 2) return null
        return parts[0] to parts[1]
    }

    private fun httpGet(url: String): String {
        val conn = open(url)
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

    private fun open(url: String): HttpURLConnection {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.instanceFollowRedirects = true
        conn.connectTimeout = 20_000
        conn.readTimeout = 60_000
        conn.requestMethod = "GET"
        conn.setRequestProperty("User-Agent", "HarnessPortable")
        conn.setRequestProperty("Accept", "*/*")
        return conn
    }
}
