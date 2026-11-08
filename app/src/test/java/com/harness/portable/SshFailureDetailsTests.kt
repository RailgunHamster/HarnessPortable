package com.harness.portable

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SshFailureDetailsTests {
    @Test fun wrappedAndroidDnsFailuresAreRecognizedBeforeAuthentication() {
        assertTrue(SshFailureDetails.nameResolutionFailure(java.io.IOException("Session.connect", java.net.UnknownHostException("fixture.invalid"))))
        assertTrue(SshFailureDetails.nameResolutionFailure(Exception("Session.connect: java.net.UnknownHostException: fixture.invalid")))
        assertFalse(SshFailureDetails.offersPasswordChange("连接失败：Session.connect: java.net.UnknownHostException: fixture.invalid"))
        assertFalse(SshFailureDetails.offersPasswordChange(SshFailureDetails.dnsExplanation("fixture.invalid")))
        assertFalse(SshFailureDetails.offersPasswordChange("Connection timed out"))
        assertTrue(SshFailureDetails.offersPasswordChange("认证失败：Auth failed for methods 'password,keyboard-interactive'"))
        assertTrue(SshFailureDetails.offersPasswordChange("未保存密码"))
    }
    @Test fun serverIdentificationEofIsRecognizedEvenWithTheUiPrefix() {
        assertTrue(SshFailureDetails.closedBeforeAuthentication("连接失败：connection is closed by foreign host"))
        assertTrue(SshFailureDetails.closedBeforeAuthentication("Connection is closed by foreign host"))
    }

    @Test fun authenticationAndOtherDisconnectsDoNotClaimThePasswordWasNeverAttempted() {
        listOf(null, "", "Auth failed for methods 'password,keyboard-interactive'", "Socket closed", "Connection reset", "SSH_MSG_DISCONNECT: 2 Too many authentication failures").forEach {
            assertFalse(it, SshFailureDetails.closedBeforeAuthentication(it))
        }
    }
}
