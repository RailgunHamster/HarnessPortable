package com.harness.portable

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import org.junit.Assume.assumeNotNull
import org.junit.runner.RunWith
import java.io.File

@RunWith(AndroidJUnit4::class)
class UpdateSourcesTests {
    private fun fixtureServer(): String {
        val address = InstrumentationRegistry.getArguments().getString("smb_fixture")
        assumeNotNull(address)
        return "smb://$address/public/releases"
    }
    @Test fun defaultHomeShareIsUsableAndBuildsFeedAndApkAddresses() {
        val sources = UpdateSources(UpdateSourceStore.DEFAULT_HOME, ApkUpdate.DEFAULT_SERVER, UpdateSourceKind.File)
        assertTrue(sources.selectedLooksUsable())
        assertEquals("smb://server-home/public/Software/HarnessPortable-Releases/android.json", sources.feedUrl())
        assertEquals("smb://server-home/public/Software/HarnessPortable-Releases/test.apk", ApkUpdate.apkUrl(sources.feedUrl(), "test.apk"))
        assertEquals(sources.feedUrl(), ApkUpdate.feedUrl("smb://server-home/public/Software/HarnessPortable-Releases/"))
    }

    @Test fun selectionPersistsWithoutChangingEitherSavedAddress() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val prior = UpdateSourceStore.load(ctx)
        val original = UpdateSources(UpdateSourceStore.DEFAULT_HOME, "https://github.com/example/repo", UpdateSourceKind.GitHub)
        try {
            UpdateSourceStore.save(ctx, original)
            assertEquals(original.copy(selected = UpdateSourceKind.File), UpdateSourceStore.select(ctx, UpdateSourceKind.File))
            assertEquals(UpdateSourceKind.File, UpdateSourceStore.load(ctx).selected)
            assertEquals(original, UpdateSourceStore.select(ctx, UpdateSourceKind.GitHub))
        } finally { UpdateSourceStore.save(ctx, prior) }
    }

    @Test fun sharePathsEncodeNamesAndRejectCredentialOrTraversalAddresses() {
        assertEquals("smb://host:1445/public/Release%20files/android.json", ApkUpdate.feedUrl("smb://host:1445/public/Release%20files"))
        assertEquals("smb://host/public/Release%20files/android.json", ApkUpdate.feedUrl("\\\\host\\public\\Release files"))
        listOf("smb://host", "smb://user:password@host/public", "smb://host/public/../secret", "smb://host:99999/public").forEach {
            assertTrue(it, runCatching { SmbUpdateAddress.parse(it) }.isFailure)
        }
        val feed = "smb://host/public/releases/android.json"
        listOf("../secret.apk", "smb://other/public/test.apk", "smb://host/private/test.apk").forEach {
            assertTrue(it, runCatching { ApkUpdate.apkUrl(feed, it) }.isFailure)
        }
    }

    @Test fun sharePasswordIsEncryptedAndDomainAccountIsParsed() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val prior = UpdateShareStore.load(ctx)
        try {
            val credentials = UpdateShareCredentials("DOMAIN\\fixture", "fixture-secret")
            UpdateShareStore.save(ctx, credentials)
            assertEquals(credentials, UpdateShareStore.load(ctx))
            assertFalse(ctx.getSharedPreferences("harness_portable_secure", 0).getString("update_share_password", "").orEmpty().contains("fixture-secret"))
            assertEquals("DOMAIN", credentials.authentication().domain)
            assertEquals("fixture", credentials.authentication().username)
            assertTrue(UpdateShareCredentials().authentication().isAnonymous)
        } finally { UpdateShareStore.save(ctx, prior) }
    }

    @Test fun authenticatedShareReadsFeedAndDownloadsCompletePackage() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val credentials = UpdateShareCredentials("fixture", "fixture-password")
        val server = fixtureServer()
        val feed = ApkUpdate.fetchFeed(server, UpdateProxy(true, "invalid proxy"), credentials)
        assertEquals("9.0.0", feed.versionName)
        assertEquals("$server/test.apk", feed.apkUrl)
        val destination = File(ctx.cacheDir, "smb-test.apk")
        val progress = mutableListOf<Int>()
        try {
            ApkUpdate.download(feed.apkUrl, destination, UpdateProxy(true, "invalid proxy"), credentials) { progress.add(it) }
            assertEquals("smb-update-fixture", destination.readText())
            assertEquals(100, progress.last())
        } finally { destination.delete() }
    }

    @Test fun incorrectSharePasswordFailsWithoutDownloading() {
        val server = fixtureServer()
        val error = runCatching { ApkUpdate.fetchFeed(server, shareCredentials = UpdateShareCredentials("fixture", "wrong")) }.exceptionOrNull()
        assertNotNull(error)
        assertTrue(error!!.message.orEmpty(), error.message.orEmpty().contains("共享账号和密码"))
    }
}
