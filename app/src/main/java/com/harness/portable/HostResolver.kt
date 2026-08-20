package com.harness.portable

import android.os.SystemClock
import android.util.Log
import com.jcraft.jsch.SocketFactory
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.net.SocketTimeoutException
import java.net.URI
import java.net.UnknownHostException
import kotlin.random.Random

/**
 * Resolves bare host names (no dot, not an IP literal) for the SSH tunnel:
 *
 *  1. System DNS — catches Tailscale MagicDNS (bare name -> 100.x.y.z) and
 *     any LAN DNS that knows the name.
 *  2. NetBIOS (NBNS broadcast on UDP 137) — Windows machines on the local
 *     LAN answer for their registered names, which Android does not resolve.
 *
 * Both run in parallel with hard deadlines; a Tailscale-range DNS answer
 * (100.64.0.0/10) wins, then any DNS answer, then NetBIOS.
 */
object HostResolver {

    data class Resolution(val ip: String, val source: String)
    data class LanMachine(val name: String, val ip: String)

    private const val TTL_MS = 5 * 60 * 1000L
    private val cache = HashMap<String, Pair<Resolution, Long>>()

    fun isIpLiteral(host: String): Boolean {
        val h = host.trim()
        if (h.isEmpty()) return false
        if (h.contains(':')) return true // IPv6
        val parts = h.split('.')
        if (parts.size != 4) return false
        return parts.all { p -> p.toIntOrNull() in 0..255 }
    }

    /** 100.64.0.0/10 — the CGNAT range Tailscale assigns. */
    fun isTailscaleAddress(ip: String): Boolean {
        val parts = ip.split('.')
        if (parts.size != 4) return false
        val a = parts[0].toIntOrNull() ?: return false
        val b = parts[1].toIntOrNull() ?: return false
        return a == 100 && b in 64..127
    }

    /** True when the phone currently has an active Tailscale interface. */
    fun tailscaleActive(): Boolean {
        return try {
            listInterfaces().any { ni ->
                ni.name.startsWith("tailscale") && ni.interfaceAddresses.any { ia ->
                    (ia.address is Inet4Address) &&
                            isTailscaleAddress(ia.address.hostAddress ?: "")
                }
            }
        } catch (_: Exception) {
            false
        }
    }

    fun resolve(host: String): Resolution? {
        val key = host.trim().lowercase()
        if (key.isEmpty()) return null
        if (isIpLiteral(key)) return Resolution(key, "ip")

        synchronized(cache) {
            cache[key]?.let { (res, at) ->
                if (SystemClock.elapsedRealtime() - at < TTL_MS) return res
                cache.remove(key)
            }
        }

        var dnsIp: String? = null
        var nbCandidates: List<String> = emptyList()

        val dnsThread = Thread {
            dnsIp = try {
                InetAddress.getAllByName(host)
                    .firstOrNull { it is Inet4Address }?.hostAddress
            } catch (_: Exception) {
                null
            }
        }.apply { isDaemon = true; start() }

        // NetBIOS names are <= 15 chars; longer names can only come from DNS.
        val nbThread = if (key.length <= 15) Thread {
            nbCandidates = NetBios.resolveAll(key, 1500)
        }.apply { isDaemon = true; start() } else null

        dnsThread.join(2500)
        nbThread?.join(2500)

        val d = dnsIp
        // A multihomed responder answers with every interface IP (VMware
        // adapters included); prefer the one on a subnet we actually share.
        val n = pickReachable(nbCandidates)
        Log.d(
            "HarnessTunnel",
            "resolve '$host': dns=$d nbns=$nbCandidates tailscaleIf=${tailscaleActive()}" +
                    " -> picked ${n ?: "none"}"
        )
        // Priority: Tailscale MagicDNS > NetBIOS > plain DNS.
        // NetBIOS beats plain DNS because carrier DNS can hijack unknown
        // names; a Tailscale-range DNS answer always wins by design.
        val res = when {
            d != null && isTailscaleAddress(d) -> Resolution(d, "tailscale")
            n != null -> Resolution(n, "netbios")
            d != null -> Resolution(d, "dns")
            else -> null
        } ?: return null

        synchronized(cache) { cache[key] = res to SystemClock.elapsedRealtime() }
        return res
    }

    fun invalidate(host: String) {
        synchronized(cache) { cache.remove(host.trim().lowercase()) }
    }

