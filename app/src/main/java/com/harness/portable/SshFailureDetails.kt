package com.harness.portable

/** JSch 0.2.21 emits this exact failure while reading the server identification, before authentication. */
internal object SshFailureDetails {
    fun closedBeforeAuthentication(message: String?): Boolean =
        message?.contains("connection is closed by foreign host", ignoreCase = true) == true

    const val BEFORE_AUTHENTICATION_EXPLANATION =
        "尚未开始密码认证，连接就被对端关闭了。请检查 SSH 地址、端口和服务器访问限制。"
}
