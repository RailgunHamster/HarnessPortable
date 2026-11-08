package com.harness.portable

/** JSch 0.2.21 emits this exact failure while reading the server identification, before authentication. */
internal object SshFailureDetails {
    fun nameResolutionFailure(error: Throwable): Boolean {
        var current: Throwable? = error
        repeat(16) {
            val value = current ?: return false
            if (value is java.net.UnknownHostException || value.message?.contains("UnknownHostException", true) == true || value.message?.contains("无法解析") == true) return true
            current = value.cause
        }
        return false
    }

    fun offersPasswordChange(message: String?): Boolean = message != null &&
        !message.contains("UnknownHostException", true) && !message.contains("无法解析") &&
        !closedBeforeAuthentication(message) && SshIdentity.looksLikeAuthenticationFailure(message)

    fun dnsExplanation(host: String) = "无法解析服务器 $host。请检查当前 Wi-Fi 的 DNS 记录；同一广播局域网可使用机器名，也可填写可直达的 IP。尚未开始密码认证。"

    fun closedBeforeAuthentication(message: String?): Boolean =
        message?.contains("connection is closed by foreign host", ignoreCase = true) == true

    const val BEFORE_AUTHENTICATION_EXPLANATION =
        "尚未开始密码认证，连接就被对端关闭了。请检查 SSH 地址、端口和服务器访问限制。"
}
