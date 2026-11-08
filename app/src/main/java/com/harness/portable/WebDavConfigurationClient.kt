package com.harness.portable

import okhttp3.Call
import okhttp3.Credentials
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.net.URI
import java.util.concurrent.TimeUnit

internal data class WebDavOptions(val url: String = "", val username: String = "", val autoSync: Boolean = false, val allowHttp: Boolean = false)
internal class WebDavConfigurationClient {
    private val http = OkHttpClient.Builder().connectTimeout(10, TimeUnit.SECONDS).readTimeout(15, TimeUnit.SECONDS).callTimeout(25, TimeUnit.SECONDS).followRedirects(false).followSslRedirects(false).build()
    @Volatile private var active: Call? = null
    @Volatile private var cancelled = false
    fun begin() { cancelled = false }
    fun cancel() { cancelled = true; active?.cancel() }
    fun isCancelled() = cancelled
    fun checkCancelled() { check(!cancelled) { "同步已取消；下次同步会重新检查云端。" } }
    companion object {
        fun folder(options: WebDavOptions): String {
            val uri = URI(options.url)
            require(uri.scheme in listOf("https", "http") && !uri.host.isNullOrBlank() && uri.rawUserInfo == null && uri.rawQuery == null && uri.rawFragment == null && (uri.scheme == "https" || options.allowHttp) && ':' !in options.username && options.username.none { it.isISOControl() }) { "请填写 HTTPS WebDAV 目录；可信局域网 HTTP 需明确允许，地址不能含账号、密码或查询参数。" }
            return options.url.trimEnd('/') + "/"
        }
    }
    private fun request(options: WebDavOptions, password: String, method: String, body: ByteArray? = null, etag: String? = null, missing: Boolean = false, directory: Boolean = false): Response {
        checkCancelled()
        val b = Request.Builder().url(folder(options) + if (directory) "" else SharedConfiguration.FILE).header("Accept", "application/json")
        if (options.username.isNotEmpty() || password.isNotEmpty()) b.header("Authorization", Credentials.basic(options.username, password, Charsets.UTF_8))
        if (directory) b.header("Depth", "0")
        if (body != null) b.header(if (missing) "If-None-Match" else "If-Match", if (missing) "*" else etag!!)
        b.method(method, body?.toRequestBody("application/json; charset=utf-8".toMediaType()))
        return http.newCall(b.build()).also { active = it }.execute()
    }
    private fun check(code: Int) {
        if (code in 200..299) return
        error(when (code) { 401 -> "WebDAV 登录失败（401），请检查同步账号。"; 403 -> "WebDAV 拒绝访问（403），请检查目录权限。"; 404, 409 -> "WebDAV 目录不存在，请先创建私有同步目录。"; in 300..399 -> "WebDAV 返回重定向，请填写最终地址；未转发凭据。"; else -> "WebDAV 请求失败（HTTP $code）。" })
    }
    fun test(options: WebDavOptions, password: String) { request(options, password, "PROPFIND", directory = true).use { check(it.code) } }
    data class Remote(val document: JSONObject, val etag: String?, val missing: Boolean)
    fun read(options: WebDavOptions, password: String): Remote {
        request(options, password, "GET").use { response ->
            if (response.code == 404) return Remote(SharedConfiguration.empty(), null, true)
            check(response.code)
            val body = response.body ?: error("WebDAV 未返回配置")
            require(body.contentLength() <= SharedConfiguration.LIMIT) { "同步文件超过 1 MiB" }
            val bytes = ByteArrayOutputStream(); val buffer = ByteArray(8192)
            body.byteStream().use { stream -> while (true) { checkCancelled(); val n = stream.read(buffer); if (n < 0) break; require(bytes.size() + n <= SharedConfiguration.LIMIT) { "同步文件超过 1 MiB" }; bytes.write(buffer, 0, n) } }
            val document = JSONObject(bytes.toString("UTF-8")); SharedConfiguration.validate(document)
            return Remote(document, response.header("ETag"), false)
        }
    }
    fun write(options: WebDavOptions, password: String, document: JSONObject, etag: String?, missing: Boolean): Boolean {
        SharedConfiguration.validate(document)
        require(missing || etag != null && etag.matches(Regex("\"[!#-~\\u0080-\\uFFFF]*\""))) { "服务器未提供强 ETag，已停止上传，避免覆盖其他设备的修改。" }
        val bytes = SharedConfiguration.canonical(document).toByteArray(Charsets.UTF_8); require(bytes.size <= SharedConfiguration.LIMIT)
        request(options, password, "PUT", bytes, etag, missing).use { if (it.code == 412) return false; check(it.code); checkCancelled(); return true }
    }
}
