package com.harness.portable

import android.content.Context
import com.jcraft.jsch.ChannelExec
import com.jcraft.jsch.Session
import java.io.ByteArrayOutputStream

/** Uses the same Linux helper as the desktop clients. Retrieval never tears down the tunnel. */
object SystemdAuthUrl {
    private const val FETCH_TIMEOUT_MS = 10_000L

    internal fun shellQuote(value: String) = "'" + value.replace("'", "'\"'\"'") + "'"

    private fun command(ctx: Context, profile: TunnelProfile): String {
        val script = ctx.assets.open("systemd-auth.sh").bufferedReader().use { it.readText() }
            .replace("\r\n", "\n")
        val unit = profile.systemdUnit.trim().ifBlank { "dsh-web.service" }
        return "sh -c ${shellQuote(script)} sh ${shellQuote(unit)} " +
            "${shellQuote(profile.systemdScope)} ${profile.remotePort} ${shellQuote(profile.systemdLogPath.trim())}"
    }

    fun fetch(ctx: Context, session: Session, profile: TunnelProfile): String? {
        return try {
            val channel = session.openChannel("exec") as ChannelExec
            try {
                channel.setCommand(command(ctx, profile))
                val output = ByteArrayOutputStream()
                channel.setOutputStream(output)
                channel.setErrStream(null)
                val deadline = System.nanoTime() + FETCH_TIMEOUT_MS * 1_000_000L
                channel.connect(FETCH_TIMEOUT_MS.toInt())
                while (!channel.isClosed && System.nanoTime() < deadline) Thread.sleep(100)
                if (!channel.isClosed || channel.exitStatus != 0) null
                else NssmAuthUrl.extractUrl(output.toString("UTF-8"))?.takeIf {
                    NssmAuthUrl.rewriteToLocal(it, profile.localPort, profile.remotePort) != null
                }
            } finally {
                channel.disconnect()
            }
        } catch (_: Exception) {
            null
        }
    }
}
