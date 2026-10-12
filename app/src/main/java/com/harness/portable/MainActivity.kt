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
import androidx.activity.compose.BackHandler
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.toMutableStateList
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.key
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
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class MainActivity : ComponentActivity() {
    private var immersive = false
    private var darkAppearance = false

    fun setUiPresentation(fullscreen: Boolean, dark: Boolean) {
        immersive = fullscreen
        darkAppearance = dark
        applyImmersive()
    }
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

    override fun onStart() {
        super.onStart()
        AppVisibility.setVisible(true)
        // Process death / OEM kills drop the in-memory SSH session but leave
        // PREF_ACTIVE set. Bring the tunnel back as soon as the user returns.
        SshTunnelService.resumeIfNeeded(this)
    }

    override fun onStop() {
        AppVisibility.setVisible(false)
        super.onStop()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) applyImmersive()
    }

    private fun applyImmersive() {
        WindowCompat.setDecorFitsSystemWindows(window, false)
        WindowInsetsControllerCompat(window, window.decorView).let { c ->
            if (immersive) c.hide(WindowInsetsCompat.Type.systemBars()) else c.show(WindowInsetsCompat.Type.systemBars())
            c.isAppearanceLightStatusBars = !darkAppearance
            c.isAppearanceLightNavigationBars = !darkAppearance
            c.systemBarsBehavior =
                WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }
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
    // A foreground tunnel outlives the Activity. Restore the WebView only
    // when the session is already up; otherwise park on the connecting
    // screen and let [SshTunnelService.resumeIfNeeded] rebuild it.
    val restoredProfile = remember {
        SshTunnelService.activeProfileId(ctx)?.let { activeId ->
            tunnels.firstOrNull { it.id == activeId }
        }
    }
    val restoredConnected = restoredProfile != null &&
        tunnelInfo.profileId == restoredProfile.id &&
        tunnelInfo.status == TunnelState.Status.CONNECTED
    var target by remember {
        mutableStateOf<ActiveTarget?>(
            restoredProfile
                ?.takeIf { restoredConnected }
                ?.let { ActiveTarget.Tunnel(it.id, it.name) }
        )
    }
    var pending by remember {
        mutableStateOf(restoredProfile?.takeIf { !restoredConnected })
    }
    var passwordFor by remember { mutableStateOf<TunnelProfile?>(null) }
    var screen by rememberSaveable { mutableStateOf(if (restoredProfile != null) "browser" else "connections") }
    var settingsReturn by rememberSaveable { mutableStateOf("connections") }
    var switchingTo by remember { mutableStateOf<ActiveTarget?>(null) }
    val uiPreferences = rememberUiPreferences()
    val darkAppearance = LocalDsh.current.isDark
    LaunchedEffect(screen, target, pending?.id, uiPreferences.browserFullscreen, darkAppearance) {
        (activity as? MainActivity)?.setUiPresentation(screen == "browser" && target != null && pending == null && uiPreferences.browserFullscreen, darkAppearance)
    }
    BackHandler(enabled = screen == "settings") { screen = settingsReturn }
    var batteryPrompt by remember {
        mutableStateOf(
            restoredProfile != null && BatteryExemption.shouldPrompt(ctx)
        )
    }

    // The tunnel runs in a foreground service; ask for the notification
    // permission once so its status is visible.
    val notifLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { }

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

    fun connectTunnel(p: TunnelProfile, forceRestart: Boolean = false) {
        if (!SecureStore.hasPassword(ctx, p.id) && p.identityFile.isBlank()) {
            passwordFor = p
            return
        }
        if (!forceRestart &&
            tunnelInfo.profileId == p.id &&
            tunnelInfo.status == TunnelState.Status.CONNECTED
        ) {
            pending = null
            target = ActiveTarget.Tunnel(p.id, p.name)
            screen = "browser"
            return
        }
        if (Build.VERSION.SDK_INT >= 33 && !prefs.getBoolean("notification_permission_asked", false)) {
            prefs.edit().putBoolean("notification_permission_asked", true).apply()
            if (androidx.core.content.ContextCompat.checkSelfPermission(ctx, Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED)
                notifLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
        if ((target as? ActiveTarget.Tunnel)?.profileId != p.id) target = null
        pending = p
        screen = "browser"
        if (BatteryExemption.shouldPrompt(ctx)) batteryPrompt = true
        SshTunnelService.start(ctx, p.id, restart = forceRestart)
    }

    // The editor's field wins on save: a value is stored, an emptied field
    // clears the stored one (the tunnel then falls back to the page dialog).
    fun applyAuthInput(profileId: String, authInput: String?) {
        if (authInput.isNullOrEmpty()) SecureStore.clearAuthInput(ctx, profileId)
        else SecureStore.setAuthInput(ctx, profileId, authInput)
    }

    fun disconnectCurrent() {
        SshTunnelService.stop(ctx)
        target = null
        pending = null
        screen = "connections"
    }

    fun openTarget(next: ActiveTarget, forceRestart: Boolean = false) {
        when (next) {
            is ActiveTarget.Tunnel -> tunnels.firstOrNull { it.id == next.profileId }?.let { connectTunnel(it, forceRestart) }
            is ActiveTarget.Direct -> {
                SshTunnelService.stop(ctx)
                pending = null
                target = next
                screen = "browser"
            }
        }
    }

    fun requestTarget(next: ActiveTarget, forceRestart: Boolean = false) {
        val active = target ?: pending?.let { ActiveTarget.Tunnel(it.id, it.name) }
        val same = when {
            active is ActiveTarget.Tunnel && next is ActiveTarget.Tunnel -> active.profileId == next.profileId
            active is ActiveTarget.Direct && next is ActiveTarget.Direct -> active.url == next.url
            else -> active == null
        }
        if (!same) switchingTo = next else openTarget(next, forceRestart)
    }

    fun openSettings() { settingsReturn = screen; screen = "settings" }
    val current = target
    val activeLabel = when (current) {
        is ActiveTarget.Tunnel -> tunnels.firstOrNull { it.id == current.profileId }?.name ?: current.name
        is ActiveTarget.Direct -> hostOf(current.url)
        else -> pending?.name
    }
    val activeStatus = if (current is ActiveTarget.Direct) "直连网页 · 已打开" else when (tunnelInfo.status) {
        TunnelState.Status.CONNECTED -> "SSH · 已连接"
        TunnelState.Status.CONNECTING -> "SSH · 连接中…"
        TunnelState.Status.RETRYING -> "SSH · 重连中"
        TunnelState.Status.FAILED -> "SSH · 连接失败"
        else -> "SSH · 未连接"
    }

    Box(Modifier.fillMaxSize()) {
        if (current != null) key(when (current) {
            is ActiveTarget.Tunnel -> "tunnel:" + current.profileId
            is ActiveTarget.Direct -> "direct:" + current.url
        }) {
            val tunnel = current as? ActiveTarget.Tunnel
            val fallbackPort = tunnels.firstOrNull { it.id == tunnel?.profileId }?.localPort ?: 3080
            val port = if (tunnelInfo.profileId == tunnel?.profileId && tunnelInfo.localPort > 0) tunnelInfo.localPort else fallbackPort
            val authUrl = tunnelInfo.authUrl.takeIf { tunnelInfo.profileId == tunnel?.profileId && tunnelInfo.status == TunnelState.Status.CONNECTED }
            val url = if (current is ActiveTarget.Direct) current.url else NssmAuthUrl.rewriteToLocal(authUrl, port, 0) ?: "http://127.0.0.1:$port"
            WebViewScreen(url = url, orientation = orientation, tunnelMode = tunnel != null,
                tunnelInfo = if (tunnel == null) null else if (tunnelInfo.profileId == tunnel.profileId) tunnelInfo else TunnelState.Info(profileId = tunnel.profileId, status = TunnelState.Status.STOPPED),
                title = activeLabel.orEmpty(), visible = screen == "browser" && pending == null,
                fullScreen = uiPreferences.browserFullscreen,
                onToggleOrientation = { orientation = orientation.next() },
                onChangeServer = { screen = "connections" },
                onOpenSettings = { openSettings() }, onDisconnect = { disconnectCurrent() },
                onReconnect = { tunnel?.let { t -> tunnels.firstOrNull { it.id == t.profileId }?.let { connectTunnel(it, forceRestart = true) } } })
        }

        SettingsScreen(tunnels = tunnels, directs = directs,
            runningTunnelId = tunnelInfo.profileId.takeIf { tunnelInfo.status == TunnelState.Status.CONNECTED },
            visible = screen != "browser", settingsOnly = screen == "settings",
            activeLabel = activeLabel, activeStatus = activeStatus,
            onResumeCurrent = { screen = "browser" }, onDisconnectCurrent = { disconnectCurrent() },
            onOpenSettings = { openSettings() }, onBackSettings = { screen = settingsReturn }, onManageConnections = { screen = "connections" },
            onConnectTunnel = { requestTarget(ActiveTarget.Tunnel(it.id, it.name)) },
            onSaveAndConnectTunnel = { requestTarget(ActiveTarget.Tunnel(it.id, it.name), forceRestart = true) },
            onAddTunnel = { profile, password, authInput ->
                tunnels.removeAll { it.id == profile.id }; tunnels.add(0, profile); ProfileStore.saveTunnels(ctx, tunnels)
                password?.let { SecureStore.setPassword(ctx, profile.id, it) }; applyAuthInput(profile.id, authInput)
            },
            onUpdateTunnel = { profile, password, authInput ->
                val index = tunnels.indexOfFirst { it.id == profile.id }; if (index >= 0) tunnels[index] = profile
                ProfileStore.saveTunnels(ctx, tunnels); password?.let { SecureStore.setPassword(ctx, profile.id, it) }; applyAuthInput(profile.id, authInput)
            },
            onDeleteTunnel = { profile ->
                tunnels.removeAll { it.id == profile.id }; ProfileStore.saveTunnels(ctx, tunnels)
                SecureStore.clearPassword(ctx, profile.id); SecureStore.clearAuthInput(ctx, profile.id); SshIdentityFiles.deleteManaged(ctx, profile.id, profile.identityFile)
                if (tunnelInfo.profileId == profile.id || (target as? ActiveTarget.Tunnel)?.profileId == profile.id || pending?.id == profile.id) disconnectCurrent()
            },
            onConnectDirect = { requestTarget(ActiveTarget.Direct(it)) },
            onAddDirect = { url -> directs.remove(url); directs.add(0, url); ProfileStore.saveDirect(ctx, directs) },
            onUpdateDirect = { old, new ->
                val index = directs.indexOf(old); directs.remove(new)
                if (old != new) directs.remove(old)
                directs.add(index.coerceIn(0, directs.size), new); ProfileStore.saveDirect(ctx, directs)
            },
            onDeleteDirect = { url -> directs.remove(url); ProfileStore.saveDirect(ctx, directs); if ((target as? ActiveTarget.Direct)?.url == url) disconnectCurrent() })

        if (pending != null && screen == "browser") {
            val profile = pending!!
            Surface(Modifier.fillMaxSize(), color = LocalDsh.current.base) {
                Column(Modifier.fillMaxSize()) {
                    TextButton(onClick = { screen = "connections" }) { Text("返回连接列表（保持连接）") }
                    Box(Modifier.weight(1f)) {
                        ConnectingScreen(info = if (tunnelInfo.profileId == profile.id) tunnelInfo else TunnelState.Info(profileId = profile.id, profileName = profile.name, status = TunnelState.Status.CONNECTING),
                            profile = profile, onCancel = { disconnectCurrent() },
                            onRetry = { connectTunnel(profile, forceRestart = true) }, onChangePassword = { passwordFor = profile })
                    }
                }
            }
        }
    }

    switchingTo?.let { next ->
        val nextName = when (next) { is ActiveTarget.Tunnel -> next.name; is ActiveTarget.Direct -> hostOf(next.url) }
        AlertDialog(onDismissRequest = { switchingTo = null }, title = { Text("切换到 $nextName？") },
            text = { Text("将断开当前服务，然后连接 $nextName。确认前保持当前连接。") },
            confirmButton = { TextButton(onClick = { switchingTo = null; openTarget(next) }) { Text("断开并切换") } },
            dismissButton = { TextButton(onClick = { switchingTo = null }) { Text("取消") } })
    }

    if (batteryPrompt) {
        AlertDialog(
            onDismissRequest = {
                BatteryExemption.markPrompted(ctx)
                batteryPrompt = false
            },
            title = { Text("保持 SSH 隧道在后台运行") },
            text = {
                Text(
                    "系统省电策略会在切到后台后冻结或杀掉隧道，所以每次重新打开都会显示「隧道未连接」。" +
                        "请允许本应用忽略电池优化，通知栏里的隧道服务才能真正保活。",
                    fontSize = 13.sp
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    batteryPrompt = false
                    BatteryExemption.request(ctx)
                }) { Text("去允许") }
            },
            dismissButton = {
                TextButton(onClick = {
                    BatteryExemption.markPrompted(ctx)
                    batteryPrompt = false
                }) { Text("稍后") }
            },
            shape = DshCardShape,
            containerColor = MaterialTheme.colorScheme.surfaceContainerLow,
            titleContentColor = MaterialTheme.colorScheme.onSurface,
            textContentColor = MaterialTheme.colorScheme.onSurfaceVariant
        )
    }

    passwordFor?.let { p ->
        PasswordDialog(
            profile = p,
            onDismiss = { passwordFor = null },
            onSave = { pass ->
                SecureStore.setPassword(ctx, p.id, pass)
                passwordFor = null
                connectTunnel(p, forceRestart = true)
            }
        )
    }
}

/** Shown while a direct URL's machine name is being resolved to an IP. */
@Composable
internal fun DirectResolvingScreen(host: String) {
    val dsh = LocalDsh.current
    Box(
        modifier = Modifier.fillMaxSize().dshPage(),
        contentAlignment = Alignment.Center
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            CircularProgressIndicator(
                color = dsh.accent,
                trackColor = dsh.layer3,
                modifier = Modifier.size(28.dp)
            )
            Text("正在解析 $host…", fontSize = 14.sp, color = dsh.textSecondary)
        }
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
    val dsh = LocalDsh.current
    Box(
        modifier = Modifier.fillMaxSize().dshPage(),
        contentAlignment = Alignment.Center
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(12.dp),
            modifier = Modifier
                .padding(24.dp)
                .widthIn(max = 420.dp)
                .dshCard()
                .padding(20.dp)
        ) {
            when (info.status) {
                TunnelState.Status.FAILED -> {
                    Icon(
                        Icons.Filled.ErrorOutline,
                        contentDescription = null,
                        tint = dsh.danger,
                        modifier = Modifier.size(40.dp)
                    )
                    Text(
                        "连接失败",
                        fontWeight = FontWeight.SemiBold,
                        fontSize = 16.sp,
                        color = dsh.textPrimary
                    )
                }

                TunnelState.Status.STOPPED -> {
                    Text(
                        "隧道已停止",
                        fontWeight = FontWeight.SemiBold,
                        fontSize = 16.sp,
                        color = dsh.textPrimary
                    )
                }

                else -> {
                    CircularProgressIndicator(
                        color = dsh.accent,
                        trackColor = dsh.layer3,
                        modifier = Modifier.size(28.dp)
                    )
                    Text(
                        if (info.status == TunnelState.Status.RETRYING)
                            "连接失败，正在重试…"
                        else "正在连接 ${profile.name}…",
                        fontWeight = FontWeight.SemiBold,
                        fontSize = 16.sp,
                        color = dsh.textPrimary
                    )
                }
            }
            info.message?.let {
                Text(
                    it,
                    fontSize = 12.sp,
                    fontFamily = DshMonoFontFamily,
                    color = dsh.textTertiary,
                    textAlign = TextAlign.Center
                )
            }
            when (info.status) {
                TunnelState.Status.FAILED -> Row(
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedButton(
                        onClick = onChangePassword,
                        shape = DshControlShape,
                        colors = ButtonDefaults.outlinedButtonColors(contentColor = dsh.accent)
                    ) { Text("修改密码") }
                    Button(
                        onClick = onRetry,
                        shape = DshControlShape,
                        colors = ButtonDefaults.buttonColors(
                            containerColor = dsh.brandFill,
                            contentColor = dsh.brandText
                        )
                    ) { Text("重试") }
                }

                TunnelState.Status.STOPPED -> Button(
                    onClick = onCancel,
                    shape = DshControlShape,
                    colors = ButtonDefaults.buttonColors(
                        containerColor = dsh.brandFill,
                        contentColor = dsh.brandText
                    )
                ) { Text("返回") }

                else -> OutlinedButton(
                    onClick = onCancel,
                    shape = DshControlShape,
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = dsh.accent)
                ) { Text("取消") }
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
                    fontSize = 13.sp,
                    fontFamily = DshMonoFontFamily,
                    color = MaterialTheme.colorScheme.onSurface
                )
                Text(
                    "密码将使用 Android Keystore 加密后存储在本机，仅用于建立隧道。",
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                OutlinedTextField(
                    value = pass,
                    onValueChange = { pass = it },
                    label = { Text("密码") },
                    singleLine = true,
                    shape = DshControlShape,
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
        },
        shape = DshCardShape,
        containerColor = MaterialTheme.colorScheme.surfaceContainerLow,
        titleContentColor = MaterialTheme.colorScheme.onSurface,
        textContentColor = MaterialTheme.colorScheme.onSurfaceVariant
    )
}
