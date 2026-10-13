package com.harness.portable

import android.content.Context
import android.util.Base64
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.jcraft.jsch.*
import org.junit.Assert.*
import org.junit.Assume.assumeNotNull
import org.junit.Test
import org.junit.runner.RunWith
import java.net.HttpURLConnection
import java.net.URL

@RunWith(AndroidJUnit4::class)
class SshHostKeyTests {
    private val ctx get() = InstrumentationRegistry.getInstrumentation().targetContext
    private val prefs get() = ctx.getSharedPreferences("harness_portable_known_hosts", Context.MODE_PRIVATE)
    private val alias = "[host-key-regression]:22221"
    private fun fixturePort(): Int {
        val value = InstrumentationRegistry.getArguments().getString("ssh_fixture")
        assumeNotNull(value)
        return value!!.toInt()
    }
    private fun session(port: Int, repository: HostKeyRepository, algorithm: String? = null): Session =
        JSch().apply { setHostKeyRepository(repository) }.getSession("fixture", "127.0.0.1", port).apply {
            setHostKeyAlias(alias)
            setPassword("fixture-password")
            setConfig("StrictHostKeyChecking", "yes")
            setConfig("PreferredAuthentications", "password")
            setConfig("NumberOfPasswordPrompts", "1")
            algorithm?.let { setConfig("server_host_key", it) }
        }
    private fun clean() {
        TofuHostKeyRepository(ctx).remove(alias, null)
    }

    @Test fun savedRsaKeyIsPreferredOverNewlyAvailableEd25519AndForwardsTraffic() {
        val port = fixturePort()
        clean()
        val repository = TofuHostKeyRepository(ctx)
        try {
            val old = session(port, repository, "rsa-sha2-512")
            old.connect(10000); val pinned = old.hostKey.key; old.disconnect()
            // Reproduce 2.7.5: the server has the same RSA key, but the empty
            // listing lets the new crypto dependency select Ed25519 instead.
            val emptyListing = object : HostKeyRepository by repository {
                override fun getHostKey(host: String?, type: String?) = emptyArray<HostKey>()
            }
            val regressed = session(port, emptyListing)
            try {
                val error = runCatching { regressed.connect(10000) }.exceptionOrNull()
                assertTrue(error.toString(), error is JSchException && error.message.orEmpty().contains("HostKey has been changed"))
            } finally { regressed.disconnect() }
            val fixed = session(port, repository)
            try {
                fixed.connect(10000)
                assertEquals("ssh-rsa", fixed.hostKey.type)
                assertEquals(pinned, fixed.hostKey.key)
                val local = fixed.setPortForwardingL(0, "127.0.0.1", 45871)
                val request = URL("http://127.0.0.1:$local/").openConnection() as HttpURLConnection
                try { assertEquals("ssh-forward-regression-ok", request.inputStream.bufferedReader().use { it.readText() }) }
                finally { request.disconnect() }
            } finally { fixed.disconnect() }
        } finally { clean() }
    }

    @Test fun legacyEcdsaKeyIsListedBeforeMigrationAndSurvivesDefaultNegotiation() {
        val port = fixturePort()
        clean()
        val repository = TofuHostKeyRepository(ctx)
        try {
            val old = session(port, repository, "ecdsa-sha2-nistp256")
            old.connect(10000); val pinned = old.hostKey.key; old.disconnect()
            prefs.edit().remove("hk2_$alias").putString("hk_${alias}_ecdsa-sha2-nistp256", pinned).commit()
            assertEquals("ecdsa-sha2-nistp256", repository.getHostKey(alias, null).single().type)
            assertEquals(0, repository.getHostKey(alias, "ssh-rsa").size)
            val fixed = session(port, repository)
            try {
                fixed.connect(10000)
                assertEquals(pinned, fixed.hostKey.key)
                assertEquals("ecdsa-sha2-nistp256:$pinned", prefs.getString("hk2_$alias", null))
            } finally { fixed.disconnect() }
        } finally { clean() }
    }

    @Test fun actualChangedRsaKeyIsStillRejectedAndNeverReplacesPin() {
        val port = fixturePort()
        val changedPort = InstrumentationRegistry.getArguments().getString("ssh_changed_fixture")!!.toInt()
        clean()
        val repository = TofuHostKeyRepository(ctx)
        try {
            val old = session(port, repository, "rsa-sha2-512")
            old.connect(10000); val pin = old.hostKey.key; old.disconnect()
            val changed = session(changedPort, repository)
            try {
                val error = runCatching { changed.connect(10000) }.exceptionOrNull()
                assertTrue(error.toString(), error is JSchException && error.message.orEmpty().contains("HostKey has been changed"))
                assertEquals(pin, repository.getHostKey(alias, null).single().key)
            } finally { changed.disconnect() }
        } finally { clean() }
    }

    @Test fun sharedPasswordWritesLeaveExistingSshPasswordAndWebTokenIntact() {
        val id = "credential-regression"
        val priorSharePassword = SecureStore.getUpdateSharePassword(ctx)
        try {
            SecureStore.setPassword(ctx, id, "existing-ssh-password")
            SecureStore.setAuthInput(ctx, id, "existing-web-token")
            val encrypted = ctx.getSharedPreferences("harness_portable_secure", 0).getString("pw_$id", null)
            SecureStore.setUpdateSharePassword(ctx, "new-share-password")
            assertEquals("existing-ssh-password", SecureStore.getPassword(ctx, id))
            assertEquals("existing-web-token", SecureStore.getAuthInput(ctx, id))
            assertEquals(encrypted, ctx.getSharedPreferences("harness_portable_secure", 0).getString("pw_$id", null))
            SecureStore.setUpdateSharePassword(ctx, "")
            assertEquals("existing-ssh-password", SecureStore.getPassword(ctx, id))
        } finally {
            SecureStore.clearPassword(ctx, id); SecureStore.clearAuthInput(ctx, id)
            SecureStore.setUpdateSharePassword(ctx, priorSharePassword)
        }
    }

    @Test fun malformedStoredPinRemainsRejected() {
        clean()
        try {
            prefs.edit().putString("hk2_$alias", "ssh-rsa:corrupt").commit()
            assertEquals(0, TofuHostKeyRepository(ctx).getHostKey(alias, null).size)
            assertEquals(HostKeyRepository.CHANGED, TofuHostKeyRepository(ctx).check(alias, Base64.decode("AAAAB3NzaC1yc2EAAAADAQABAAAABAECAwQ=", 0)))
        } finally { clean() }
    }
}
