package com.harness.portable

import android.content.Context
import android.content.ContextWrapper
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import okhttp3.mockwebserver.Dispatcher
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.RecordedRequest
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.net.InetAddress
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class ConfigurationSyncNativeTests {
    private val baseContext get() = InstrumentationRegistry.getInstrumentation().targetContext
    private fun isolated(): Pair<Context, () -> Unit> {
        val names = mutableSetOf<String>(); val prefix = "sync-fixture-${UUID.randomUUID()}-"
        val context = object : ContextWrapper(baseContext) {
            override fun getApplicationContext(): Context = this
            override fun getSharedPreferences(name: String, mode: Int): android.content.SharedPreferences {
                synchronized(names) { names.add(prefix + name) }; return baseContext.getSharedPreferences(prefix + name, mode)
            }
        }
        return context to { names.forEach { baseContext.deleteSharedPreferences(it) } }
    }
    private class Cloud : AutoCloseable {
        val server = MockWebServer()
        var document: String? = null
        private var revision = 0
        init {
            server.dispatcher = object : Dispatcher() {
                override fun dispatch(request: RecordedRequest): MockResponse = synchronized(this@Cloud) {
                    val response = when (request.method) {
                        "PROPFIND" -> MockResponse().setResponseCode(207)
                        "GET" -> if (document == null) MockResponse().setResponseCode(404) else MockResponse().setBody(document!!)
                        "PUT" -> {
                            val condition = if (document == null) request.getHeader("If-None-Match") == "*" else request.getHeader("If-Match") == "\"v$revision\""
                            if (!condition) MockResponse().setResponseCode(412) else { document = request.body.readUtf8(); revision++; MockResponse().setResponseCode(204) }
                        }
                        else -> MockResponse().setResponseCode(405)
                    }
                    response.setHeader("ETag", "\"v$revision\"")
                }
            }
            server.start(InetAddress.getByName("127.0.0.1"), 0)
        }
        fun replace(value: String) = synchronized(this) { document = value; revision++ }
        fun options() = WebDavOptions("http://127.0.0.1:${server.port}/dav/Harness/", "fixture", allowHttp = true)
        override fun close() = server.close()
    }
    @Test fun twoNativeDevicesMergeWithoutTransferringPasswordsOrPrivateKeys() = runBlocking {
        val (a, cleanA) = isolated(); val (b, cleanB) = isolated()
        try { Cloud().use { cloud -> withContext(Dispatchers.Main) {
            val first = TunnelProfile(id = "qa-1", name = "本机连接", sshHost = "fixture.invalid", user = "fixture", identityFile = "/private/fixture-key")
            ProfileStore.saveTunnels(a, listOf(first)); SecureStore.setPassword(a, first.id, "native-ssh-secret"); SecureStore.setAuthInput(a, first.id, "native-web-secret")
            ProfileStore.saveTunnels(b, listOf(first.copy(id = "qa-2")))
            val sa = ConfigurationSync.isolated(a); val sb = ConfigurationSync.isolated(b)
            sa.configure(cloud.options(), "native-dav-secret"); sb.configure(cloud.options(), "native-dav-secret")
            sa.run(); assertEquals(sa.state.value.message, "success", sa.state.value.phase)
            sb.run(); assertEquals(sb.state.value.message, "success", sb.state.value.phase)
            sa.run(); assertEquals(sa.state.value.message, "success", sa.state.value.phase)
            assertEquals(2, ProfileStore.loadTunnels(a).size); assertEquals(2, ProfileStore.loadTunnels(b).size)
            assertEquals("native-ssh-secret", SecureStore.getPassword(a, first.id)); assertFalse(SecureStore.hasPassword(b, first.id))
            assertEquals("/private/fixture-key", ProfileStore.findTunnel(a, first.id)!!.identityFile); assertEquals("", ProfileStore.findTunnel(b, first.id)!!.identityFile)
            for (secret in listOf("native-ssh-secret", "native-web-secret", "native-dav-secret", "identityFile", "/private/")) assertFalse(cloud.document!!.contains(secret))
        } } } finally { cleanA(); cleanB() }
    }
    @Test fun desktopLabelsGroupsOrderAndOtherPlatformPreferencesSurviveAndroidRoundTrip() = runBlocking {
        val (context, clean) = isolated()
        try { Cloud().use { cloud ->
            val fixture = JSONObject(InstrumentationRegistry.getInstrumentation().context.assets.open("shared-configuration-v1.json").bufferedReader().use { it.readText() }); cloud.replace(fixture.toString())
            withContext(Dispatchers.Main) {
                val sync = ConfigurationSync.isolated(context); sync.configure(cloud.options(), "dav-secret"); sync.run()
                assertEquals("conflict", sync.state.value.phase)
                assertTrue(sync.state.value.conflicts.contains("shared:appearance"))
                sync.run("remote")
                assertEquals(sync.state.value.message, "success", sync.state.value.phase)
                assertTrue(UiPreferences.prefs(context).getStringSet(UiPreferences.FAVORITES, emptySet())!!.contains("tunnel:shared-fixture-1"))
                sync.run(); assertEquals(sync.state.value.message, "success", sync.state.value.phase)
                val roundTrip = SharedConfiguration.entries(JSONObject(cloud.document!!))
                assertEquals("测试分组", roundTrip.getJSONObject("tunnel:shared-fixture-1").getString("group"))
                assertEquals("测试网页", roundTrip.getJSONObject("direct:https://fixture.invalid/console").getString("label"))
                assertEquals("tray", roundTrip.getString("windows:closeBehavior")); assertFalse(roundTrip.getBoolean("macos:restoreLastLayoutOnStartup"))
                assertFalse(SecureStore.hasPassword(context, "shared-fixture-1"))
            }
        } } finally { clean() }
    }
    @Test fun changedIdentityCannotReuseLocalPasswordAndCorruptCloudDoesNotEraseLibrary() = runBlocking {
        val (context, clean) = isolated()
        try { Cloud().use { cloud -> withContext(Dispatchers.Main) {
            val profile = TunnelProfile(id = "qa-1", name = "fixture", sshHost = "fixture.invalid", user = "fixture", identityFile = "/private/key")
            ProfileStore.saveTunnels(context, listOf(profile)); SecureStore.setPassword(context, profile.id, "local-secret")
            val sync = ConfigurationSync.isolated(context); sync.configure(cloud.options()); sync.run(); assertEquals("success", sync.state.value.phase)
            val doc = JSONObject(cloud.document!!); SharedConfiguration.entries(doc).getJSONObject("tunnel:qa-1").put("sshHost", "other.invalid"); cloud.replace(doc.toString())
            sync.run(); assertEquals("success", sync.state.value.phase); assertFalse(SecureStore.hasPassword(context, profile.id)); assertEquals("", ProfileStore.findTunnel(context, profile.id)!!.identityFile)
            val before = UiPreferences.prefs(context).getString("tunnels", null); cloud.replace("<html>invalid config</html>")
            sync.run(); assertEquals("failed", sync.state.value.phase); assertEquals(before, UiPreferences.prefs(context).getString("tunnels", null))
        } } } finally { clean() }
    }
    @Test fun manualOnlyConfigurationDoesNotMakeAutomaticNetworkRequests() = runBlocking {
        val (context, clean) = isolated()
        try { Cloud().use { cloud -> withContext(Dispatchers.Main) {
            val sync = ConfigurationSync.isolated(context); sync.configure(cloud.options(), "local-dav-secret"); sync.run(automatic = true)
            assertEquals(0, cloud.server.requestCount)
            assertFalse(UiPreferences.prefs(context).all.toString().contains("local-dav-secret"))
            assertTrue(runCatching { sync.configure(cloud.options().copy(url = "http://127.0.0.1:${cloud.server.port}/different/")) }.isFailure)
        } } } finally { clean() }
    }
}
