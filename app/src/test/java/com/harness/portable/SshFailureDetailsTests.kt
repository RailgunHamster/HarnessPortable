package com.harness.portable

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SshFailureDetailsTests {
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
