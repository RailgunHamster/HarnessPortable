package com.harness.portable

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.pm.ActivityInfo
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.toMutableStateList
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat

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

private const val PREFS_NAME = "harness_portable"
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

/** What the WebView screen is currently showing. */
internal sealed interface ActiveTarget {
    data class Direct(val url: String) : ActiveTarget
    data class Tunnel(val profileId: String, val name: String) : ActiveTarget
}

@Composable
fun AppRoot() {
    val ctx = LocalContext.current
    val activity = ctx as? Activity
    val prefs = remember { ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE) }
    val tunnels = remember { ProfileStore.loadTunnels(ctx).toMutableStateList() }
    val directs = remember { ProfileStore.loadDirect(ctx).toMutableStateList() }
    var orientation by remember {
        mutableStateOf(OrientationMode.fromStorage(prefs.getString(KEY_ORIENTATION, null)))
    }
    val tunnelInfo by TunnelState.flow.collectAsState()
    var target by remember { mutableStateOf<ActiveTarget?>(null) }
    var pending by remember { mutableStateOf<TunnelProfile?>(null) }
    var passwordFor by remember { mutableStateOf<TunnelProfile?>(null) }

    // The tunnel runs in a foreground service; ask for the notification
    // permission once so its status is visible.
    val notifLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { }
    LaunchedEffect(Unit) {
        if (Build.VERSION.SDK_INT >= 33) {
            notifLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
    }

    LaunchedEffect(orientation) {
        activity?.requestedOrientation = orientation.toActivityInfo()
        prefs.edit().putString(KEY_ORIENTATION, orientation.storage).apply()
    }

    // Promote a pending connection to the WebView as soon as the tunnel is up.
    LaunchedEffect(tunnelInfo.profileId, tunnelInfo.status) {
        val p = pending ?: return@LaunchedEffect
        if (tunnelInfo.profileId == p.id && tunnelInfo.status == TunnelState.Status.CONNECTED) {
            target = ActiveTarget.Tunnel(p.id, p.name)
            pending = null
        }
    }

    fun connectTunnel(p: TunnelProfile) {
        if (!SecureStore.hasPassword(ctx, p.id)) {
            passwordFor = p
            return
        }
        if (tunnelInfo.profileId == p.id && tunnelInfo.status == TunnelState.Status.CONNECTED) {
            pending = null
            target = ActiveTarget.Tunnel(p.id, p.name)
            return
        }
        pending = p
        SshTunnelService.start(ctx, p.id)
    }

    val current = target
    when {
        current is ActiveTarget.Tunnel -> {
            val fallbackPort =
                tunnels.firstOrNull { it.id == current.profileId }?.localPort ?: 3080
            val port =
                if (tunnelInfo.profileId == current.profileId && tunnelInfo.localPort > 0)
                    tunnelInfo.localPort else fallbackPort
            WebViewScreen(
                url = "http://127.0.0.1:$port",
                orientation = orientation,
                tunnelMode = true,
                tunnelInfo = if (tunnelInfo.profileId == current.profileId) tunnelInfo
                else TunnelState.Info(profileId = current.profileId, status = TunnelState.Status.STOPPED),
                onToggleOrientation = { orientation = orientation.next() },
                onChangeServer = {
                    SshTunnelService.stop(ctx)
                    target = null
                },
                onReconnect = {
                    tunnels.firstOrNull { it.id == current.profileId }?.let { connectTunnel(it) }
                }
            )
        }

        current is ActiveTarget.Direct -> {
            WebViewScreen(
                url = current.url,
                orientation = orientation,
                tunnelMode = false,
                tunnelInfo = null,
                onToggleOrientation = { orientation = orientation.next() },
                onChangeServer = { target = null },
                onReconnect = { }
            )
        }

        pending != null -> {
            val p = pending!!
            ConnectingScreen(
                info = if (tunnelInfo.profileId == p.id) tunnelInfo
                else TunnelState.Info(
                    profileId = p.id,
                    profileName = p.name,
                    status = TunnelState.Status.CONNECTING
                ),
                profile = p,
                onCancel = {
                    SshTunnelService.stop(ctx)
                    pending = null
                },
                onRetry = { connectTunnel(p) },
                onChangePassword = { passwordFor = p }
            )
        }

        else -> SettingsScreen(
            tunnels = tunnels,
            directs = directs,
            runningTunnelId =
                tunnelInfo.profileId.takeIf { tunnelInfo.status == TunnelState.Status.CONNECTED },
            onConnectTunnel = { connectTunnel(it) },
            onAddTunnel = { profile, password ->
                tunnels.removeAll { it.id == profile.id }
                tunnels.add(0, profile)
                ProfileStore.saveTunnels(ctx, tunnels)
                password?.let { SecureStore.setPassword(ctx, profile.id, it) }
            },
            onUpdateTunnel = { profile, password ->
                val idx = tunnels.indexOfFirst { it.id == profile.id }
                if (idx >= 0) tunnels[idx] = profile
                ProfileStore.saveTunnels(ctx, tunnels)
                password?.let { SecureStore.setPassword(ctx, profile.id, it) }
            },
            onDeleteTunnel = { p ->
                tunnels.removeAll { it.id == p.id }
                ProfileStore.saveTunnels(ctx, tunnels)
                SecureStore.clearPassword(ctx, p.id)
                if (tunnelInfo.profileId == p.id) SshTunnelService.stop(ctx)
            },
            onConnectDirect = { target = ActiveTarget.Direct(it) },
            onAddDirect = { url ->
                directs.remove(url)
                directs.add(0, url)
                ProfileStore.saveDirect(ctx, directs)
            },
            onDeleteDirect = { url ->
                directs.remove(url)
                ProfileStore.saveDirect(ctx, directs)
            }
        )
    }

    passwordFor?.let { p ->
        PasswordDialog(
            profile = p,
            onDismiss = { passwordFor = null },
            onSave = { pass ->
                SecureStore.setPassword(ctx, p.id, pass)
                passwordFor = null
                connectTunnel(p)
            }
        )
    }
}

