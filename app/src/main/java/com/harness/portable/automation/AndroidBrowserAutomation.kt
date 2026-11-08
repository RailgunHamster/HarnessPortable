package com.harness.portable.automation

import android.net.LocalSocket
import android.net.LocalSocketAddress
import android.util.Base64
import android.webkit.WebView
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.net.URI
import java.security.MessageDigest
import java.security.SecureRandom

/** Public DevTools protocol supplies browser user activation without injecting screen input. */
internal object AndroidBrowserAutomation {
    private const val LIMIT = 2 * 1024 * 1024
    private val mutex = Mutex()
    private val sockets = java.util.Collections.synchronizedSet(mutableSetOf<LocalSocket>())
    private val random = SecureRandom()
    @Volatile private var debugging = false

    fun reset() {
        synchronized(sockets) { sockets.toList().forEach { runCatching { it.close() } }; sockets.clear() }
        if (debugging) { debugging = false; WebView.setWebContentsDebuggingEnabled(false) }
    }

    suspend fun evaluate(browser: WebView, script: String, raw: Boolean): Any = mutex.withLock {
        val url = browser.url ?: fail("unavailable", "The embedded page is not ready")
        if (!AndroidAutomation.enabled) fail("stale", "Automation session has ended")
        WebView.setWebContentsDebuggingEnabled(true); debugging = true
        try {
            withContext(Dispatchers.IO) {
                val targets = JSONArray(http("/json/list"))
                val pages = (0 until targets.length()).map { targets.getJSONObject(it) }
                    .filter { it.optString("type") == "page" && it.optString("url") == url }
                if (pages.size != 1) fail("unavailable", "Cannot uniquely identify the embedded page")
                val path = URI(pages.single().getString("webSocketDebuggerUrl")).rawPath
                if (!path.startsWith("/devtools/page/") || path.any { it.isISOControl() }) fail("browser_error", "Invalid DevTools target")
                val value = command(path, script)
                if (!raw) value else when (value) { is String -> JSONObject.quote(value); else -> value.toString() }
            }
        } finally { reset() }
    }

