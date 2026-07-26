package com.opencode.web

import android.annotation.SuppressLint
import android.view.ViewGroup
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ScreenRotation
import androidx.compose.material.icons.filled.StayCurrentLandscape
import androidx.compose.material.icons.filled.StayCurrentPortrait
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SmallFloatingActionButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView

@SuppressLint("SetJavaScriptEnabled")
@Composable
internal fun WebViewScreen(
    url: String,
    orientation: OrientationMode,
    onToggleOrientation: () -> Unit,
    onChangeServer: () -> Unit
) {
    var webRef by remember { mutableStateOf<WebView?>(null) }
    var canGoBack by remember { mutableStateOf(false) }

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

        Column(
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(12.dp)
                .alpha(0.55f),
            horizontalAlignment = Alignment.End,
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            SmallFloatingActionButton(
                onClick = onToggleOrientation,
                shape = CircleShape,
                containerColor = MaterialTheme.colorScheme.primary
            ) {
                Icon(
                    orientation.icon(),
                    contentDescription = "方向: ${orientation.storage}",
                    tint = MaterialTheme.colorScheme.onPrimary,
                    modifier = Modifier.size(20.dp)
                )
            }
            FloatingActionButton(
                onClick = onChangeServer,
                shape = CircleShape,
                containerColor = MaterialTheme.colorScheme.primary
            ) {
                Text("服务器", color = MaterialTheme.colorScheme.onPrimary)
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