    /** Bounded NetBIOS browse used only for address-field suggestions. */
    fun discoverLanMachines(timeoutMs: Int = 1800): List<LanMachine> =
        NetBios.discover(timeoutMs)

    /**
     * True when [url] is an http(s) URL whose host is a *name* (not an IP
     * literal) that [resolveUrl] would try to resolve. Cheap and I/O-free:
     * callers use it to skip the resolving flow entirely for IP URLs.
     */
    fun urlNeedsResolve(url: String): Boolean {
        val host = parseHttpUrl(url)?.host ?: return false
        return host.isNotEmpty() && !isIpLiteral(host)
    }

    /**
     * Resolves the host name inside an http(s) URL to an IPv4 literal, e.g.
     * http://winserver:4096 -> http://192.168.0.104:4096. Returns null when
     * the URL has no host name to resolve (IP literal, non-http scheme,
     * unparseable) or the name is unknown — callers then fall back to the
     * browser's own resolution, i.e. the original URL.
     *
     * Blocking (DNS + NetBIOS, up to a few seconds): call off the main thread.
     */
    fun resolveUrl(url: String): String? {
        if (!urlNeedsResolve(url)) return null
        val host = parseHttpUrl(url)?.host ?: return null
        val res = resolve(host) ?: return null
        return replaceHost(url, res.ip)
    }

    /**
     * Replaces the host inside an http(s) URL with [ip], keeping scheme,
     * userinfo, port, path, query and fragment untouched. Returns null when
     * the URL cannot be parsed or its host cannot be located verbatim in the
     * authority (e.g. bracketed IPv6).
     */
    fun replaceHost(url: String, ip: String): String? {
        if (ip.isBlank()) return null
        val s = url.trim()
        val host = parseHttpUrl(s)?.host ?: return null
        if (host.isEmpty()) return null

        val schemeEnd = s.indexOf("://")
        if (schemeEnd < 0) return null
        val authorityStart = schemeEnd + 3

        // The authority runs to the first path/query/fragment character.
        var authorityEnd = authorityStart
        while (authorityEnd < s.length && s[authorityEnd] !in "/?#") authorityEnd++

        // Skip userinfo (may itself contain ':'); host starts after the last '@'.
        var hostStart = authorityStart
        for (i in authorityStart until authorityEnd) {
            if (s[i] == '@') hostStart = i + 1
        }

        // The host ends at the port separator, if any.
        var hostEnd = authorityEnd
        for (i in hostStart until authorityEnd) {
            if (s[i] == ':') {
                hostEnd = i
                break
            }
        }

        val rawHost = s.substring(hostStart, hostEnd)
        if (!rawHost.equals(host, ignoreCase = true)) {
            return null // percent-encoded or otherwise unusual — don't guess
        }

        val replacement = if (ip.contains(':')) "[$ip]" else ip // IPv6 needs brackets
        return s.substring(0, hostStart) + replacement + s.substring(hostEnd)
    }

    private fun parseHttpUrl(url: String): URI? {
        val s = url.trim()
        if (s.isEmpty()) return null
        val uri = try {
            URI(s)
        } catch (_: Exception) {
            return null
        }
        if (!uri.isAbsolute) return null
        return when (uri.scheme?.lowercase()) {
            "http", "https" -> uri
            else -> null
        }
    }

    /**
     * Picks the NetBIOS answer IP that shares a subnet with one of our
     * interfaces (excluding loopback and Tailscale); falls back to the
     * first candidate. Returns null for an empty list.
     */
    private fun pickReachable(candidates: List<String>): String? {
        if (candidates.isEmpty()) return null
        candidates.forEach { c ->
            listInterfaces().forEach { ni ->
                if (ni.isLoopback || ni.name.startsWith("tailscale")) return@forEach
                ni.interfaceAddresses.forEach { ia ->
                    val local = ia.address as? Inet4Address ?: return@forEach
                    if (sameSubnet(local.hostAddress ?: "", ia.networkPrefixLength, c)) return c
                }
            }
        }
        return candidates.first()
    }