    private fun connect(): LocalSocket {
        repeat(40) { attempt ->
            if (!debugging) fail("stale", "Browser automation has ended")
            val socket = LocalSocket(); sockets.add(socket)
            try {
                socket.soTimeout = 10000
                socket.connect(LocalSocketAddress("webview_devtools_remote_${android.os.Process.myPid()}", LocalSocketAddress.Namespace.ABSTRACT))
                return socket
            } catch (error: java.io.IOException) {
                sockets.remove(socket); socket.close()
                if (attempt == 39) throw error
                Thread.sleep(50)
            }
        }
        fail("unavailable", "Browser debugging is unavailable")
    }
    private fun headers(input: InputStream): Pair<Int, Map<String, String>> {
        val data = ByteArrayOutputStream()
        while (data.size() < 32768) {
            val next = input.read(); if (next < 0) fail("browser_error", "DevTools connection ended")
            data.write(next)
            val bytes = data.toByteArray(); val n = bytes.size
            if (n >= 4 && bytes[n-4] == 13.toByte() && bytes[n-3] == 10.toByte() && bytes[n-2] == 13.toByte() && bytes[n-1] == 10.toByte()) {
                val lines = data.toString("ISO-8859-1").split("\r\n")
                val status = lines.first().split(' ').getOrNull(1)?.toIntOrNull() ?: fail("browser_error", "Invalid DevTools response")
                return status to lines.drop(1).filter { ':' in it }.associate { it.substringBefore(':').lowercase() to it.substringAfter(':').trim() }
            }
        }
        fail("limit_exceeded", "DevTools response headers exceed the limit")
    }
    private fun exact(input: InputStream, length: Int): ByteArray {
        if (length !in 0..LIMIT) fail("limit_exceeded", "DevTools response exceeds the limit")
        val bytes = ByteArray(length); var offset = 0
        while (offset < length) { val count = input.read(bytes, offset, length-offset); if (count < 0) fail("browser_error", "Incomplete DevTools response"); offset += count }
        return bytes
    }
    private fun http(path: String): String = connect().use { socket ->
        try {
            socket.outputStream.write("GET $path HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n".toByteArray(Charsets.US_ASCII))
            socket.outputStream.flush()
            val (status, fields) = headers(socket.inputStream)
            if (status != 200) fail("browser_error", "DevTools target discovery failed")
            val length = fields["content-length"]?.toIntOrNull() ?: fail("browser_error", "Missing DevTools response length")
            exact(socket.inputStream, length).toString(Charsets.UTF_8)
        } finally { sockets.remove(socket) }
    }
    private fun send(output: OutputStream, opcode: Int, bytes: ByteArray) {
        output.write(0x80 or opcode)
        when {
            bytes.size < 126 -> output.write(0x80 or bytes.size)
            bytes.size <= 65535 -> { output.write(0x80 or 126); output.write(bytes.size ushr 8); output.write(bytes.size and 255) }
            else -> { output.write(0x80 or 127); for (shift in 56 downTo 0 step 8) output.write((bytes.size.toLong() ushr shift).toInt() and 255) }
        }
        val mask = ByteArray(4).also(random::nextBytes); output.write(mask)
        output.write(ByteArray(bytes.size) { (bytes[it].toInt() xor mask[it % 4].toInt()).toByte() }); output.flush()
    }
    private fun command(path: String, script: String): Any = connect().use { socket ->
        try {
            val key = Base64.encodeToString(ByteArray(16).also(random::nextBytes), Base64.NO_WRAP)
            val output = socket.outputStream; val input = socket.inputStream
            output.write(("GET $path HTTP/1.1\r\nHost: localhost\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: $key\r\n\r\n").toByteArray(Charsets.US_ASCII)); output.flush()
            val (status, fields) = headers(input)
            val accept = Base64.encodeToString(MessageDigest.getInstance("SHA-1").digest((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").toByteArray(Charsets.US_ASCII)), Base64.NO_WRAP)
            if (status != 101 || fields["sec-websocket-accept"] != accept) fail("browser_error", "DevTools handshake failed")
            val request = JSONObject().put("id", 1).put("method", "Runtime.evaluate").put("params", JSONObject().put("expression", script).put("returnByValue", true).put("awaitPromise", true).put("userGesture", true))
            send(output, 1, request.toString().toByteArray(Charsets.UTF_8))
            val message = ByteArrayOutputStream(); val deadline = android.os.SystemClock.elapsedRealtime() + 10000
            repeat(100) {
                val remaining = deadline - android.os.SystemClock.elapsedRealtime(); if (remaining <= 0) fail("timeout", "DevTools request timed out")
                socket.soTimeout = remaining.toInt()
                val head = exact(input, 2); val opcode = head[0].toInt() and 15
                if ((head[0].toInt() and 0x70) != 0 || (head[1].toInt() and 0x80) != 0) fail("browser_error", "Unsupported DevTools frame")
                var length = (head[1].toInt() and 127).toLong()
                if (length == 126L || length == 127L) {
                    val count = if (length == 126L) 2 else 8; length = 0
                    for (byte in exact(input, count)) { length = (length shl 8) or (byte.toLong() and 255); if (length > LIMIT || length < 0) fail("limit_exceeded", "DevTools response exceeds the limit") }
                }
                val bytes = exact(input, length.toInt())
                when (opcode) {
                    8 -> fail("browser_error", "DevTools closed the connection")
                    9 -> send(output, 10, bytes)
                    10 -> Unit
                    0, 1 -> {
                        if (message.size() + bytes.size > LIMIT) fail("limit_exceeded", "DevTools response exceeds the limit")
                        message.write(bytes)
                        if ((head[0].toInt() and 0x80) != 0) {
                            val response = JSONObject(message.toString("UTF-8")); message.reset()
                            if (response.optInt("id") == 1) {
                                if (response.has("error")) fail("browser_error", "DevTools command was rejected")
                                val result = response.getJSONObject("result")
                                if (result.has("exceptionDetails")) fail("script_failed", "Page script raised an exception")
                                val remote = result.getJSONObject("result")
                                return@use remote.opt("value") ?: remote.opt("unserializableValue") ?: JSONObject.NULL
                            }
                        }
                    }
                    else -> fail("browser_error", "Unsupported DevTools frame")
                }
            }
            fail("limit_exceeded", "Too many DevTools messages")
        } finally { sockets.remove(socket) }
    }
}
