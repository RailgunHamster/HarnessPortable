package com.harness.portable

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.net.ServerSocket
import java.net.InetAddress
import java.net.Proxy
import java.util.concurrent.CopyOnWriteArrayList
import kotlin.concurrent.thread

@RunWith(AndroidJUnit4::class)
class UpdateProxyTests {
    @Test fun defaultsAndSavedPreference() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val old = UpdateProxyStore.load(ctx)
        try {
            assertFalse(UpdateProxy().enabled)
            assertEquals("localhost:7890", UpdateProxy().address)
            val next = UpdateProxy(true, "127.0.0.1:8080")
            UpdateProxyStore.save(ctx, next)
            assertEquals(next, UpdateProxyStore.load(ctx))
        } finally { UpdateProxyStore.save(ctx, old) }
    }

    @Test fun validationAndDisabledPolicy() {
        listOf("localhost:7890", "http://127.0.0.1:80/", "[::1]:7890").forEach { UpdateProxy.parseAddress(it) }
        listOf("localhost", "localhost:0", "localhost:65536", "https://localhost:7890", "socks5://localhost:7890",
            "http://user:pass@localhost:7890", "localhost:7890/path", "localhost:7890?x=1", "local host:7890").forEach {
            assertTrue(it, runCatching { UpdateProxy.parseAddress(it) }.isFailure)
        }
        assertSame(Proxy.NO_PROXY, UpdateProxy(false, "bad address").connectionProxy())
    }

    @Test fun feedDownloadAndRedirectUseProxy() {
        WireServer().use { server ->
            val proxy = UpdateProxy(true, server.address)
            val feed = ApkUpdate.fetchFeed("http://update.invalid", proxy)
            assertEquals("9.0.0", feed.versionName)
            val ctx = InstrumentationRegistry.getInstrumentation().targetContext
            val file = java.io.File(ctx.cacheDir, "proxy-test.apk")
            try {
                ApkUpdate.download(feed.apkUrl, file, proxy) { }
                assertEquals("package", file.readText())
                assertTrue(server.requests.any { it.startsWith("GET http://update.invalid/android.json") })
                assertTrue(server.requests.any { it.startsWith("GET http://download.invalid/final") })
            } finally { file.delete() }
        }
    }

    @Test fun disabledProxyConnectsDirectly() {
        WireServer().use { server ->
            val feed = ApkUpdate.fetchFeed("http://${server.address}", UpdateProxy(false, "bad address"))
            assertEquals("9.0.0", feed.versionName)
            assertTrue(server.requests.any { it.startsWith("GET /android.json ") })
        }
    }

    @Test fun httpsUsesConnectProxy() {
        WireServer().use { server ->
            assertTrue(runCatching { ApkUpdate.fetchFeed("https://update.invalid", UpdateProxy(true, server.address)) }.isFailure)
            assertTrue(server.requests.any { it.startsWith("CONNECT update.invalid:443 ") })
        }
    }

    @Test fun unavailableProxyDoesNotFallBack() {
        WireServer().use { origin ->
            val port = ServerSocket(0, 1, InetAddress.getByName("127.0.0.1")).use { it.localPort }
            assertTrue(runCatching { ApkUpdate.fetchFeed("http://${origin.address}", UpdateProxy(true, "127.0.0.1:$port")) }.isFailure)
            assertTrue(origin.requests.isEmpty())
        }
    }

    private class WireServer : AutoCloseable {
        private val socket = ServerSocket(0, 10, InetAddress.getByName("127.0.0.1"))
        val address = "127.0.0.1:${socket.localPort}"
        val requests = CopyOnWriteArrayList<String>()
        private val worker = thread(name = "UpdateProxyFixture", isDaemon = true) {
            while (!socket.isClosed) {
                try {
                    socket.accept().use { client ->
                        client.soTimeout = 5000
                        val reader = client.getInputStream().bufferedReader()
                        val request = reader.readLine().orEmpty()
                        while (!reader.readLine().isNullOrEmpty()) { }
                        requests.add(request)
                        val code: Int; val headers: String; val body: String
                        if (request.startsWith("CONNECT ")) { code = 502; headers = ""; body = "refused test tunnel" }
                        else if (request.contains("android.json")) { code = 200; headers = ""; body = """{"versionCode":999,"versionName":"9.0.0","apk":"test.apk"}""" }
                        else if (request.contains("test.apk")) { code = 302; headers = "Location: http://download.invalid/final\r\n"; body = "" }
                        else { code = 200; headers = ""; body = "package" }
                        client.getOutputStream().write("HTTP/1.1 $code Test\r\n${headers}Content-Length: ${body.toByteArray().size}\r\nConnection: close\r\n\r\n$body".toByteArray())
                    }
                } catch (error: java.io.IOException) { if (!socket.isClosed) throw error }
            }
        }
        override fun close() { socket.close(); worker.join(2000) }
    }
}
