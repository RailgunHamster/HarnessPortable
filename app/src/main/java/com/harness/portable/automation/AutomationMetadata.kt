package com.harness.portable.automation

import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.SemanticsPropertyKey
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTag

internal val AutomationDataKey = SemanticsPropertyKey<String>("HarnessDataKey")
internal val AutomationSensitive = SemanticsPropertyKey<Boolean>("HarnessSensitive", mergePolicy = { parent, child -> parent == true || child })
internal val AutomationVisible = SemanticsPropertyKey<Boolean>("HarnessVisible", mergePolicy = { parent, child -> parent != false && child })
internal val AutomationScrollOwner = SemanticsPropertyKey<Boolean>("HarnessScrollOwner")

/** Stable identity belongs to the operation, independently of its displayed label. */
fun Modifier.automationId(id: String, dataKey: String = ""): Modifier = semantics {
    testTag = id
    if (dataKey.isNotEmpty()) this[AutomationDataKey] = dataKey
}

/** Also protects a password/token after the user toggles its visual mask. */
fun Modifier.automationSensitive(): Modifier = semantics { this[AutomationSensitive] = true }
fun Modifier.automationVisible(visible: Boolean): Modifier = semantics { this[AutomationVisible] = visible }
/** Material tabs and menus own an internal scroll node distinct from their tagged surface. */
fun Modifier.automationScrollOwner(): Modifier = semantics { this[AutomationScrollOwner] = true }
