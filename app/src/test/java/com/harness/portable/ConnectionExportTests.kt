package com.harness.portable

import org.junit.Assert.*
import org.junit.Test

class ConnectionExportTests {
    @Test fun exportedConnectionsExcludeUrlCredentialsAndDevicePrivateKeyPaths() {
        val profile = TunnelProfile(id = "fixture", name = "LAN fixture", sshHost = "fixture-lan", user = "fixture", identityFile = "/private/fixture-secret-key")
        val doc = ConnectionExport.document(listOf(profile), listOf("https://fixture:private-password@fixture.test:8443/a%2Fb?token=private-token#private-fragment"))
        val serialized = doc.toString()
        listOf("private-password", "private-token", "private-fragment", "fixture-secret-key").forEach { assertFalse(serialized.contains(it)) }
        assertEquals("https://fixture.test:8443/a%2Fb", doc.getJSONArray("directs").getString(0))
        assertEquals("fixture-lan", doc.getJSONArray("tunnels").getJSONObject(0).getString("sshHost"))
        assertEquals("", doc.getJSONArray("tunnels").getJSONObject(0).getString("identityFile"))
    }
}
