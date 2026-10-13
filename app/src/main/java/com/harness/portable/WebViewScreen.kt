package com.harness.portable

import android.annotation.SuppressLint
import android.content.Context
import android.net.Uri
import android.view.ViewGroup
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Autorenew
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Dns
import androidx.compose.material.icons.filled.LinkOff
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.ScreenRotation
import androidx.compose.material.icons.filled.StayCurrentLandscape
import androidx.compose.material.icons.filled.StayCurrentPortrait
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FloatingActionButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.SmallFloatingActionButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.IconButton
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Settings
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import kotlin.math.roundToInt

@SuppressLint("SetJavaScriptEnabled")
@Composable
internal fun WebViewScreen(
    url: String,
    orientation: OrientationMode,
    tunnelMode: Boolean,
    tunnelInfo: TunnelState.Info?,
    onToggleOrientation: () -> Unit,
    onChangeServer: () -> Unit,
    onReconnect: () -> Unit,
    title: String = "",
    visible: Boolean = true,
    fullScreen: Boolean = false,
    onOpenSettings: () -> Unit = {},
    onDisconnect: () -> Unit = onChangeServer
) {
    val ctx = LocalContext.current
    val localDensity = LocalDensity.current
    val dsh = LocalDsh.current
    val prefs = remember { ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE) }

    var webRef by remember { mutableStateOf<WebView?>(null) }
    var canGoBack by remember { mutableStateOf(false) }
    val fileChooser = remember { FileChooserHost() }
    val pickOneFile = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri -> fileChooser.complete(uri?.let { arrayOf(it) }) }
    val pickManyFiles = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenMultipleDocuments()
    ) { uris -> fileChooser.complete(if (uris.isEmpty()) null else uris.toTypedArray()) }
    fileChooser.open = { mimeTypes, multiple ->
        val types = mimeTypes
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .ifEmpty { listOf("*/*") }
            .toTypedArray()
        if (multiple) pickManyFiles.launch(types) else pickOneFile.launch(types)
    }
    DisposableEffect(Unit) {
        onDispose { fileChooser.complete(null) }
    }
    var controlsExpanded by remember { mutableStateOf(false) }
    var toolbarCollapsed by remember { mutableStateOf(prefs.getBoolean("browser_toolbar_collapsed", false)) }
    var dragging by remember { mutableStateOf(false) }

    fun setToolbarCollapsed(collapsed: Boolean) {
        toolbarCollapsed = collapsed
        prefs.edit().putBoolean("browser_toolbar_collapsed", collapsed).apply()
    }

    val ballSizeDp = 48.dp
    val marginDp = 10.dp
    val buttonCount = if (tunnelMode) 4 else 3
    val actionsHeightDp = (buttonCount * 48 + (buttonCount - 1) * 6).dp

    var ballX by remember { mutableStateOf(prefs.getFloat("ball_x_dp", Float.NaN)) }
    var ballY by remember { mutableStateOf(prefs.getFloat("ball_y_dp", Float.NaN)) }

    // Tunnel-awareness: reload when the URL changes (e.g. rebind on another
    // local port) and once the tunnel has come back after a drop.
    var loadedUrl by remember { mutableStateOf<String?>(null) }
    val tunnelUp = tunnelInfo == null || tunnelInfo.status == TunnelState.Status.CONNECTED
    var tunnelWasDown by remember { mutableStateOf(false) }
    LaunchedEffect(url, tunnelUp, visible) {
        if (!tunnelUp) { tunnelWasDown = true; return@LaunchedEffect }
        if (!visible) return@LaunchedEffect
        val browser = webRef ?: return@LaunchedEffect
        if (loadedUrl != url) { browser.loadUrl(url); loadedUrl = url; tunnelWasDown = false }
        else if (tunnelWasDown) { browser.reload(); tunnelWasDown = false }
    }
    DisposableEffect(visible) {
        if (visible) webRef?.onResume() else webRef?.onPause()
        onDispose { }
    }

    // dsh-web style services print a one-time token URL on the server; the
    // bare host:port answers 401 until that URL has been opened once. Detect
    // the rejection page and let the user paste the URL (the tunnel service
    // usually fetches it automatically in NSSM mode — this is the fallback).
    var showAuthPrompt by remember { mutableStateOf(false) }
    var authInput by remember { mutableStateOf("") }
    var authHint by remember { mutableStateOf<String?>(null) }

    fun openAuthInput(raw: String) {
        val input = raw.trim()
        val base = url.substringBefore('?').trimEnd('/')
        val target = when {
            input.startsWith("http://", true) || input.startsWith("https://", true) -> {
                val uri = runCatching { java.net.URI(input) }.getOrNull()
                val pathAndQuery = buildString {
                    append(uri?.rawPath?.takeIf { it.isNotEmpty() } ?: "/")
                    uri?.rawQuery?.let { append('?').append(it) }
                }
                if (pathAndQuery.contains('?')) base + pathAndQuery else null
            }
            input.startsWith("?") -> base + input
            input.contains('=') && !input.contains('/') && !input.contains(' ') ->
                "$base/?$input"
            // Bare token (dsh tokens are base64url: [A-Za-z0-9_-]).
            input.isNotEmpty() && !input.contains('/') && input.none { it.isWhitespace() } ->
                "$base/?token=${encodeQueryValue(input)}"
            else -> null
        }
        if (target == null) {
            authHint = "无法识别输入。请粘贴 dsh web 打印的完整 URL，或直接粘贴 token 本身。"
        } else {
            showAuthPrompt = false
            authHint = null
            webRef?.loadUrl(target)
        }
    }

    Box(modifier = Modifier.fillMaxSize()) {
        Column(Modifier.fillMaxSize().then(if (fullScreen) Modifier else Modifier.windowInsetsPadding(WindowInsets.safeDrawing))) {
        if (!fullScreen) Row(Modifier.fillMaxWidth().background(dsh.layer1).padding(horizontal = 8.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onChangeServer) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回连接列表，保持连接", tint = dsh.textPrimary) }
            Column(Modifier.weight(1f)) {
                Text(title, style = MaterialTheme.typography.titleSmall, maxLines = 1, color = dsh.textPrimary)
                Text(if (tunnelMode) "SSH" else "直连网页", style = MaterialTheme.typography.bodySmall, color = dsh.textSecondary)
            }
            IconButton(onClick = onOpenSettings) { Icon(Icons.Filled.Settings, "设置", tint = dsh.textPrimary) }
        }
        AndroidView(
            factory = { ctx ->
                WebView(ctx).apply {
                    if ((ctx.applicationInfo.flags and android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE) != 0) android.util.Log.d("HarnessUi", "WebView created")
                    layoutParams = ViewGroup.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.MATCH_PARENT
                    )
                    settings.javaScriptEnabled = true
                    settings.domStorageEnabled = true
                    settings.allowFileAccess = true
                    settings.allowContentAccess = true
                    settings.cacheMode = WebSettings.LOAD_DEFAULT
                    settings.useWideViewPort = true
                    settings.loadWithOverviewMode = true
                    webChromeClient = object : WebChromeClient() {
                        override fun onShowFileChooser(
                            webView: WebView?,
                            filePathCallback: ValueCallback<Array<Uri>>?,
                            fileChooserParams: FileChooserParams?
                        ): Boolean {
                            val types = fileChooserParams?.acceptTypes ?: emptyArray()
                            val multiple = fileChooserParams?.mode ==
                                FileChooserParams.MODE_OPEN_MULTIPLE
                            return fileChooser.show(filePathCallback, types, multiple)
                        }
                    }
                    webViewClient = object : WebViewClient() {
                        override fun shouldOverrideUrlLoading(
                            view: WebView,
                            request: WebResourceRequest
                        ): Boolean = false

                        override fun onPageFinished(view: WebView, url: String?) {
                            super.onPageFinished(view, url)
                            canGoBack = view.canGoBack()

                            val current = url ?: return
                            if (!current.startsWith("http")) return
                            view.evaluateJavascript(
                                "(document.body ? (document.body.innerText || '') : '').slice(0, 4000)"
                            ) { result ->
                                if (result == null || result == "null") return@evaluateJavascript
                                val text = runCatching {
                                    org.json.JSONObject("{\"v\":$result}").optString("v")
                                }.getOrDefault("")
                                if (text.isNotEmpty() && NssmAuthUrl.looksLikeAuthRequired(text)) {
                                    authInput = ""
                                    authHint = null
                                    showAuthPrompt = true
                                }
                            }
                        }
                    }
                    if (tunnelUp) loadUrl(url)
                }.also { webRef = it; if (tunnelUp) loadedUrl = url }
            },
            modifier = Modifier.weight(1f).fillMaxWidth().imePadding(),
            update = { browser -> browser.visibility = if (visible) android.view.View.VISIBLE else android.view.View.INVISIBLE },
            onReset = null,
            onRelease = { browser ->
                fileChooser.complete(null); webRef = null
                browser.stopLoading(); browser.webChromeClient = null; browser.destroy()
                if ((ctx.applicationInfo.flags and android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE) != 0) android.util.Log.d("HarnessUi", "WebView released")
            }
        )
        if (!fullScreen && !toolbarCollapsed) Row(Modifier.fillMaxWidth().background(dsh.layer1).padding(horizontal = 8.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { webRef?.reload() }) {
                Icon(Icons.Filled.Refresh, "刷新网页", tint = dsh.textPrimary)
            }
            IconButton(onClick = onToggleOrientation) {
                Icon(Icons.Filled.ScreenRotation, "旋转屏幕", tint = dsh.textPrimary)
            }
            Spacer(Modifier.weight(1f))
            IconButton(onClick = onDisconnect) {
                Icon(Icons.Filled.LinkOff, "断开连接", tint = dsh.danger)
            }
            IconButton(onClick = { setToolbarCollapsed(true) }) {
                Icon(Icons.Filled.KeyboardArrowDown, "收起工具栏", tint = dsh.textSecondary)
            }
        }
        }

        if (visible && !fullScreen && toolbarCollapsed) {
            Surface(
                color = dsh.layer1,
                shape = DshControlShape,
                border = BorderStroke(1.dp, dsh.borderOver(dsh.borderL2, dsh.layer1)),
                modifier = Modifier.align(Alignment.BottomEnd)
                    .windowInsetsPadding(WindowInsets.safeDrawing)
                    .imePadding()
                    .padding(end = 12.dp, bottom = 8.dp)
            ) {
                IconButton(onClick = { setToolbarCollapsed(false) }) {
                    Icon(Icons.Filled.KeyboardArrowUp, "展开工具栏", tint = dsh.textSecondary)
                }
            }
        }

        // Tunnel status overlay: covers the dead WebView while disconnected.
        if (!tunnelUp && visible) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .background(dsh.scrim),
                contentAlignment = Alignment.Center
            ) {
                Surface(
                    shape = DshCardShape,
                    color = dsh.layer1,
                    border = BorderStroke(1.dp, dsh.borderOver(dsh.borderL2, dsh.layer1)),
                    shadowElevation = 0.dp,
                    tonalElevation = 0.dp,
                    modifier = Modifier.padding(24.dp)
                ) {
                    Column(
                        modifier = Modifier.padding(20.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        when (tunnelInfo?.status) {
                            TunnelState.Status.FAILED -> {
                                Text(
                                    "连接失败",
                                    fontWeight = FontWeight.SemiBold,
                                    fontSize = 15.sp,
                                    color = dsh.danger
                                )
                                tunnelInfo.message?.let {
                                    Text(
                                        it, fontSize = 12.sp,
                                        color = dsh.textTertiary,
                                        textAlign = TextAlign.Center
                                    )
                                }
                                OutlinedButton(
                                    onClick = onReconnect,
                                    shape = DshControlShape,
                                    colors = ButtonDefaults.outlinedButtonColors(
                                        contentColor = dsh.accent
                                    )
                                ) { Text("重连") }
                            }

                            TunnelState.Status.RETRYING,
                            TunnelState.Status.CONNECTING,
                            TunnelState.Status.IDLE -> {
                                CircularProgressIndicator(
                                    color = dsh.accent,
                                    trackColor = dsh.layer3,
                                    modifier = Modifier.size(28.dp)
                                )
                                Text(
                                    if (tunnelInfo?.status == TunnelState.Status.RETRYING)
                                        "隧道中断，正在重连…"
                                    else "正在连接隧道…",
                                    fontSize = 14.sp,
                                    color = dsh.textPrimary
                                )
                            }

                            else -> {
                                Text(
                                    "隧道未连接",
                                    fontWeight = FontWeight.SemiBold,
                                    fontSize = 15.sp,
                                    color = dsh.warning
                                )
                                tunnelInfo?.message?.let {
                                    Text(
                                        it, fontSize = 12.sp,
                                        color = dsh.textTertiary,
                                        textAlign = TextAlign.Center
                                    )
                                }
                                OutlinedButton(
                                    onClick = onReconnect,
                                    shape = DshControlShape,
                                    colors = ButtonDefaults.outlinedButtonColors(
                                        contentColor = dsh.accent
                                    )
                                ) { Text("重连") }
                            }
                        }
                        Button(
                            onClick = onChangeServer,
                            shape = DshControlShape,
                            colors = ButtonDefaults.buttonColors(
                                containerColor = dsh.brandFill,
                                contentColor = dsh.brandText
                            )
                        ) { Text("返回") }
                    }
                }
            }
        }

        // Auth-required paste dialog (manual fallback for token-gated pages).
    if (showAuthPrompt && visible) {
            AlertDialog(
                onDismissRequest = { showAuthPrompt = false },
                title = { Text("网页要求重新认证", fontWeight = FontWeight.SemiBold, fontSize = 16.sp) },
                text = {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(
                            "服务端要求先用带令牌的 URL 打开一次（例如 dsh 更新后）。" +
                                    "请复制服务器上 dsh web 打印的完整 URL —— 或只复制 token 本身 —— " +
                                    "粘贴到下面，将直接在内置浏览器中完成认证。",
                            fontSize = 12.sp,
                            color = dsh.textTertiary
                        )
                        OutlinedTextField(
                            value = authInput,
                            onValueChange = {
                                authInput = it
                                authHint = null
                            },
                            label = { Text("完整 URL、?token=… 或 token") },
                            singleLine = true,
                            shape = DshControlShape,
                            textStyle = dsh.monoStyle(14),
                            colors = dshFieldColors(),
                            modifier = Modifier.fillMaxWidth()
                        )
                        authHint?.let {
                            Text(it, fontSize = 11.sp, color = dsh.danger)
                        }
                    }
                },
                confirmButton = {
                    TextButton(onClick = { openAuthInput(authInput) }) { Text("打开") }
                },
                dismissButton = {
                    TextButton(onClick = { showAuthPrompt = false }) { Text("取消") }
                },
                shape = DshCardShape,
                containerColor = MaterialTheme.colorScheme.surfaceContainerLow,
                titleContentColor = dsh.textPrimary,
                textContentColor = dsh.textSecondary
            )
        }

        if (visible && fullScreen) Box(Modifier.align(Alignment.TopEnd).padding(12.dp)) {
            SmallFloatingActionButton(onClick = { controlsExpanded = !controlsExpanded }, containerColor = dsh.accent, contentColor = dsh.brandText) {
                Icon(Icons.Filled.MoreVert, "服务菜单")
            }
            DropdownMenu(expanded = controlsExpanded, onDismissRequest = { controlsExpanded = false }) {
                DropdownMenuItem(text = { Text("刷新网页") }, onClick = { controlsExpanded = false; webRef?.reload() })
                DropdownMenuItem(text = { Text("旋转屏幕") }, onClick = { controlsExpanded = false; onToggleOrientation() })
                DropdownMenuItem(text = { Text("返回连接列表（保持连接）") }, onClick = { controlsExpanded = false; onChangeServer() })
                DropdownMenuItem(text = { Text("设置") }, onClick = { controlsExpanded = false; onOpenSettings() })
                if (tunnelMode) DropdownMenuItem(text = { Text("重新连接") }, onClick = { controlsExpanded = false; onReconnect() })
                DropdownMenuItem(text = { Text("断开连接", color = dsh.danger) }, onClick = { controlsExpanded = false; onDisconnect() })
            }
        }
    }

    BackHandler(enabled = visible) {
        if (canGoBack) webRef?.goBack() else onChangeServer()
    }
}

