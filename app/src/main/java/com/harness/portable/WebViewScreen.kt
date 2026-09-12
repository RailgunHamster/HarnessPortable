package com.harness.portable

import android.annotation.SuppressLint
import android.content.Context
import android.view.ViewGroup
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.foundation.background
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
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Autorenew
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Dns
import androidx.compose.material.icons.filled.LinkOff
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.ScreenRotation
import androidx.compose.material.icons.filled.StayCurrentLandscape
import androidx.compose.material.icons.filled.StayCurrentPortrait
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.SmallFloatingActionButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
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
    onReconnect: () -> Unit
) {
    val ctx = LocalContext.current
    val localDensity = LocalDensity.current
    val prefs = remember { ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE) }

    var webRef by remember { mutableStateOf<WebView?>(null) }
    var canGoBack by remember { mutableStateOf(false) }
    var controlsExpanded by remember { mutableStateOf(false) }
    var dragging by remember { mutableStateOf(false) }

    val ballSizeDp = 48.dp
    val marginDp = 10.dp
    val buttonCount = if (tunnelMode) 4 else 3
    val actionsHeightDp = (buttonCount * 48 + (buttonCount - 1) * 6).dp

    var ballX by remember { mutableStateOf(prefs.getFloat("ball_x_dp", Float.NaN)) }
    var ballY by remember { mutableStateOf(prefs.getFloat("ball_y_dp", Float.NaN)) }

    // Tunnel-awareness: reload when the URL changes (e.g. rebind on another
    // local port) and once the tunnel has come back after a drop.
    var loadedUrl by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(url) {
        val w = webRef
        if (w != null && loadedUrl != null && loadedUrl != url) w.loadUrl(url)
        loadedUrl = url
    }

    val tunnelUp = tunnelInfo == null || tunnelInfo.status == TunnelState.Status.CONNECTED
    var tunnelWasDown by remember { mutableStateOf(false) }
    LaunchedEffect(tunnelUp) {
        if (tunnelUp && tunnelWasDown) webRef?.reload()
        tunnelWasDown = !tunnelUp
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
            else -> null
        }
        if (target == null) {
            authHint = "无法识别输入。请粘贴 dsh web 打印的完整 URL（应包含 ?token=… 之类的参数）。"
        } else {
            showAuthPrompt = false
            authHint = null
            webRef?.loadUrl(target)
        }
    }

    Box(modifier = Modifier.fillMaxSize()) {
        AndroidView(
            factory = { ctx ->
                WebView(ctx).apply {
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
                    loadUrl(url)
                }.also { webRef = it }
            },
            modifier = Modifier
                .fillMaxSize()
                .imePadding()
        )

        // Tunnel status overlay: covers the dead WebView while disconnected.
        if (!tunnelUp) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .background(MaterialTheme.colorScheme.scrim.copy(alpha = 0.7f)),
                contentAlignment = Alignment.Center
            ) {
                Surface(
                    shape = RoundedCornerShape(16.dp),
                    tonalElevation = 4.dp,
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
                                    fontSize = 15.sp
                                )
                                tunnelInfo.message?.let {
                                    Text(
                                        it, fontSize = 12.sp,
                                        color = MaterialTheme.colorScheme.outline,
                                        textAlign = TextAlign.Center
                                    )
                                }
                                OutlinedButton(onClick = onReconnect) { Text("重连") }
                            }

                            else -> {
                                CircularProgressIndicator(modifier = Modifier.size(28.dp))
                                Text(
                                    if (tunnelInfo?.status == TunnelState.Status.RETRYING)
                                        "隧道中断，正在重连…"
                                    else "隧道未连接",
                                    fontSize = 14.sp
                                )
                            }
                        }
                        Button(onClick = onChangeServer) { Text("返回") }
                    }
                }
            }
        }

        // Auth-required paste dialog (manual fallback for token-gated pages).
        if (showAuthPrompt) {
            AlertDialog(
                onDismissRequest = { showAuthPrompt = false },
                title = { Text("网页要求重新认证", fontWeight = FontWeight.SemiBold, fontSize = 16.sp) },
                text = {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(
                            "服务端要求先用带令牌的 URL 打开一次（例如 dsh 更新后）。" +
                                    "请复制服务器上 dsh web 打印的完整 URL 粘贴到下面，" +
                                    "将直接在内置浏览器中完成认证。",
                            fontSize = 12.sp,
                            color = MaterialTheme.colorScheme.outline
                        )
                        OutlinedTextField(
                            value = authInput,
                            onValueChange = {
                                authInput = it
                                authHint = null
                            },
                            label = { Text("完整 URL 或 ?token=…") },
                            singleLine = true,
                            modifier = Modifier.fillMaxWidth()
                        )
                        authHint?.let {
                            Text(it, fontSize = 11.sp, color = MaterialTheme.colorScheme.error)
                        }
                    }
                },
                confirmButton = {
                    TextButton(onClick = { openAuthInput(authInput) }) { Text("打开") }
                },
                dismissButton = {
                    TextButton(onClick = { showAuthPrompt = false }) { Text("取消") }
                }
            )
        }

        BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
            val maxX = (maxWidth - ballSizeDp).value
            val maxY = (maxHeight - ballSizeDp).value
            val currentX = when {
                ballX.isNaN() || ballX < 0 || ballX > maxX -> (maxX - marginDp.value)
                else -> ballX
            }
            val currentY = when {
                ballY.isNaN() || ballY < 0 || ballY > maxY -> marginDp.value
                else -> ballY
            }

            // Expand downward if there's room, otherwise upward
            val spaceBelow = maxHeight.value - currentY - ballSizeDp.value
            val expandDown = spaceBelow >= actionsHeightDp.value + 6f
            val actionsY = if (expandDown) {
                currentY + ballSizeDp.value + 6f
            } else {
                currentY - actionsHeightDp.value - 6f
            }

            // Toggle button (draggable)
            Box(
                modifier = Modifier
                    .offset {
                        IntOffset(
                            (currentX * localDensity.density).roundToInt(),
                            (currentY * localDensity.density).roundToInt()
                        )
                    }
                    .pointerInput(maxX, maxY, localDensity.density) {
                        detectDragGestures(
                            onDragStart = { dragging = true },
                            onDragEnd = {
                                dragging = false
                                prefs.edit()
                                    .putFloat("ball_x_dp", ballX)
                                    .putFloat("ball_y_dp", ballY)
                                    .apply()
                            },
                            onDragCancel = { dragging = false },
                            onDrag = { change, dragAmount ->
                                change.consume()
                                val dx = dragAmount.x / localDensity.density
                                val dy = dragAmount.y / localDensity.density
                                val baseX = if (ballX.isNaN()) currentX else ballX
                                val baseY = if (ballY.isNaN()) currentY else ballY
                                ballX = (baseX + dx).coerceIn(0f, maxX)
                                ballY = (baseY + dy).coerceIn(0f, maxY)
                            }
                        )
                    }
            ) {
                SmallFloatingActionButton(
                    onClick = { controlsExpanded = !controlsExpanded },
                    shape = CircleShape,
                    containerColor = MaterialTheme.colorScheme.primary,
                    modifier = Modifier
                        .size(ballSizeDp)
                        .alpha(if (dragging) 1f else 0.85f)
                ) {
                    Icon(
                        if (controlsExpanded) Icons.Filled.Close else Icons.Filled.MoreVert,
                        contentDescription = if (controlsExpanded) "收起" else "菜单",
                        tint = MaterialTheme.colorScheme.onPrimary,
                        modifier = Modifier.size(20.dp)
                    )
                }
            }

            // Actions column, positioned above or below the toggle
            AnimatedVisibility(
                visible = controlsExpanded,
                modifier = Modifier.offset {
                    IntOffset(
                        (currentX * localDensity.density).roundToInt(),
                        (actionsY * localDensity.density).roundToInt()
                    )
                },
                enter = fadeIn() + scaleIn(initialScale = 0.7f),
                exit = fadeOut() + scaleOut(targetScale = 0.7f)
            ) {
                Column(
                    verticalArrangement = Arrangement.spacedBy(6.dp),
                    horizontalAlignment = Alignment.End
                ) {
                    SmallFloatingActionButton(
                        onClick = { webRef?.reload() },
                        shape = CircleShape,
                        containerColor = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(ballSizeDp).alpha(0.85f)
                    ) {
                        Icon(
                            Icons.Filled.Refresh,
                            contentDescription = "刷新",
                            tint = MaterialTheme.colorScheme.onPrimary,
                            modifier = Modifier.size(22.dp)
                        )
                    }
                    SmallFloatingActionButton(
                        onClick = onToggleOrientation,
                        shape = CircleShape,
                        containerColor = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(ballSizeDp).alpha(0.85f)
                    ) {
                        Icon(
                            orientation.icon(),
                            contentDescription = "方向: ${orientation.storage}",
                            tint = MaterialTheme.colorScheme.onPrimary,
                            modifier = Modifier.size(22.dp)
                        )
                    }
                    SmallFloatingActionButton(
                        onClick = onChangeServer,
                        shape = CircleShape,
                        containerColor = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(ballSizeDp).alpha(0.85f)
                    ) {
                        Icon(
                            if (tunnelMode) Icons.Filled.LinkOff else Icons.Filled.Dns,
                            contentDescription =
                                if (tunnelMode) "断开并返回" else "服务器",
                            tint = MaterialTheme.colorScheme.onPrimary,
                            modifier = Modifier.size(22.dp)
                        )
                    }
                    if (tunnelMode) {
                        SmallFloatingActionButton(
                            onClick = onReconnect,
                            shape = CircleShape,
                            containerColor = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.size(ballSizeDp).alpha(0.85f)
                        ) {
                            Icon(
                                Icons.Filled.Autorenew,
                                contentDescription = "重连隧道",
                                tint = MaterialTheme.colorScheme.onPrimary,
                                modifier = Modifier.size(22.dp)
                            )
                        }
                    }
                }
            }
        }
    }

    BackHandler(enabled = canGoBack) {
        webRef?.goBack()
    }
}

private fun OrientationMode.icon(): ImageVector = when (this) {
    OrientationMode.PORTRAIT  -> Icons.Filled.StayCurrentPortrait
    OrientationMode.LANDSCAPE -> Icons.Filled.StayCurrentLandscape
    OrientationMode.AUTO      -> Icons.Filled.ScreenRotation
}
