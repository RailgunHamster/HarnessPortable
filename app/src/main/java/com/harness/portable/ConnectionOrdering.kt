package com.harness.portable

import android.content.Context
import org.json.JSONArray

/** One order for SSH and direct entries, shared by the UI, API, and WebDAV snapshot. */
internal object ConnectionOrdering {
    const val PREFERENCE = "sync_connection_order"

    fun ordered(keys: List<String>, saved: List<String>): List<String> {
        val valid = keys.toSet()
        return (saved + keys).filter { it in valid }.distinct()
    }

    fun validate(keys: List<String>, proposed: List<String>) {
        require(proposed.size == keys.distinct().size && proposed.distinct().size == proposed.size && proposed.toSet() == keys.toSet()) {
            "链接库已改变，请重新打开排序；顺序必须包含每个连接且不能重复。"
        }
    }

    private fun keys(context: Context): List<String> =
        (ProfileStore.loadTunnels(context).map { "tunnel:" + it.id } + ProfileStore.loadDirect(context).map { "direct:$it" }).distinct()

    fun load(context: Context): List<String> {
        val prefs = UiPreferences.prefs(context)
        val favorites = prefs.getStringSet(UiPreferences.FAVORITES, emptySet()).orEmpty()
        val available = keys(context).sortedByDescending { it in favorites }
        val saved = runCatching {
            val array = JSONArray(prefs.getString(PREFERENCE, "[]"))
            (0 until array.length()).map { array.getString(it) }
        }.getOrDefault(emptyList())
        return ordered(available, saved)
    }

    fun save(context: Context, proposed: List<String>) {
        validate(keys(context), proposed)
        check(UiPreferences.prefs(context).edit().putString(PREFERENCE, JSONArray(proposed).toString()).commit()) { "连接顺序未保存，请重试。" }
    }
}
