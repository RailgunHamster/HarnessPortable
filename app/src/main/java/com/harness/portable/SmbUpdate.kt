package com.harness.portable

import android.content.Context
import com.hierynomus.msfscc.fileinformation.FileStandardInformation
import com.hierynomus.msdtyp.AccessMask
import com.hierynomus.msfscc.FileAttributes
import com.hierynomus.mssmb2.SMB2CreateDisposition
import com.hierynomus.mssmb2.SMB2CreateOptions
import com.hierynomus.mssmb2.SMB2ShareAccess
import com.hierynomus.mssmb2.SMBApiException
import com.hierynomus.security.bc.BCSecurityProvider
import com.hierynomus.smbj.SMBClient
import com.hierynomus.smbj.SmbConfig
import com.hierynomus.smbj.auth.AuthenticationContext
import com.hierynomus.smbj.share.DiskShare
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.IOException
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
    private fun <T> withFile(raw: String, credentials: UpdateShareCredentials, action: (com.hierynomus.smbj.share.File) -> T): T {
        val address = SmbUpdateAddress.parse(raw)
        val host = HostResolver.resolve(address.host)?.ip ?: address.host
        val config = SmbConfig.builder().withTimeout(20, TimeUnit.SECONDS).withSoTimeout(20, TimeUnit.SECONDS)
            .withSecurityProvider(BCSecurityProvider()).build()
        try {
            SMBClient(config).use { client ->
                val connection = client.connect(host, address.port)
                try {
                    val session = connection.authenticate(credentials.authentication())
                    val share = session.connectShare(address.share) as DiskShare
                    share.openFile(address.path.replace('/', '\\'), setOf(AccessMask.FILE_READ_DATA, AccessMask.FILE_READ_ATTRIBUTES),
                        setOf(FileAttributes.FILE_ATTRIBUTE_NORMAL), setOf(SMB2ShareAccess.FILE_SHARE_READ, SMB2ShareAccess.FILE_SHARE_WRITE, SMB2ShareAccess.FILE_SHARE_DELETE),
                        SMB2CreateDisposition.FILE_OPEN, setOf(SMB2CreateOptions.FILE_NON_DIRECTORY_FILE)).use { file -> return action(file) }
                } finally {
                    // This operation owns a dedicated connection. Closing its
                    // transport releases the share/session without waiting for
                    // a network Logoff reply after the requested read completed.
                    connection.close(true)
                }
            }
        } catch (error: SMBApiException) {
            val message = when (error.statusCode) {
                0xC000006DL, 0xC000006AL, 0xC0000022L -> "家庭共享拒绝访问，请检查并保存共享账号和密码"
                0xC0000034L, 0xC000003AL, 0xC00000CCL -> "家庭共享中找不到更新文件，请检查共享名称和目录"
                else -> "读取家庭共享失败：${error.message}"
            }
            throw IOException(message, error)
        }
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