@Composable
internal fun ConnectingScreen(
    info: TunnelState.Info,
    profile: TunnelProfile,
    onCancel: () -> Unit,
    onRetry: () -> Unit,
    onChangePassword: () -> Unit
) {
    androidx.compose.foundation.layout.Box(
        modifier = Modifier.fillMaxSize(),
        contentAlignment = Alignment.Center
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(12.dp),
            modifier = Modifier.padding(24.dp).widthIn(max = 420.dp)
        ) {
            when (info.status) {
                TunnelState.Status.FAILED -> {
                    Icon(
                        Icons.Filled.ErrorOutline,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.error,
                        modifier = Modifier.size(48.dp)
                    )
                    Text("连接失败", fontWeight = FontWeight.SemiBold, fontSize = 16.sp)
                }

                TunnelState.Status.STOPPED -> {
                    Text("隧道已停止", fontWeight = FontWeight.SemiBold, fontSize = 16.sp)
                }

                else -> {
                    CircularProgressIndicator()
                    Text(
                        if (info.status == TunnelState.Status.RETRYING)
                            "连接失败，正在重试…"
                        else "正在连接 ${profile.name}…",
                        fontWeight = FontWeight.SemiBold,
                        fontSize = 16.sp
                    )
                }
            }
            info.message?.let {
                Text(
                    it,
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.outline,
                    textAlign = TextAlign.Center
                )
            }
            when (info.status) {
                TunnelState.Status.FAILED -> Row(
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedButton(onClick = onChangePassword) { Text("修改密码") }
                    Button(onClick = onRetry) { Text("重试") }
                }

                TunnelState.Status.STOPPED -> Button(onClick = onCancel) { Text("返回") }

                else -> OutlinedButton(onClick = onCancel) { Text("取消") }
            }
        }
    }
}

@Composable
internal fun PasswordDialog(
    profile: TunnelProfile,
    onDismiss: () -> Unit,
    onSave: (String) -> Unit
) {
    var pass by remember { mutableStateOf("") }
    var visible by remember { mutableStateOf(false) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("SSH 密码") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    "${profile.user}@${profile.sshHost}:${profile.sshPort}",
                    fontSize = 13.sp
                )
                Text(
                    "密码将使用 Android Keystore 加密后存储在本机，仅用于建立隧道。",
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.outline
                )
                OutlinedTextField(
                    value = pass,
                    onValueChange = { pass = it },
                    label = { Text("密码") },
                    singleLine = true,
                    visualTransformation =
                        if (visible) VisualTransformation.None else PasswordVisualTransformation(),
                    trailingIcon = {
                        TextButton(onClick = { visible = !visible }) {
                            Text(if (visible) "隐藏" else "显示")
                        }
                    }
                )
            }
        },
        confirmButton = {
            TextButton(
                enabled = pass.isNotEmpty(),
                onClick = { onSave(pass) }
            ) { Text("保存并连接") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("取消") }
        }
    )
}
