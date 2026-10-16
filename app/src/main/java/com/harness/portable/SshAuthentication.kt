package com.harness.portable

import com.jcraft.jsch.Session
import com.jcraft.jsch.UIKeyboardInteractive
import com.jcraft.jsch.UserInfo
import java.util.Locale
import java.util.Properties

/** Keep JSch's per-method password buffers separate during authentication fallback. */
object SshAuthentication {
    fun connect(session: Session, password: String?, hasIdentity: Boolean, timeout: Int) {
        val credentials = password?.takeIf { it.isNotEmpty() }?.let(::SavedPassword)
        val methods = buildList {
            if (hasIdentity) add("publickey")
            if (credentials != null) {
                add("password")
                add("keyboard-interactive")
            }
            if (isEmpty()) add("publickey")
        }.joinToString(",")
        session.setConfig(Properties().apply {
            put("StrictHostKeyChecking", "yes")
            put("PreferredAuthentications", methods)
            put("NumberOfPasswordPrompts", "1")
        })
        // setPassword shares one byte array between auth methods. The password
        // method wipes it on failure, so keyboard-interactive would send zeros.
        // These callbacks let each method obtain its own fresh UTF-8 buffer.
        session.userInfo = credentials
        try {
            session.connect(timeout)
        } finally {
            session.userInfo = null
            credentials?.clear()
        }
    }

    private class SavedPassword(private var saved: String?) : UserInfo, UIKeyboardInteractive {
        private var passwordAttempted = false
        private var interactiveAttempted = false

        override fun getPassword(): String? = saved
        override fun promptPassword(message: String?): Boolean {
            if (saved == null || passwordAttempted) return false
            passwordAttempted = true
            return true
        }
        override fun getPassphrase(): String? = null
        override fun promptPassphrase(message: String?): Boolean = false
        override fun promptYesNo(message: String?): Boolean = false
        override fun showMessage(message: String?) = Unit

        override fun promptKeyboardInteractive(
            destination: String?, name: String?, instruction: String?,
            prompt: Array<out String>?, echo: BooleanArray?
        ): Array<String>? {
            if (interactiveAttempted || prompt?.size != 1 || echo?.size != 1 || echo[0]) return null
            val label = prompt[0].lowercase(Locale.ROOT)
            // A saved login password must never be supplied as an OTP/token,
            // or repeated in additional rounds of a multi-factor challenge.
            if (listOf("one-time", "one time", "otp", "verification", "token", "验证码", "动态", "一次性").any { it in label }) return null
            if (!("password" in label || "密码" in label || "口令" in label)) return null
            val value = saved ?: return null
            interactiveAttempted = true
            return arrayOf(value)
        }

        fun clear() { saved = null }
    }
}
