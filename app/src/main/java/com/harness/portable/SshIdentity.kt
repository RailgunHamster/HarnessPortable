package com.harness.portable

import android.content.Context
import android.net.Uri
import java.io.File

object SshIdentity {
    fun looksLikeAuthenticationFailure(message: String): Boolean {
        val lower = message.lowercase()
        return lower.contains("auth") ||
            lower.contains("password") ||
            lower.contains("permission denied") ||
            lower.contains("too many") ||
            lower.contains("keyboard-interactive") ||
            lower.contains("publickey") ||
            lower.contains("identity") ||
            lower.contains("private key") ||
            message.contains("未保存密码") ||
            message.contains("私钥") ||
            message.contains("认证") ||
            message.contains("密码")
    }

    fun missingCredentialsMessage(identityFile: String): String {
        val path = identityFile.trim()
        if (path.isNotEmpty()) {
            return if (!File(path).isFile) "私钥文件不存在：$path"
            else "无法读取私钥（可能需要口令）：$path"
        }
        return "未保存密码，也没有可用的 SSH 私钥"
    }
}

object SshIdentityFiles {
    fun keyFile(ctx: Context, profileId: String): File =
        File(ctx.applicationContext.filesDir, "ssh-keys/$profileId")

    fun import(ctx: Context, profileId: String, uri: Uri): String {
        val dest = keyFile(ctx, profileId)
        dest.parentFile?.mkdirs()
        ctx.contentResolver.openInputStream(uri)?.use { input ->
            dest.outputStream().use { output -> input.copyTo(output) }
        } ?: throw IllegalArgumentException("无法读取所选私钥")
        dest.setReadable(false, false)
        dest.setReadable(true, true)
        dest.setWritable(false, false)
        dest.setWritable(true, true)
        return dest.absolutePath
    }

    fun deleteManaged(ctx: Context, profileId: String, identityFile: String = "") {
        val managed = keyFile(ctx, profileId)
        if (identityFile.isBlank()) {
            managed.delete()
            return
        }
        val selected = File(identityFile)
        if (!selected.exists() || selected.canonicalFile == managed.canonicalFile) {
            managed.delete()
        }
    }
}
