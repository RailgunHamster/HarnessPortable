package com.harness.portable

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.DeleteOutline
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

@Composable
internal fun ShellIcon(icon: ImageVector) {
    val dsh = LocalDsh.current
    Box(Modifier.size(38.dp).background(dsh.accentSoft, DshControlShape), contentAlignment = Alignment.Center) {
        Icon(icon, contentDescription = null, tint = dsh.accent, modifier = Modifier.size(20.dp))
    }
}

@Composable
internal fun ShellStatusPill(label: String, color: Color) {
    Row(
        Modifier.background(color.copy(alpha = 0.12f), CircleShape).padding(horizontal = 8.dp, vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(5.dp)
    ) {
        Box(Modifier.size(6.dp).background(color, CircleShape))
        Text(label, style = MaterialTheme.typography.bodySmall, color = color)
    }
}

@Composable
internal fun ShellEmptyState(
    icon: ImageVector, title: String, message: String,
    actionLabel: String, onAction: () -> Unit
) {
    val dsh = LocalDsh.current
    Column(
        Modifier.fillMaxWidth().dshCard().padding(28.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        ShellIcon(icon)
        Text(title, style = MaterialTheme.typography.titleMedium, color = dsh.textPrimary, textAlign = TextAlign.Center)
        Text(message, style = MaterialTheme.typography.bodyMedium, color = dsh.textSecondary, textAlign = TextAlign.Center)
        Button(onClick = onAction, shape = DshControlShape) { Text(actionLabel) }
    }
}

@Composable
internal fun RowActions(onDelete: () -> Unit, onEdit: (() -> Unit)? = null) {
    var expanded by remember { mutableStateOf(false) }
    val dsh = LocalDsh.current
    Box {
        IconButton(onClick = { expanded = true }) {
            Icon(Icons.Filled.MoreVert, contentDescription = "更多操作", tint = dsh.textSecondary)
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false },
            containerColor = dsh.layer1, tonalElevation = 0.dp, shadowElevation = 0.dp) {
            if (onEdit != null) {
                DropdownMenuItem(text = { Text("编辑连接") }, leadingIcon = { Icon(Icons.Filled.Edit, null) },
                    onClick = { expanded = false; onEdit() })
            }
            DropdownMenuItem(text = { Text("删除连接", color = dsh.danger) },
                leadingIcon = { Icon(Icons.Filled.DeleteOutline, null, tint = dsh.danger) },
                onClick = { expanded = false; onDelete() })
        }
    }
}
