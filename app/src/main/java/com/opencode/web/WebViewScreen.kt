package com.opencode.web

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
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Dns
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.ScreenRotation
import androidx.compose.material.icons.filled.StayCurrentLandscape
import androidx.compose.material.icons.filled.StayCurrentPortrait
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SmallFloatingActionButton
import androidx.compose.runtime.Composable
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
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import kotlin.math.roundToInt

@SuppressLint("SetJavaScriptEnabled")
@Composable
internal fun WebViewScreen(
    url: String,
    orientation: OrientationMode,
    onToggleOrientation: () -> Unit,
    onChangeServer: () -> Unit
) {
    val ctx = LocalContext.current
    val localDensity = LocalDensity.current
    val prefs = remember { ctx.getSharedPreferences("opencode_web", Context.MODE_PRIVATE) }

    var webRef by remember { mutableStateOf<WebView?>(null) }
    var canGoBack by remember { mutableStateOf(false) }
    var controlsExpanded by remember { mutableStateOf(false) }
    var dragging by remember { mutableStateOf(false) }

    val ballSizeDp = 48.dp
    val marginDp = 10.dp
    val actionsHeightDp = 156.dp   // 3 buttons * 48dp + 2 gaps * 6dp

    var ballX by remember { mutableStateOf(prefs.getFloat("ball_x_dp", Float.NaN)) }
    var ballY by remember { mutableStateOf(prefs.getFloat("ball_y_dp", Float.NaN)) }

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
                            view.evaluateJavascript(SAFE_AREA_OVERRIDE_JS, null)
                        }
                    }
                    loadUrl(url)
                }.also { webRef = it }
            },
            modifier = Modifier.fillMaxSize()
        )

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
                    .pointerInput(Unit) {
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
                            Icons.Filled.Dns,
                            contentDescription = "服务器",
                            tint = MaterialTheme.colorScheme.onPrimary,
                            modifier = Modifier.size(22.dp)
                        )
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

// opencode web adds 30px padding-top on mobile viewports (status-bar placeholder).
// Strip it so content sits flush at the top of the screen.
private const val SAFE_AREA_OVERRIDE_JS = """
(function() {
    try {
        if (document.getElementById('ocw-override')) return;
        var s = document.createElement('style');
        s.id = 'ocw-override';
        s.textContent =
            'html,body{' +
              'height:100%!important;width:100%!important;' +
              'margin:0!important;padding:0!important;overflow:hidden!important;' +
            '}' +
            '#root{height:100%!important;min-height:100%!important;}' +
            '#root > *{padding-top:0!important;}';
        document.head.appendChild(s);
    } catch (e) {}
})();
"""
