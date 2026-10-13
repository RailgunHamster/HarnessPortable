package com.harness.portable

import android.content.Context
import com.hierynomus.msfscc.fileinformation.FileStandardInformation
import com.hierynomus.msdtyp.AccessMask
import com.hierynomus.msfscc.FileAttributes
import com.hierynomus.mssmb2.SMB2CreateDisposition
import com.hierynomus.mssmb2.SMB2CreateOptions
import com.hierynomus.mssmb2.SMB2ShareAccess
import com.hierynomus.mssmb2.SMBApiException
import com.hierynomus.mssmb2.SMB2Dialect
import com.hierynomus.security.bc.BCSecurityProvider
import com.hierynomus.ntlm.NtlmException
import com.hierynomus.smbj.SMBClient
import com.hierynomus.smbj.SmbConfig
import com.hierynomus.smbj.auth.AuthenticationContext
import com.hierynomus.smbj.share.DiskShare
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.IOException
import java.io.EOFException
import java.net.URI
import java.util.concurrent.TimeUnit

internal data class SmbUpdateAddress(val host: String, val port: Int, val share: String, val path: String) {
    fun url(): String = URI("smb", null, host, if (port == 445) -1 else port, "/$share/$path", null, null).toASCIIString()
    fun feed(): SmbUpdateAddress = if (path.endsWith("android.json", true)) this
        else copy(path = listOf(path.trimEnd('/'), "android.json").filter { it.isNotEmpty() }.joinToString("/"))

    fun apk(field: String): String {
        if (field.startsWith("http://", true) || field.startsWith("https://", true)) return field
        val next = if (looksLikeSmb(field)) parse(field) else {
            val parts = field.replace('\\', '/').split('/')
            require(parts.all { it.isNotBlank() && it != "." && it != ".." }) { "安装包路径无效" }
            copy(path = (path.substringBeforeLast('/', "") + "/" + field.replace('\\', '/')).trimStart('/'))
        }
        require(next.host.equals(host, true) && next.port == port && next.share.equals(share, true)) {
            "共享更新的安装包必须位于同一服务器和共享中"
        }
        return next.url()
    }

    companion object {
        fun looksLikeSmb(raw: String): Boolean = raw.trim().let {
            it.startsWith("\\\\") || it.startsWith("//") || it.startsWith("smb://", true)
        }

        fun parse(raw: String): SmbUpdateAddress {
            val value = raw.trim()
            require(looksLikeSmb(value)) { "请填写完整共享路径，例如 \\\\server-home\\public\\Software\\HarnessPortable-Releases" }
            val uri = if (value.startsWith("smb://", true)) URI(value) else {
                val normalized = value.replace('\\', '/').trimStart('/')
                val host = normalized.substringBefore('/')
                val path = normalized.substringAfter('/', "")
                URI("smb", null, host, -1, "/$path", null, null)
            }
            require(uri.scheme.equals("smb", true) && !uri.host.isNullOrBlank() && uri.userInfo == null && uri.query == null && uri.fragment == null) { "共享地址无效，请将账号填写在共享账号字段中" }
            require(uri.port == -1 || uri.port in 1..65535) { "共享端口无效" }
            val parts = uri.path.orEmpty().trim('/').split('/')
            require(parts.isNotEmpty() && parts.all { it.isNotBlank() && it != "." && it != ".." && it.none { c -> c == '\\' || c.isISOControl() } }) { "共享地址必须包含服务器和共享名称，且不能包含上级路径" }
            return SmbUpdateAddress(uri.host.removeSurrounding("[", "]"), uri.port.takeIf { it > 0 } ?: 445, parts.first(), parts.drop(1).joinToString("/"))
        }
    }
}

data class UpdateShareCredentials(val account: String = "", val password: String = "") {
    internal fun authentication(): AuthenticationContext {
        val value = account.trim()
        if (value.isEmpty()) return AuthenticationContext.anonymous()
        val separator = value.indexOf('\\')
        return if (separator >= 0) AuthenticationContext(value.substring(separator + 1), password.toCharArray(), value.substring(0, separator))
            else AuthenticationContext(value, password.toCharArray(), "")
    }
}

internal object UpdateShareStore {
    fun load(ctx: Context): UpdateShareCredentials = UpdateShareCredentials(
        UiPreferences.prefs(ctx).getString("update_share_account", "").orEmpty(), SecureStore.getUpdateSharePassword(ctx))
    fun save(ctx: Context, credentials: UpdateShareCredentials) {
        SecureStore.setUpdateSharePassword(ctx, credentials.password)
        UiPreferences.prefs(ctx).edit().putString("update_share_account", credentials.account.trim()).apply()
    }
}

internal object SmbUpdate {
    internal enum class Stage(val label: String) { NEGOTIATE("协商连接"), AUTHENTICATE("登录共享"), SHARE("打开共享"), FILE("打开更新文件"), READ("读取更新文件") }
    private class Failure(val stage: Stage, cause: Exception) : IOException(cause)

    private fun Throwable.causes(): Sequence<Throwable> = generateSequence(this) { it.cause }
    private fun Throwable.status(): Long? = causes().filterIsInstance<SMBApiException>().firstOrNull()?.statusCode
    private fun Throwable.closedEarly(): Boolean = causes().any { it is EOFException }

