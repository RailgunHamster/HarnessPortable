package com.harness.portable

import java.net.URI
import java.net.URLEncoder

/**
 * Turns what the user typed (a dsh web token URL, a bare query, a
 * key=value pair, or the token itself) into the absolute URL the WebView
 * should open. Returns null when nothing usable was typed.
 */
object WebAuthInput {

    fun normalize(input: String?, host: String, port: Int): String? {
        val s = input?.trim().orEmpty()
        if (s.isEmpty()) return null

        // Absolute http(s) URL: keep it as typed, but only when it actually
        // carries a query — the token lives there.
        val uri = runCatching { URI(s) }.getOrNull()
        val scheme = uri?.scheme?.lowercase()
        if (uri != null && uri.isAbsolute && (scheme == "http" || scheme == "https")) {
            return if (uri.rawQuery.isNullOrEmpty()) null else s
        }

        val authority = host.trim().ifEmpty { "127.0.0.1" }
        val base = "http://$authority:$port"
        return when {
            // Server-printed query ("?token=…") pasted without the host.
            s.startsWith("?") -> base + s
            // key=value pair pasted without the leading "?".
            s.contains('=') && !s.contains('/') && s.none { it.isWhitespace() } -> "$base/?$s"
            // A bare token: dsh tokens are base64url ([A-Za-z0-9_-]).
            !s.contains('?') && !s.contains('/') && s.none { it.isWhitespace() } ->
                "$base/?token=${encodeQueryValue(s)}"
            else -> null
        }
    }
}

/**
 * Percent-encodes one query value for a token URL. Java's URLEncoder maps a
 * space to '+', which is wrong outside form encoding, so it is rewritten.
 */
internal fun encodeQueryValue(value: String): String =
    URLEncoder.encode(value, "UTF-8").replace("+", "%20")
