package com.harness.portable

import org.json.JSONArray
import org.json.JSONObject

/** Portable file export shares the sync URL allowlist and never reads the credential store. */
internal object ConnectionExport {
    fun document(tunnels: List<TunnelProfile>, directs: List<String>): JSONObject = JSONObject()
        .put("version", 1)
        .put("directs", JSONArray(directs.map(SharedConfiguration::portableUrl)))
        .put("tunnels", JSONArray(tunnels.map { p ->
            JSONObject().put("id", p.id).put("name", p.name).put("sshHost", p.sshHost).put("sshPort", p.sshPort)
                .put("user", p.user).put("remoteHost", p.remoteHost).put("remotePort", p.remotePort).put("localPort", p.localPort)
                .put("authMode", p.authMode).put("identityFile", "").put("systemdUnit", p.systemdUnit)
                .put("systemdScope", p.systemdScope).put("systemdLogPath", p.systemdLogPath)
        }))
}
