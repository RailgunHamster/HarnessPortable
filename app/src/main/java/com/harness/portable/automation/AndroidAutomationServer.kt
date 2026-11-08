package com.harness.portable.automation

import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.ThreadFactory
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

/** JSON-lines RPC over device loopback; expose it through an explicit adb forward. */
internal class AndroidAutomationServer(token: String, requestedPort: Int) : Closeable {
    private val secret = token.toByteArray(StandardCharsets.US_ASCII)
    private val listener = ServerSocket(requestedPort, 8, InetAddress.getByName("127.0.0.1"))
    val port: Int get() = listener.localPort
    private val threadIds = AtomicInteger()
    private val workers = ThreadPoolExecutor(2, 4, 30, TimeUnit.SECONDS, ArrayBlockingQueue(16), ThreadFactory { Thread(it, "HarnessApi-${threadIds.incrementAndGet()}").apply { isDaemon = true } })
    private val clients = java.util.Collections.synchronizedSet(mutableSetOf<Socket>())
    init {
        Thread({
            while (!listener.isClosed) {
                val client = try { listener.accept() } catch (_: Exception) { break }
                clients.add(client)
                try { workers.execute { serve(client) } } catch (_: Exception) { clients.remove(client); client.close() }
            }
        }, "HarnessApi-Accept").apply { isDaemon = true; start() }
    }
    private fun serve(socket: Socket) {
        try { socket.use {
            it.soTimeout = 15000
            val input = it.getInputStream(); val output = it.getOutputStream()
            repeat(64) {
                val line = ByteArrayOutputStream()
                while (true) {
                    val byte = input.read()
                    if (byte < 0) return
                    if (byte == 10) break
                    if (line.size() >= 1024 * 1024) return
                    line.write(byte)
                }
                var id: Any = JSONObject.NULL
                val response = JSONObject().put("jsonrpc", "2.0")
                try {
                    val request = JSONObject(line.toString("UTF-8"))
                    val provided = request.optString("auth").toByteArray(StandardCharsets.US_ASCII)
                    if (!MessageDigest.isEqual(secret, provided)) fail("unauthorized", "Invalid automation session")
                    id = request.opt("id") ?: JSONObject.NULL
                    if (request.optString("jsonrpc") != "2.0") fail("invalid_request", "Expected jsonrpc 2.0")
                    val params = request.optJSONObject("params") ?: if (request.has("params")) fail("invalid_argument", "params must be an object") else JSONObject()
                    val result = AndroidAutomation.dispatch(request.requiredString("method"), params)
                    response.put("result", result ?: JSONObject.NULL)
                } catch (e: Exception) {
                    val failure = e as? AutomationFailure
                    response.put("error", JSONObject().put("code", failure?.code ?: "invalid_request").put("message", failure?.message ?: "Invalid automation request"))
                }
                response.put("id", id)
                output.write((response.toString() + "\n").toByteArray(StandardCharsets.UTF_8)); output.flush()
                AndroidAutomation.afterResponse(response.opt("result"))
            }
        } } catch (_: Exception) { /* No request, token, or text-field input is logged. */ }
        finally { clients.remove(socket) }
    }
    override fun close() {
        listener.close()
        synchronized(clients) { clients.toList().forEach { runCatching { it.close() } }; clients.clear() }
        workers.shutdownNow(); secret.fill(0)
    }
}