    private fun sameSubnet(a: String, prefixLen: Short, b: String): Boolean {
        val pa = a.split('.').mapNotNull { it.toIntOrNull() }
        val pb = b.split('.').mapNotNull { it.toIntOrNull() }
        if (pa.size != 4 || pb.size != 4 || prefixLen < 0 || prefixLen > 32) return false
        val plen = prefixLen.toInt()
        val mask = if (plen == 0) 0L else (-1L shl (32 - plen)) and 0xFFFFFFFFL
        val va = (pa[0] shl 24) or (pa[1] shl 16) or (pa[2] shl 8) or pa[3]
        val vb = (pb[0] shl 24) or (pb[1] shl 16) or (pb[2] shl 8) or pb[3]
        return (va.toLong() and mask) == (vb.toLong() and mask)
    }

    fun failureMessage(host: String): String {
        val base = "无法解析 $host：DNS 与 NetBIOS 均无结果"
        return if (tailscaleActive())
            "$base。检测到 Tailscale：请确认其 MagicDNS 已开启，或直接使用 IP 地址"
        else
            "$base。请确认与服务器在同一局域网，或使用 IP 地址"
    }

    private fun listInterfaces(): List<NetworkInterface> {
        val out = ArrayList<NetworkInterface>()
        try {
            val e = NetworkInterface.getNetworkInterfaces()
            while (e.hasMoreElements()) out.add(e.nextElement())
        } catch (_: Exception) {
        }
        return out
    }
}

/**
 * NetBIOS Name Service (RFC 1002) broadcast query: "who owns <NAME>?".
 * Windows hosts on the LAN reply unicast with their IP. Android has no
 * built-in NetBIOS support, hence this minimal implementation.
 */
private object NetBios {

    fun resolve(name: String, timeoutMs: Int): String? =
        resolveAll(name, timeoutMs).firstOrNull()

    /** Returns every non-zero IPv4 the responder claims for [name]. */
    fun resolveAll(name: String, timeoutMs: Int): List<String> {
        if (name.isEmpty() || name.length > 15) return emptyList()
        val txn = Random.nextInt(0x10000)
        val query = buildQuery(name.uppercase(), txn)

        val targets = broadcastTargets()
        if (targets.isEmpty()) return emptyList()

        try {
            DatagramSocket().use { sock ->
                sock.broadcast = true
                sock.soTimeout = timeoutMs
                targets.forEach { t ->
                    try {
                        sock.send(DatagramPacket(query, query.size, t, 137))
                    } catch (_: Exception) {
                    }
                }
                val deadline = SystemClock.elapsedRealtime() + timeoutMs + 300
                val buf = ByteArray(1024)
                while (SystemClock.elapsedRealtime() < deadline) {
                    val pkt = DatagramPacket(buf, buf.size)
                    try {
                        sock.receive(pkt)
                    } catch (_: SocketTimeoutException) {
                        break
                    }
                    parseAnswer(buf, pkt.length, txn)?.let { return it }
                }
            }
        } catch (_: Exception) {
        }
        return emptyList()
    }

    private fun broadcastTargets(): List<InetAddress> {
        val targets = ArrayList<InetAddress>()
        try {
            listInterfaces().forEach { ni ->
                if (!ni.isUp || ni.isLoopback) return@forEach
                if (ni.name.startsWith("tailscale")) return@forEach
                ni.interfaceAddresses.forEach { ia ->
                    val bc = ia.broadcast ?: return@forEach
                    if (targets.none { it == bc }) targets.add(bc)
                }
            }
        } catch (_: Exception) {
        }
        try {
            val global = InetAddress.getByName("255.255.255.255")
            if (targets.none { it == global }) targets.add(global)
        } catch (_: Exception) {
        }
        return targets
    }

    /** Broadcasts NBSTAT (*) and extracts each responder's computer name. */
    fun discover(timeoutMs: Int): List<HostResolver.LanMachine> {
        val targets = broadcastTargets()
        if (targets.isEmpty()) return emptyList()

        val txn = Random.nextInt(0x10000)
        val query = buildNodeStatusQuery(txn)
        val machines = LinkedHashMap<String, HostResolver.LanMachine>()

        try {
            DatagramSocket().use { sock ->
                sock.broadcast = true
                sock.soTimeout = timeoutMs
                targets.forEach { target ->
                    try {
                        sock.send(DatagramPacket(query, query.size, target, 137))
                    } catch (_: Exception) {
                    }
                }

                val deadline = SystemClock.elapsedRealtime() + timeoutMs + 300
                val buf = ByteArray(2048)
                while (SystemClock.elapsedRealtime() < deadline) {
                    val packet = DatagramPacket(buf, buf.size)
                    try {
                        sock.receive(packet)
                    } catch (_: SocketTimeoutException) {
                        break
                    }

                    parseNodeStatus(buf, packet.length, txn).forEach { name ->
                        val ip = packet.address?.hostAddress.orEmpty()
                        if (ip.isNotEmpty()) {
                            machines[name.lowercase()] = HostResolver.LanMachine(name, ip)
                        }
                    }
                }
            }
        } catch (_: Exception) {
        }

        return machines.values.sortedBy { it.name.lowercase() }
    }

