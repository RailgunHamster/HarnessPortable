package com.opencode.web

import android.app.Activity
import android.content.Context
import android.content.pm.ActivityInfo
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.toMutableStateList
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import org.json.JSONArray

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        applyImmersive()
        setContent {
            AppTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    AppRoot()
                }
            }
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) applyImmersive()
    }

    private fun applyImmersive() {
        WindowCompat.setDecorFitsSystemWindows(window, false)
        WindowInsetsControllerCompat(window, window.decorView).let { c ->
            c.hide(WindowInsetsCompat.Type.systemBars())
            c.systemBarsBehavior =
                WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }
}

@Composable
private fun AppTheme(content: @Composable () -> Unit) {
    val ctx = LocalContext.current
    val dark = isSystemInDarkTheme()
    val scheme = when {
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.S ->
            if (dark) dynamicDarkColorScheme(ctx) else dynamicLightColorScheme(ctx)
        dark -> darkColorScheme()
        else -> lightColorScheme()
    }
    MaterialTheme(colorScheme = scheme, content = content)
}

private const val PREFS_NAME = "opencode_web"
private const val KEY_URL = "current_url"
private const val KEY_SERVERS = "servers"
private const val KEY_ORIENTATION = "orientation"

internal enum class OrientationMode(val storage: String) {
    PORTRAIT("portrait"),
    LANDSCAPE("landscape"),
    AUTO("auto");

    companion object {
        fun fromStorage(s: String?): OrientationMode =
            entries.firstOrNull { it.storage == s } ?: AUTO
    }
}

internal fun OrientationMode.toActivityInfo(): Int = when (this) {
    OrientationMode.PORTRAIT  -> ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
    OrientationMode.LANDSCAPE -> ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE
    OrientationMode.AUTO      -> ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
}

internal fun OrientationMode.next(): OrientationMode = when (this) {
    OrientationMode.PORTRAIT  -> OrientationMode.LANDSCAPE
    OrientationMode.LANDSCAPE -> OrientationMode.AUTO
    OrientationMode.AUTO      -> OrientationMode.PORTRAIT
}
// First-launch seed values (user can delete freely afterwards)
private val DEFAULT_SEED_URLS = listOf(
    "http://192.168.0.104:4096",
    "http://100.73.139.109:4096"
)

@Composable
fun AppRoot() {
    val ctx = LocalContext.current
    val activity = ctx as? Activity
    val prefs = remember { ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE) }
    val servers = remember { loadServers(prefs).toMutableStateList() }
    var url by remember { mutableStateOf(prefs.getString(KEY_URL, null)) }
    var orientation by remember {
        mutableStateOf(OrientationMode.fromStorage(prefs.getString(KEY_ORIENTATION, null)))
    }

    LaunchedEffect(orientation) {
        activity?.requestedOrientation = orientation.toActivityInfo()
        prefs.edit().putString(KEY_ORIENTATION, orientation.storage).apply()
    }

    val current = url
    if (current == null) {
        SettingsScreen(
            savedServers = servers,
            onConnect = { chosen ->
                prefs.edit().putString(KEY_URL, chosen).apply()
                servers.remove(chosen)
                servers.add(0, chosen)
                saveServers(prefs, servers)
                url = chosen
            },
            onAddServer = { newUrl ->
                servers.remove(newUrl)
                servers.add(0, newUrl)
                saveServers(prefs, servers)
                prefs.edit().putString(KEY_URL, newUrl).apply()
                url = newUrl
            },
            onDeleteServer = { del ->
                servers.remove(del)
                saveServers(prefs, servers)
            }
        )
    } else {
        WebViewScreen(
            url = current,
            orientation = orientation,
            onToggleOrientation = { orientation = orientation.next() },
            onChangeServer = {
                prefs.edit().remove(KEY_URL).apply()
                url = null
            }
        )
    }
}

private fun loadServers(prefs: android.content.SharedPreferences): List<String> {
    val json = prefs.getString(KEY_SERVERS, null)
    if (json == null) {
        saveServers(prefs, DEFAULT_SEED_URLS)
        return DEFAULT_SEED_URLS
    }
    return try {
        val arr = JSONArray(json)
        (0 until arr.length()).map { arr.getString(it) }
    } catch (e: Exception) {
        DEFAULT_SEED_URLS
    }
}

private fun saveServers(prefs: android.content.SharedPreferences, list: List<String>) {
    val arr = JSONArray()
    list.forEach { arr.put(it) }
    prefs.edit().putString(KEY_SERVERS, arr.toString()).apply()
}
