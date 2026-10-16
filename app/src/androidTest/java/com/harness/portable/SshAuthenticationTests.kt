package com.harness.portable

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.jcraft.jsch.JSch
import com.jcraft.jsch.JSchException
import com.jcraft.jsch.KeyPair
import com.jcraft.jsch.Session
import org.junit.Assert.*
import org.junit.Assume.assumeNotNull
import org.junit.Test
import org.junit.runner.RunWith
import java.io.ByteArrayOutputStream

@RunWith(AndroidJUnit4::class)
class SshAuthenticationTests {
    private val ctx get() = InstrumentationRegistry.getInstrumentation().targetContext
    private val alias = "[authentication-regression]:22223"
    private fun port(): Int {
        val value = InstrumentationRegistry.getArguments().getString("ssh_auth_fixture")
        assumeNotNull(value)
        return value!!.toInt()
    }
    private fun session(user: String, key: Boolean = false): Session {
        val jsch = JSch().apply { setHostKeyRepository(TofuHostKeyRepository(ctx)) }
        if (key) {
            val identity = KeyPair.genKeyPair(jsch, KeyPair.RSA, 2048)
            try {
                val pem = ByteArrayOutputStream().also { identity.writePrivateKey(it) }.toByteArray()
                try { jsch.addIdentity("authentication-regression", pem, null, null) }
                finally { pem.fill(0) }
            } finally { identity.dispose() }
        }
        return jsch.getSession(user, "127.0.0.1", port()).apply { setHostKeyAlias(alias) }
    }
    private fun isolated(block: () -> Unit) {
        TofuHostKeyRepository(ctx).remove(alias, null)
        try { block() } finally { TofuHostKeyRepository(ctx).remove(alias, null) }
    }
    private fun connects(user: String, key: Boolean = false, password: String? = "fixture-password") = isolated {
        val value = session(user, key)
        try {
            SshAuthentication.connect(value, password, key, 10000)
            assertTrue(value.isConnected)
            assertEquals("yes", value.getConfig("StrictHostKeyChecking"))
            assertNull(value.userInfo)
        } finally { value.disconnect() }
    }

    @Test fun passwordFallbackRetainsCorrectPasswordAfterOrdinaryMethodRejectsIt() = isolated {
        val legacy = session("fixture")
        try {
            legacy.setPassword("fixture-password")
            legacy.setConfig("StrictHostKeyChecking", "yes")
            legacy.setConfig("PreferredAuthentications", "password,keyboard-interactive")
            legacy.setConfig("NumberOfPasswordPrompts", "1")
            assertTrue(runCatching { legacy.connect(10000) }.exceptionOrNull() is JSchException)
        } finally { legacy.disconnect() }
        val pin = TofuHostKeyRepository(ctx).getHostKey(alias, null).single().key
        val fixed = session("fixture")
        try {
            SshAuthentication.connect(fixed, "fixture-password", false, 10000)
            assertTrue(fixed.isConnected)
            assertNull(fixed.userInfo)
            assertEquals(pin, fixed.hostKey.key)
        } finally { fixed.disconnect() }
    }
    @Test fun ordinaryPasswordStillWorks() = connects("password")
    @Test fun localizedPasswordChallengeWorks() = connects("localized")
    @Test fun keyOnlyAuthenticationStillWorks() = connects("key", key = true, password = null)
    @Test fun rejectedKeyCanFallBackToSavedPassword() = connects("password", key = true)

    @Test fun wrongPasswordIsRejectedAndCallbackIsReleased() = isolated {
        val value = session("fixture")
        try {
            assertTrue(runCatching { SshAuthentication.connect(value, "wrong", false, 10000) }.exceptionOrNull() is JSchException)
            assertFalse(value.isConnected)
            assertNull(value.userInfo)
        } finally { value.disconnect() }
    }
    @Test fun loginPasswordIsNotUsedAsAnOtp() = isolated {
        val value = session("otp")
        try {
            assertTrue(runCatching { SshAuthentication.connect(value, "fixture-password", false, 10000) }.exceptionOrNull() is JSchException)
            assertFalse(value.isConnected)
            assertNull(value.userInfo)
        } finally { value.disconnect() }
    }
}