    /** NBSTAT query for the wildcard NetBIOS name '*'. */
    private fun buildNodeStatusQuery(txn: Int): ByteArray {
        val out = ByteArrayOutputStream(50)
        out.write(txn shr 8); out.write(txn and 0xFF)
        out.write(0); out.write(0) // flags
        out.write(0); out.write(1) // QDCOUNT
        out.write(0); out.write(0) // ANCOUNT
        out.write(0); out.write(0) // NSCOUNT
        out.write(0); out.write(0) // ARCOUNT
        out.write(0x20)
        for (i in 0 until 16) {
            val b = if (i == 0) '*'.code else ' '.code
            out.write((b shr 4) + 'A'.code)
            out.write((b and 0x0F) + 'A'.code)
        }
        out.write(0) // empty scope
        out.write(0); out.write(0x21) // QTYPE = NBSTAT
        out.write(0); out.write(1) // QCLASS = IN
        return out.toByteArray()
    }

    private fun parseNodeStatus(d: ByteArray, len: Int, txn: Int): List<String> {
        if (len < 12 || u16(d, 0) != txn) return emptyList()
        val flags = u16(d, 2)
        if (flags and 0x8000 == 0 || flags and 0x000F != 0) return emptyList()

        val qd = u16(d, 4)
        val an = u16(d, 6)
        var off = 12
        repeat(qd) {
            off = skipName(d, off, len) ?: return emptyList()
            if (off + 4 > len) return emptyList()
            off += 4
        }

        val names = ArrayList<String>()
        repeat(an) {
            off = skipName(d, off, len) ?: return names
            if (off + 10 > len) return names
            val type = u16(d, off)
            val rdlen = u16(d, off + 8)
            val recordStart = off + 10
            val recordEnd = recordStart + rdlen
            if (recordEnd > len) return names
            if (type == 0x0021 && rdlen > 0) {
                val count = d[recordStart].toInt() and 0xFF
                var p = recordStart + 1
                repeat(count) {
                    if (p + 18 > recordEnd) return@repeat
                    val name = String(d, p, 15, Charsets.US_ASCII).trim()
                    val suffix = d[p + 15].toInt() and 0xFF
                    val nameFlags = u16(d, p + 16)
                    if (suffix == 0 && nameFlags and 0x8000 == 0 && isUsableMachineName(name)) {
                        names.add(name)
                    }
                    p += 18
                }
            }
            off = recordEnd
        }
        return names
    }

    private fun isUsableMachineName(name: String): Boolean {
        val value = name.trim()
        return value.length in 1..63 &&
                value.all { it.isLetterOrDigit() || it == '-' || it == '_' || it == '.' }
    }

    /** NB name query packet: header + encoded name + QTYPE=NB(0x20)/IN. */
    private fun buildQuery(name: String, txn: Int): ByteArray {
        val padded = name.take(15).padEnd(15, ' ')
        val out = ByteArrayOutputStream(50)
        out.write(txn shr 8); out.write(txn and 0xFF)
        out.write(0x01); out.write(0x00)   // flags: recursion desired
        out.write(0); out.write(1)         // QDCOUNT = 1
        out.write(0); out.write(0)         // ANCOUNT
        out.write(0); out.write(0)         // NSCOUNT
        out.write(0); out.write(0)         // ARCOUNT
        out.write(0x20)                    // encoded-name length
        for (i in 0 until 15) {
            val b = padded[i].code and 0xFF
            out.write((b shr 4) + 'A'.code)
            out.write((b and 0x0F) + 'A'.code)
        }
        out.write('A'.code); out.write('A'.code) // suffix byte 0x00
        out.write(0)                       // empty scope
        out.write(0); out.write(0x20)      // QTYPE = NB
        out.write(0); out.write(1)         // QCLASS = IN
        return out.toByteArray()
    }

