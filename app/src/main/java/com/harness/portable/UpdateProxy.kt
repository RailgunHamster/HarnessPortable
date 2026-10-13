package com.harness.portable

import android.content.Context
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.URI

/** Transport policy is scoped to update requests, never JVM or WebView preferences. */
data class UpdateProxy(val enabled: Boolean = false, val address: String = DEFAULT_ADDRESS) {
    fun connectionProxy(): Proxy {
        if (!enabled) return Proxy.NO_PROXY
        val endpoint = parseAddress(address)
        return Proxy(Proxy.Type.HTTP, InetSocketAddress(endpoint.host.removeSurrounding("[", "]"), endpoint.port))
    }

    companion object {
        const val DEFAULT_ADDRESS = "localhost:7890"
        fun parseAddress(address: String): URI {
            val value = address.trim().let { if (it.contains("://")) it else "http://$it" }
            val uri = try { URI(value) } catch (_: Exception) { null }
            require(uri != null && uri.scheme.equals("http", true) && !uri.host.isNullOrEmpty() &&
                uri.port in 1..65535 && uri.rawUserInfo == null && uri.rawQuery == null && uri.rawFragment == null &&
                uri.rawPath.orEmpty() in listOf("", "/")) { "请输入 HTTP 代理地址，例如 localhost:7890。" }
            return uri
        }
    }
}

object UpdateProxyStore {
    fun load(ctx: Context): UpdateProxy {
        val prefs = ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE)
        return UpdateProxy(prefs.getBoolean("update_proxy_enabled", false),
            prefs.getString("update_proxy_address", UpdateProxy.DEFAULT_ADDRESS)?.trim().orEmpty().ifEmpty { UpdateProxy.DEFAULT_ADDRESS })
    }
    fun save(ctx: Context, proxy: UpdateProxy) {
        ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE).edit()
            .putBoolean("update_proxy_enabled", proxy.enabled).putString("update_proxy_address", proxy.address.trim()).apply()
    }
}
