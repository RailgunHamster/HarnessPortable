package com.opencode.web

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    savedServers: List<String>,
    onConnect: (String) -> Unit,
    onAddServer: (String) -> Unit,
    onDeleteServer: (String) -> Unit
) {
    var input by remember { mutableStateOf("") }

    Scaffold(
        topBar = { TopAppBar(title = { Text("OpenCode Web") }) }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            item {
                Text("添加服务器", fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                Spacer(Modifier.height(6.dp))
                OutlinedTextField(
                    value = input,
                    onValueChange = { input = it },
                    label = { Text("IP 或完整 URL，如 192.168.0.104") },
                    placeholder = { Text("192.168.0.104") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(
                        keyboardType = KeyboardType.Uri,
                        imeAction = ImeAction.Done
                    ),
                    keyboardActions = KeyboardActions(
                        onDone = {
                            val norm = normalizeUrl(input)
                            if (norm != null) {
                                onAddServer(norm)
                                input = ""
                            }
                        }
                    ),
                    trailingIcon = {
                        IconButton(onClick = {
                            val norm = normalizeUrl(input)
                            if (norm != null) {
                                onAddServer(norm)
                                input = ""
                            }
                        }) { Icon(Icons.Filled.Add, contentDescription = "添加") }
                    },
                    modifier = Modifier.fillMaxWidth()
                )
            }

            item {
                Spacer(Modifier.height(8.dp))
                HorizontalDivider()
                Spacer(Modifier.height(8.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("已保存", fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                    Spacer(Modifier.size(8.dp))
                    Text("${savedServers.size}", color = MaterialTheme.colorScheme.outline, fontSize = 12.sp)
                }
                Spacer(Modifier.height(4.dp))
            }

            if (savedServers.isEmpty()) {
                item {
                    Text(
                        "还没有保存的服务器。在上面输入 IP 然后点 + 添加。",
                        color = MaterialTheme.colorScheme.outline,
                        fontSize = 13.sp
                    )
                }
            }

            items(savedServers, key = { it }) { url ->
                ServerRow(
                    url = url,
                    onConnect = { onConnect(url) },
                    onDelete = { onDeleteServer(url) }
                )
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ServerRow(
    url: String,
    onConnect: () -> Unit,
    onDelete: () -> Unit
) {
    Surface(
        onClick = onConnect,
        shape = RoundedCornerShape(12.dp),
        tonalElevation = 1.dp,
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(hostOf(url), fontWeight = FontWeight.Medium, fontSize = 15.sp)
                Text(url, fontSize = 12.sp, color = MaterialTheme.colorScheme.outline)
            }
            IconButton(onClick = onDelete) {
                Icon(
                    Icons.Filled.Delete,
                    contentDescription = "删除",
                    tint = MaterialTheme.colorScheme.outline
                )
            }
        }
    }
}

internal fun normalizeUrl(raw: String): String? {
    val s = raw.trim()
    if (s.isEmpty()) return null
    return when {
        s.startsWith("http://") || s.startsWith("https://") || s.contains("://") -> s
        else -> {
            val withPort = if (s.contains(":")) s else "$s:4096"
            "http://$withPort"
        }
    }
}

internal fun hostOf(url: String): String {
    return try {
        val u = java.net.URI(url)
        u.host?.takeIf { it.isNotEmpty() } ?: url
    } catch (e: Exception) {
        url
    }
}
