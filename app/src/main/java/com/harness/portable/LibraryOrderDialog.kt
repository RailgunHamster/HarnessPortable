package com.harness.portable

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.automirrored.filled.Sort
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.DialogProperties
import com.harness.portable.automation.*

internal data class LibraryOrderItem(val key: String, val name: String, val kind: String)

@Composable
internal fun LibraryOrderDialog(entries: List<LibraryOrderItem>, onDismiss: () -> Unit, onSave: (List<String>) -> String?) {
    val initial = remember { entries.toList() }
    var draft by remember { mutableStateOf(initial) }
    var error by remember { mutableStateOf<String?>(null) }
    fun move(index: Int, direction: Int) {
        val target = index + direction
        if (target !in draft.indices) return
        draft = draft.toMutableList().apply { add(target, removeAt(index)) }
        error = null
    }
    AutomationAlertDialog(
        onDismissRequest = onDismiss,
        modifier = Modifier.widthIn(max = 640.dp).fillMaxWidth(.94f).automationId("Library.Order.Dialog"),
        icon = { Icon(Icons.AutoMirrored.Filled.Sort, null) },
        title = { Text("调整连接顺序") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Text("上下移动后保存，顺序也会随 WebDAV 配置同步。", style = MaterialTheme.typography.bodyMedium)
                LazyColumn(modifier = Modifier.fillMaxWidth().heightIn(max = 360.dp).automationId("Library.Order.Items"), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    itemsIndexed(draft, key = { _, entry -> entry.key }) { index, entry ->
                        Row(Modifier.fillMaxWidth().dshCard().padding(start = 12.dp, end = 2.dp, top = 6.dp, bottom = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                            Text((index + 1).toString(), modifier = Modifier.width(28.dp), style = MaterialTheme.typography.labelLarge, color = LocalDsh.current.textTertiary)
                            Column(Modifier.weight(1f)) {
                                Text(entry.name, maxLines = 1, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.titleSmall)
                                Text(entry.kind, style = MaterialTheme.typography.bodySmall, color = LocalDsh.current.textSecondary)
                            }
                            IconButton(onClick = { move(index, -1) }, enabled = index > 0, modifier = Modifier.automationId("Library.Order.Up", AndroidAutomation.dataKey(entry.key))) { Icon(Icons.Filled.KeyboardArrowUp, "上移 ${entry.name}") }
                            IconButton(onClick = { move(index, 1) }, enabled = index < draft.lastIndex, modifier = Modifier.automationId("Library.Order.Down", AndroidAutomation.dataKey(entry.key))) { Icon(Icons.Filled.KeyboardArrowDown, "下移 ${entry.name}") }
                        }
                    }
                }
                error?.let { Text(it, color = LocalDsh.current.danger, style = MaterialTheme.typography.bodySmall) }
            }
        },
        confirmButton = {
            Button(onClick = { error = onSave(draft.map { it.key }); if (error == null) onDismiss() }, enabled = draft.map { it.key } != initial.map { it.key }, modifier = Modifier.automationId("Library.Order.Save")) { Text("保存顺序") }
        },
        dismissButton = { TextButton(onClick = onDismiss, modifier = Modifier.automationId("Library.Order.Cancel")) { Text("取消") } },
        shape = DshCardShape,
        containerColor = MaterialTheme.colorScheme.surfaceContainerLow,
        properties = DialogProperties(usePlatformDefaultWidth = false)
    )
}
