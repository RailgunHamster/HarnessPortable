package com.harness.portable

import android.content.Context
import android.content.SharedPreferences
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext

internal data class UiPreferenceState(val appearance: String = "system", val browserFullscreen: Boolean = false)

internal object UiPreferences {
    const val APPEARANCE = "ui_appearance"
    const val FULLSCREEN = "browser_fullscreen"
    const val FAVORITES = "favorite_connections"
    fun prefs(ctx: Context): SharedPreferences = ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE)
    fun load(ctx: Context) = prefs(ctx).let { UiPreferenceState(it.getString(APPEARANCE, "system") ?: "system", it.getBoolean(FULLSCREEN, false)) }
    fun setAppearance(ctx: Context, value: String) { prefs(ctx).edit().putString(APPEARANCE, value).apply() }
    fun setFullscreen(ctx: Context, value: Boolean) { prefs(ctx).edit().putBoolean(FULLSCREEN, value).apply() }
}

@Composable
internal fun rememberUiPreferences(): UiPreferenceState {
    val ctx = LocalContext.current
    val prefs = remember(ctx) { UiPreferences.prefs(ctx) }
    var value by remember(ctx) { mutableStateOf(UiPreferences.load(ctx)) }
    DisposableEffect(prefs) {
        val listener = SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
            if (key == UiPreferences.APPEARANCE || key == UiPreferences.FULLSCREEN) value = UiPreferences.load(ctx)
        }
        prefs.registerOnSharedPreferenceChangeListener(listener)
        onDispose { prefs.unregisterOnSharedPreferenceChangeListener(listener) }
    }
    return value
}
