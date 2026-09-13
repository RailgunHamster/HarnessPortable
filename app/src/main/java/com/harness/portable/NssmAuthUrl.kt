package com.harness.portable

import android.util.Base64
import com.jcraft.jsch.Session
import java.io.ByteArrayOutputStream
import java.net.URI

/**
 * Obtains the current "dsh web" launch-token URL from the SSH peer when the
 * service there runs under NSSM. dsh prints its authenticated root URL to
 * stdout once at startup; NSSM's stdout redirection persists that line to a
 * log file, and `nssm get <service> AppStdout` names the file. The script
 * below discovers the dsh service, extracts the newest printed URL and
 * verifies it against the live server (expecting the 303 token-redirect) so
 * only a currently-valid URL is returned. Failures return null and callers
 * fall back to the plain host:port URL (whose 401 page opens the manual
 * paste dialog).
 */
object NssmAuthUrl {

    private const val FETCH_TIMEOUT_MS = 10_000L

    /** Exit codes: 0 = verified URL on stdout, 3 = not found, 4 = stale token. */
    internal val SCRIPT = """
§ErrorActionPreference = "Stop"
§ProgressPreference = "SilentlyContinue"
foreach (§c in (Get-CimInstance Win32_Service | Where-Object { §_.State -eq "Running" -and §_.PathName -match "nssm\.exe" })) {
  §exe = [regex]::Match(§c.PathName, "^""?(.+?nssm\.exe)").Groups[1].Value
  if (-not §exe) { continue }
  §app = & §exe get §c.Name Application 2>§null
  §params = & §exe get §c.Name AppParameters 2>§null
  if ("§app §params" -notmatch "dsh") { continue }
  §log = & §exe get §c.Name AppStdout 2>§null
  if (-not §log) { continue }
  §m = Select-String -Path "§log" -Pattern "dsh web: (http\S+)" | Select-Object -Last 1
  if (-not §m) { continue }
  §u = §m.Matches[0].Groups[1].Value
  §code = & curl.exe -s -o NUL -w "%{http_code}" --max-time 3 §u
  if ("§code" -eq "303") { Write-Output §u; exit 0 }
  exit 4
}
exit 3
""".trimIndent().replace('§', '$') + "\n"

    private val URL_REGEX = Regex("""https?://\S+""")

    /**
     * Runs the discovery script over an established JSch session. Returns the
     * verified absolute URL, or null on any failure/timeout. Never throws:
     * token retrieval must not take the tunnel down.
     */
    fun fetch(session: Session, expectedRemotePort: Int): String? {
        return try {
            val encoded = Base64.encodeToString(
                SCRIPT.toByteArray(Charsets.UTF_16LE), Base64.NO_WRAP
            )
            val channel = session.openChannel("exec") as com.jcraft.jsch.ChannelExec
            try {
                channel.setCommand("powershell -NoProfile -EncodedCommand $encoded")
                val out = ByteArrayOutputStream()
                channel.setOutputStream(out)
                channel.setErrStream(null)
                channel.connect(FETCH_TIMEOUT_MS.toInt())

                val deadline = System.currentTimeMillis() + FETCH_TIMEOUT_MS
                while (!channel.isClosed && System.currentTimeMillis() < deadline) {
                    Thread.sleep(100)
                }
                if (!channel.isClosed || channel.exitStatus != 0) {
                    null
                } else {
                    extractUrl(out.toString("UTF-8"))
                        ?.takeIf { url ->
                            expectedRemotePort <= 0 || portOf(url) == expectedRemotePort
                        }
                }
            } finally {
                try {
                    channel.disconnect()
                } catch (_: Exception) {
                }
            }
        } catch (_: Exception) {
            null
        }
    }

    /** Last absolute http(s) URL appearing in the output, if any. */
    fun extractUrl(output: String): String? =
        URL_REGEX.findAll(output).lastOrNull()?.value

    /**
     * Rewrites an authenticated URL so the WebView opens it through the local
     * forwarded port: keeps path + token query, replaces the authority with
     * 127.0.0.1:localPort. Pass 0 as expectedRemotePort to skip the
     * source-port sanity check.
     */
    fun rewriteToLocal(authUrl: String?, localPort: Int, expectedRemotePort: Int): String? {
        if (authUrl.isNullOrBlank()) return null
        val uri = runCatching { URI(authUrl.trim()) }.getOrNull() ?: return null
        if (uri.scheme !in listOf("http", "https")) return null
        if (expectedRemotePort > 0 && uri.port != expectedRemotePort) return null
        val pathAndQuery = buildString {
            append(uri.rawPath?.takeIf { it.isNotEmpty() } ?: "/")
            uri.rawQuery?.let { append('?').append(it) }
        }
        return "http://127.0.0.1:$localPort$pathAndQuery"
    }

    private fun portOf(url: String): Int =
        runCatching { URI(url).port }.getOrDefault(-1)

    /** True when a page body looks like the dsh "reopen the URL" rejection. */
    fun looksLikeAuthRequired(text: String): Boolean =
        text.contains("dsh web authentication required", ignoreCase = true) ||
                (text.contains("authentication required", ignoreCase = true) &&
                        text.contains("reopen the url", ignoreCase = true))
}
