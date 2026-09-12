package com.harness.portable

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

/**
 * Cross-component observable state of the SSH tunnel, owned by [SshTunnelService].
 */
object TunnelState {

    enum class Status { IDLE, CONNECTING, CONNECTED, RETRYING, FAILED, STOPPED }

    data class Info(
        val profileId: String? = null,
        val profileName: String? = null,
        val status: Status = Status.IDLE,
        val message: String? = null,
        /** Local loopback port actually bound for forwarding (0 = none). */
        val localPort: Int = 0,
        /** Verified launch-token URL fetched from the server (NSSM mode), if any. */
        val authUrl: String? = null
    )

    private val _flow = MutableStateFlow(Info())
    val flow: StateFlow<Info> = _flow

    fun set(info: Info) {
        _flow.value = info
    }
}