/**
 * The floating control bubble: accent disc with a 1dp rank-1 border instead of
 * a shadow, so it reads the same as every other DSH surface.
 */
@Composable
private fun ActionBall(
    onClick: () -> Unit,
    containerColor: Color,
    contentColor: Color,
    modifier: Modifier = Modifier,
    content: @Composable () -> Unit
) {
    SmallFloatingActionButton(
        onClick = onClick,
        shape = DshControlShape,
        containerColor = containerColor,
        contentColor = contentColor,
        elevation = FloatingActionButtonDefaults.elevation(
            defaultElevation = 0.dp,
            pressedElevation = 0.dp,
            focusedElevation = 0.dp,
            hoveredElevation = 0.dp
        ),
        modifier = modifier.border(
            1.dp,
            LocalDsh.current.borderOver(
                if (LocalDsh.current.isDark) LocalDsh.current.borderL3
                else LocalDsh.current.borderL1,
                containerColor
            ),
            DshControlShape
        )
    ) {
        Box(contentAlignment = Alignment.Center) { content() }
    }
}

private class FileChooserHost {
    var callback: ValueCallback<Array<Uri>>? = null
    var open: ((Array<String>, Boolean) -> Unit)? = null

    fun show(
        next: ValueCallback<Array<Uri>>?,
        mimeTypes: Array<String>,
        multiple: Boolean
    ): Boolean {
        callback?.onReceiveValue(null)
        callback = next
        val opener = open
        if (next == null || opener == null) {
            callback = null
            next?.onReceiveValue(null)
            return false
        }
        return try {
            opener(mimeTypes, multiple)
            true
        } catch (_: Exception) {
            callback = null
            next.onReceiveValue(null)
            false
        }
    }

    fun complete(uris: Array<Uri>?) {
        val cb = callback
        callback = null
        cb?.onReceiveValue(uris)
    }
}

private fun OrientationMode.icon(): ImageVector = when (this) {
    OrientationMode.PORTRAIT  -> Icons.Filled.StayCurrentPortrait
    OrientationMode.LANDSCAPE -> Icons.Filled.StayCurrentLandscape
    OrientationMode.AUTO      -> Icons.Filled.ScreenRotation
}
