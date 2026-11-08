package com.harness.portable

import okhttp3.Credentials
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.util.concurrent.TimeUnit

class ConfigurationSyncTests {
    private fun fixture() = JSONObject(javaClass.classLoader!!.getResourceAsStream("shared-configuration-v1.json")!!.bufferedReader().use { it.readText() })
    private fun options(server: MockWebServer) = WebDavOptions(server.url("/dav/Harness/").toString(), "同步用户", allowHttp = true)
    @Test fun sharedNativeFixtureAcceptsAllThreePlatformSections() {
        val doc = fixture(); SharedConfiguration.validate(doc)
        assertTrue(SharedConfiguration.entries(doc).getBoolean("android:browserFullscreen"))
        assertEquals("tray", SharedConfiguration.entries(doc).getString("windows:closeBehavior"))
        assertFalse(SharedConfiguration.entries(doc).getBoolean("macos:restoreLastLayoutOnStartup"))
    }
    @Test fun cloudPasswordsPrivatePathsAndReservedIdsAreRejected() {
        for (field in listOf("password", "identityFile", "authInput", "webdavPassword")) {
            val doc = fixture(); SharedConfiguration.entries(doc).getJSONObject("tunnel:shared-fixture-1").put(field, "local-secret")
            assertTrue(runCatching { SharedConfiguration.validate(doc) }.isFailure)
        }
        val doc = fixture(); val entries = SharedConfiguration.entries(doc); val p = entries.remove("tunnel:shared-fixture-1") as JSONObject
        entries.put("tunnel:#webdav-sync", p.put("id", "#webdav-sync"))
        assertTrue(runCatching { SharedConfiguration.validate(doc) }.isFailure)
    }
    @Test fun independentEditsMergeAndDeleteEditConflictsRequireAnExplicitChoice() {
        val base = fixture(); val local = SharedConfiguration.copy(base); val remote = SharedConfiguration.copy(base)
        SharedConfiguration.entries(local).put("shared:appearance", "light"); SharedConfiguration.entries(remote).put("windows:closeBehavior", "exit")
        val merged = SharedConfiguration.merge(base, local, remote); assertTrue(merged.conflicts.isEmpty())
        assertEquals("light", SharedConfiguration.entries(merged.document).getString("shared:appearance"))
        assertEquals("exit", SharedConfiguration.entries(merged.document).getString("windows:closeBehavior"))
        SharedConfiguration.entries(local).remove("tunnel:shared-fixture-1"); SharedConfiguration.entries(remote).getJSONObject("tunnel:shared-fixture-1").put("name", "云端修改")
        assertTrue("tunnel:shared-fixture-1" in SharedConfiguration.merge(base, local, remote).conflicts)
        assertFalse(SharedConfiguration.entries(SharedConfiguration.merge(base, local, remote, "local").document).has("tunnel:shared-fixture-1"))
        assertEquals("云端修改", SharedConfiguration.entries(SharedConfiguration.merge(base, local, remote, "remote").document).getJSONObject("tunnel:shared-fixture-1").getString("name"))
    }
    @Test fun unknownVersionsPreferencesAndInvalidFieldTypesFailClosed() {
        val docs = listOf(fixture().put("version", 2), fixture().also { SharedConfiguration.entries(it).put("shared:password", "secret") }, fixture().also { SharedConfiguration.entries(it).put("android:browserFullscreen", "true") })
        for (doc in docs) assertTrue(runCatching { SharedConfiguration.validate(doc) }.isFailure)
    }
    @Test fun embeddedLoginDataIsNotIncludedInPortableUrls() {
        assertEquals("https://fixture.invalid/page", SharedConfiguration.portableUrl("https://user:secret@fixture.invalid/page?token=secret#private"))
        assertEquals("https://fixture.invalid/a%2Fb", SharedConfiguration.portableUrl("https://fixture.invalid/a%2Fb"))
    }
    @Test fun realConditionalCreateUsesUtf8CredentialsAndDoesNotPutSecretsInTheDocument() {
        MockWebServer().use { server ->
            server.enqueue(MockResponse().setResponseCode(404)); server.enqueue(MockResponse().setResponseCode(204))
            val client = WebDavConfigurationClient(); client.begin(); val options = options(server)
            assertTrue(client.read(options, "同步密码").missing)
            assertTrue(client.write(options, "同步密码", fixture(), null, true))
            val get = server.takeRequest(); val put = server.takeRequest()
            assertEquals("GET", get.method); assertEquals("PUT", put.method)
            assertEquals("/dav/Harness/harness-settings-v1.json", put.path)
            assertEquals("*", put.getHeader("If-None-Match")); assertEquals(Credentials.basic("同步用户", "同步密码", Charsets.UTF_8), put.getHeader("Authorization"))
            val body = put.body.readUtf8(); assertFalse(body.contains("同步密码")); assertFalse(body.contains("identityFile")); SharedConfiguration.validate(JSONObject(body))
        }
    }
    @Test fun realConditionalReplaceAndPreconditionFailureAreReported() {
        MockWebServer().use { server ->
            server.enqueue(MockResponse().setBody(fixture().toString()).setHeader("ETag", "\"v1\"")); server.enqueue(MockResponse().setResponseCode(412))
            val client = WebDavConfigurationClient(); client.begin(); val options = options(server)
            val remote = client.read(options, "secret"); assertEquals("\"v1\"", remote.etag)
            assertFalse(client.write(options, "secret", fixture(), remote.etag, false))
            server.takeRequest(); assertEquals("\"v1\"", server.takeRequest().getHeader("If-Match"))
        }
    }
    @Test fun missingWeakWildcardAndMultiValueEtagsNeverCauseAnUpload() {
        MockWebServer().use { server ->
            val client = WebDavConfigurationClient(); client.begin()
            for (etag in listOf(null, "W/\"weak\"", "*", "\"a\", \"b\"")) assertTrue(runCatching { client.write(options(server), "secret", fixture(), etag, false) }.isFailure)
            assertNull(server.takeRequest(100, TimeUnit.MILLISECONDS))
        }
    }
    @Test fun redirectDoesNotForwardCredentialsToAnotherOrigin() {
        MockWebServer().use { server -> MockWebServer().use { other ->
            server.enqueue(MockResponse().setResponseCode(302).setHeader("Location", other.url("/stolen")))
            val client = WebDavConfigurationClient(); client.begin()
            assertTrue(runCatching { client.read(options(server), "secret") }.isFailure)
            assertNull(other.takeRequest(100, TimeUnit.MILLISECONDS))
        } }
    }
    @Test fun corruptAndOversizedChunkedResponsesAreNotTreatedAsAnEmptyLibrary() {
        MockWebServer().use { server ->
            server.enqueue(MockResponse().setBody("<html>login required</html>"))
            server.enqueue(MockResponse().setChunkedBody(" ".repeat(SharedConfiguration.LIMIT + 1), 8192))
            val client = WebDavConfigurationClient(); client.begin()
            repeat(2) { assertTrue(runCatching { client.read(options(server), "secret") }.isFailure) }
        }
    }
    @Test fun folderTestUsesPropfindDepthZeroAndPlainHttpNeedsExplicitConsent() {
        MockWebServer().use { server ->
            server.enqueue(MockResponse().setResponseCode(207).setBody("<multistatus/>"))
            val client = WebDavConfigurationClient(); client.begin(); client.test(options(server), "secret")
            val request = server.takeRequest(); assertEquals("PROPFIND", request.method); assertEquals("0", request.getHeader("Depth"))
            assertTrue(runCatching { WebDavConfigurationClient.folder(options(server).copy(allowHttp = false)) }.isFailure)
        }
    }
}