    internal fun failureMessage(stage: Stage, guest: Boolean, error: Throwable): String {
        val reason = when (error.status()) {
            0xC000006DL, 0xC000006AL, 0xC0000072L, 0xC000015BL -> if (guest)
                "服务器不允许免登录访客访问，请填写并保存共享账号和密码；Everyone 共享权限不等于免登录"
                else "共享账号登录失败，请检查账号、密码及账号是否启用"
            0xC0000022L -> if (stage == Stage.AUTHENTICATE) {
                if (guest) "服务器要求登录，请填写并保存共享账号和密码；Everyone 共享权限不等于免登录"
                else "服务器拒绝共享账号登录，请检查账号和登录权限"
            } else "共享账号没有读取更新目录或文件的权限"
            0xC0000034L, 0xC000003AL, 0xC00000CCL -> "找不到更新文件，请检查共享名称和目录"
            else -> if (error.closedEarly()) {
                if (stage == Stage.AUTHENTICATE && guest) "服务器在访客登录时断开连接，请使用共享账号或确认服务器允许免登录访客访问"
                else "服务器提前断开 SMB 连接（EOF），请检查共享服务和网络连接"
            } else if (stage == Stage.AUTHENTICATE && guest && error.causes().any { it is NtlmException })
                "服务器未接受免登录访客访问，请填写并保存共享账号和密码；Everyone 共享权限不等于免登录"
            else error.causes().last().message ?: error.javaClass.simpleName
        }
        return "家庭服务器${stage.label}失败：$reason"
    }

    private fun <T> withFile(raw: String, credentials: UpdateShareCredentials, action: (com.hierynomus.smbj.share.File) -> T): T {
        val address = SmbUpdateAddress.parse(raw)
        val host = HostResolver.resolve(address.host)?.ip ?: address.host
        val guest = credentials.account.isBlank()
        val authentications = if (guest) listOf(AuthenticationContext.guest(), AuthenticationContext.anonymous())
            else listOf(credentials.authentication())
        fun attempt(authentication: AuthenticationContext, compatible: Boolean): T {
            var stage = Stage.NEGOTIATE
            val builder = SmbConfig.builder().withTimeout(20, TimeUnit.SECONDS).withSoTimeout(20, TimeUnit.SECONDS)
                .withSecurityProvider(BCSecurityProvider())
            if (compatible) builder.withDialects(SMB2Dialect.SMB_3_0_2, SMB2Dialect.SMB_3_0, SMB2Dialect.SMB_2_1, SMB2Dialect.SMB_2_0_2)
            val config = builder.build()
            try {
                SMBClient(config).use { client ->
                    val connection = client.connect(host, address.port)
                    try {
                        stage = Stage.AUTHENTICATE
                        val session = connection.authenticate(authentication)
                        stage = Stage.SHARE
                        val share = session.connectShare(address.share) as DiskShare
                        stage = Stage.FILE
                        val file = share.openFile(address.path.replace('/', '\\'), setOf(AccessMask.FILE_READ_DATA, AccessMask.FILE_READ_ATTRIBUTES),
                            setOf(FileAttributes.FILE_ATTRIBUTE_NORMAL), setOf(SMB2ShareAccess.FILE_SHARE_READ, SMB2ShareAccess.FILE_SHARE_WRITE, SMB2ShareAccess.FILE_SHARE_DELETE),
                            SMB2CreateDisposition.FILE_OPEN, setOf(SMB2CreateOptions.FILE_NON_DIRECTORY_FILE))
                        stage = Stage.READ
                        return action(file)
                    } finally {
                        // This dedicated transport owns the file, share and session.
                        // Closing it releases all handles without extra remote Close
                        // or Logoff requests, which must not mask the original error.
                        runCatching { connection.close(true) }
                    }
                }
            } catch (error: Exception) { throw Failure(stage, error) }
        }
        var compatible = false
        for ((index, authentication) in authentications.withIndex()) {
            while (true) {
                try { return attempt(authentication, compatible) }
                catch (error: Failure) {
                    // Retry an incompatible negotiate exactly once, before any
                    // credentials were sent. Preserve signing; never enable SMB1.
                    if (!compatible && error.stage == Stage.NEGOTIATE && error.closedEarly()) {
                        compatible = true
                        continue
                    }
                    // Null sessions and Guest logins are different on Windows
                    // and NAS servers. Blank credentials may try each once.
                    val guestRejected = error.closedEarly() || error.causes().any { it is NtlmException } ||
                        error.status() in setOf(0xC000006DL, 0xC000006AL, 0xC0000022L, 0xC0000072L, 0xC000015BL)
                    if (index < authentications.lastIndex && error.stage == Stage.AUTHENTICATE && guestRejected) break
                    throw IOException(failureMessage(error.stage, guest, error), error)
                }
            }
        }
        error("共享登录未完成")
    }

    fun readText(raw: String, credentials: UpdateShareCredentials): String = withFile(raw, credentials) { file ->
        file.inputStream.use { input ->
            val output = ByteArrayOutputStream()
            val buffer = ByteArray(8192)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                require(output.size() + count <= 1024 * 1024) { "更新索引过大" }
                output.write(buffer, 0, count)
            }
            output.toString("UTF-8")
        }
    }

    fun download(raw: String, destination: File, credentials: UpdateShareCredentials, onProgress: (Int) -> Unit) = withFile(raw, credentials) { file ->
        val size = file.getFileInformation(FileStandardInformation::class.java).endOfFile
        destination.parentFile?.mkdirs()
        val temporary = File(destination.parentFile, destination.name + ".part")
        try {
            file.inputStream.use { input -> temporary.outputStream().use { output ->
                val buffer = ByteArray(64 * 1024)
                var copied = 0L
                while (true) {
                    val count = input.read(buffer)
                    if (count < 0) break
                    output.write(buffer, 0, count); copied += count
                    if (size > 0) onProgress((copied * 100 / size).toInt().coerceIn(0, 100))
                }
                check(copied == size) { "共享安装包下载不完整，请重试" }
            } }
            if (!temporary.renameTo(destination)) { temporary.copyTo(destination, overwrite = true); temporary.delete() }
            onProgress(100)
        } finally { temporary.delete() }
    }
}
