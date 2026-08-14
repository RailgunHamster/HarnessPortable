package com.harness.portable

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

/**
 * A tunnel profile is the equivalent of
 *   ssh -N -L localPort:remoteHost:remotePort user@sshHost:sshPort
 */
data class TunnelProfile(
    val id: String = UUID.randomUUID().toString(),
    val name: String,
    val sshHost: String,
    val sshPort: Int = 22,
    val user: String,
    val remoteHost: String = "127.0.0.1",
    val remotePort: Int = 3080,
    val localPort: Int = 3080
)

object ProfileStore {

    private const val PREFS = "harness_portable"
    private const val KEY_TUNNELS = "tunnels"
    private const val KEY_DIRECT = "direct_urls"
    private const val KEY_SEEDED = "seeded_v1"
    private const val OLD_PREFS = "opencode_web"
    private const val OLD_KEY_SERVERS = "servers"

    private fun prefs(ctx: Context) =
        ctx.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    fun loadTunnels(ctx: Context): MutableList<TunnelProfile> {
        migrateIfNeeded(ctx)
        val json = prefs(ctx).getString(KEY_TUNNELS, null) ?: return mutableListOf()
        return try {
            val arr = JSONArray(json)
            (0 until arr.length()).mapNotNull { i ->
                val o = arr.optJSONObject(i) ?: return@mapNotNull null
                if (o.optString("host").isBlank()) return@mapNotNull null
                TunnelProfile(
                    id = o.optString("id").ifBlank { UUID.randomUUID().toString() },
                    name = o.optString("name"),
                    sshHost = o.optString("host"),
                    sshPort = o.optInt("port", 22),
                    user = o.optString("user"),
                    remoteHost = o.optString("remote_host", "127.0.0.1"),
                    remotePort = o.optInt("remote_port", 3080),
                    localPort = o.optInt("local_port", 3080)
                )
            }.toMutableList()
        } catch (e: Exception) {
            mutableListOf()
        }
    }

    fun saveTunnels(ctx: Context, list: List<TunnelProfile>) {
        val arr = JSONArray()
        list.forEach { p ->
            arr.put(
                JSONObject()
                    .put("id", p.id)
                    .put("name", p.name)
                    .put("host", p.sshHost)
                    .put("port", p.sshPort)
                    .put("user", p.user)
                    .put("remote_host", p.remoteHost)
                    .put("remote_port", p.remotePort)
                    .put("local_port", p.localPort)
            )
        }
        prefs(ctx).edit().putString(KEY_TUNNELS, arr.toString()).apply()
    }

    fun loadDirect(ctx: Context): MutableList<String> {
        migrateIfNeeded(ctx)
        val json = prefs(ctx).getString(KEY_DIRECT, null) ?: return mutableListOf()
        return try {
            val arr = JSONArray(json)
            (0 until arr.length()).mapNotNull { i ->
                arr.optString(i).takeIf { it.isNotBlank() }
            }.toMutableList()
        } catch (e: Exception) {
            mutableListOf()
        }
    }

    fun saveDirect(ctx: Context, list: List<String>) {
        val arr = JSONArray()
        list.forEach { arr.put(it) }
        prefs(ctx).edit().putString(KEY_DIRECT, arr.toString()).apply()
    }

    fun findTunnel(ctx: Context, id: String?): TunnelProfile? =
        id?.let { wanted -> loadTunnels(ctx).firstOrNull { it.id == wanted } }

    /**
     * One-time migration from the old "OpenCode Web" app:
     * - imports its saved direct-URL server list
     * - seeds one tunnel profile matching the typical winserver setup
     */
    private fun migrateIfNeeded(ctx: Context) {
        val p = prefs(ctx)
        if (p.getBoolean(KEY_SEEDED, false)) return

        if (p.getString(KEY_DIRECT, null) == null) {
            val imported = mutableListOf<String>()
            try {
                val old = ctx.applicationContext
                    .getSharedPreferences(OLD_PREFS, Context.MODE_PRIVATE)
                val oldJson = old.getString(OLD_KEY_SERVERS, null)
                if (oldJson != null) {
                    val arr = JSONArray(oldJson)
                    (0 until arr.length()).forEach { i ->
                        arr.optString(i).takeIf { it.isNotBlank() }?.let { imported.add(it) }
                    }
                }
            } catch (_: Exception) {
            }
            if (imported.isNotEmpty()) {
                p.edit().putString(KEY_DIRECT, JSONArray(imported).toString()).apply()
            }
        }

        if (p.getString(KEY_TUNNELS, null) == null) {
            saveTunnels(
                ctx,
                listOf(TunnelProfile(name = "winserver", sshHost = "winserver", user = "administrator"))
            )
        }

        p.edit().putBoolean(KEY_SEEDED, true).apply()
    }
}