    /** All non-zero IPv4 addresses from the first positive NB record. */
    private fun parseAnswer(d: ByteArray, len: Int, txn: Int): List<String>? {
        if (len < 12) return null
        val id = u16(d, 0)
        if (id != txn) return null
        val flags = u16(d, 2)
        if (flags and 0x8000 == 0) return null   // not a response
        if (flags and 0x000F != 0) return null   // negative / error
        val qd = u16(d, 4)
        val an = u16(d, 6)
        if (an == 0) return null

        var off = 12
        repeat(qd) {
            off = skipName(d, off, len) ?: return null
            off += 4 // QTYPE + QCLASS
            if (off > len) return null
        }
        off = skipName(d, off, len) ?: return null
        if (off + 10 > len) return null
        if (u16(d, off) != 0x0020) return null   // not an NB record
        val rdlen = u16(d, off + 8)
        var p = off + 10
        if (p + rdlen > len) return null
        // RDATA: repeat of (2-byte flags + 4-byte IPv4)
        val ips = ArrayList<String>()
        while (p + 6 <= off + 10 + rdlen) {
            val ip = "${d[p + 2].toInt() and 0xFF}.${d[p + 3].toInt() and 0xFF}." +
                    "${d[p + 4].toInt() and 0xFF}.${d[p + 5].toInt() and 0xFF}"
            if (ip != "0.0.0.0" && ip !in ips) ips.add(ip)
            p += 6
        }
        return if (ips.isEmpty()) null else ips
    }

    /** Skips a name field: compression pointer, NB-encoded (len 0x20), or labels. */
    private fun skipName(d: ByteArray, off: Int, len: Int): Int? {
        var p = off
        if (p >= len) return null
        val first = d[p].toInt() and 0xFF
        if (first and 0xC0 == 0xC0) return if (p + 2 <= len) p + 2 else null
        if (first == 0x20) {
            p += 1 + 32
            if (p > len) return null
            return skipScope(d, p, len)
        }
        while (p < len) {
            val l = d[p].toInt() and 0xFF
            p++
            if (l == 0) return p
            if (l and 0xC0 == 0xC0) return if (p + 1 <= len) p + 1 else null
            p += l
            if (p > len) return null
        }
        return null
    }

    private fun skipScope(d: ByteArray, p: Int, len: Int): Int? {
        var q = p
        while (q < len) {
            val l = d[q].toInt() and 0xFF
            q++
            if (l == 0) return q
            q += l
            if (q > len) return null
        }
        return null
    }

    private fun u16(d: ByteArray, off: Int): Int =
        ((d[off].toInt() and 0xFF) shl 8) or (d[off + 1].toInt() and 0xFF)

    private fun listInterfaces(): List<NetworkInterface> {
        val out = ArrayList<NetworkInterface>()
        try {
            val e = NetworkInterface.getNetworkInterfaces()
            while (e.hasMoreElements()) out.add(e.nextElement())
        } catch (_: Exception) {
        }
        return out
    }
}

/**
 * JSch SocketFactory that resolves bare host names itself (DNS + NetBIOS)
 * instead of letting JSch rely on the OS resolver. The session host string
 * stays the original name, so TOFU host-key pinning is stable across
 * LAN-IP / Tailscale-IP switches.
 */
class ResolvingSocketFactory(
    private val onResolved: (HostResolver.Resolution) -> Unit
) : SocketFactory {

    override fun createSocket(host: String, port: Int): Socket {
        android.util.Log.d("HarnessTunnel", "factory: resolving '$host'")
        val r = HostResolver.resolve(host) ?: throw UnknownHostException(
            HostResolver.failureMessage(host)
        )
        android.util.Log.d("HarnessTunnel", "factory: connecting ${r.ip}:$port (${r.source})")
        val s = Socket()
        try {
            s.tcpNoDelay = true
            s.connect(InetSocketAddress(r.ip, port), 15_000)
        } catch (e: Exception) {
            android.util.Log.w("HarnessTunnel", "factory: connect to ${r.ip}:$port failed: $e")
            HostResolver.invalidate(host)
            try { s.close() } catch (_: Exception) {}
            throw e
        }
        android.util.Log.d("HarnessTunnel", "factory: tcp connected ${r.ip}:$port")
        onResolved(r)
        return s
    }

    override fun getInputStream(socket: Socket?): InputStream = socket!!.getInputStream()

    override fun getOutputStream(socket: Socket?): OutputStream = socket!!.getOutputStream()
}
