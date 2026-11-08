@file:OptIn(androidx.compose.ui.ExperimentalComposeUiApi::class)
package com.harness.portable.automation

import android.os.Build
import android.view.View
import android.view.ViewGroup
import android.view.Choreographer
import android.view.inspector.WindowInspector
import androidx.activity.ComponentDialog
import androidx.compose.ui.platform.ViewRootForTest
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsConfiguration
import androidx.compose.ui.semantics.SemanticsNode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.state.ToggleableState
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.window.DialogWindowProvider
import org.json.JSONArray
import org.json.JSONObject
import java.lang.ref.WeakReference
import java.util.WeakHashMap
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume

/** Uses Compose's published semantics interface, never private handlers or screen input. */
internal object AndroidUiAutomation {
    private val roots = mutableListOf<WeakReference<ViewRootForTest>>()
    private val rootIds = WeakHashMap<View, Int>()
    private var nextRoot = 0
    private var installed = false
    private data class Node(val root: ViewRootForTest, val node: SemanticsNode, val parent: String?, val sensitive: Boolean, val visible: Boolean) {
        val id: String get() = "compose:${rootIds.getOrPut(root.view) { ++nextRoot }}:${node.id}"
        val config: SemanticsConfiguration get() = node.config
    }
    fun install() {
        if (installed) return; installed = true
        val previous = ViewRootForTest.onViewCreatedCallback
        ViewRootForTest.onViewCreatedCallback = { root -> previous?.invoke(root); roots.removeAll { it.get() == null }; roots.add(WeakReference(root)) }
    }
    private fun collect(): List<Node> {
        val active = linkedSetOf<ViewRootForTest>()
        fun views(view: View) {
            if (view is ViewRootForTest) active.add(view)
            if (view is ViewGroup) for (i in 0 until view.childCount) views(view.getChildAt(i))
        }
        views(AndroidAutomation.currentActivity.window.decorView)
        if (Build.VERSION.SDK_INT >= 29) WindowInspector.getGlobalWindowViews().forEach(::views)
        roots.removeAll { it.get() == null }
        roots.forEach { it.get()?.let(active::add) }
        val result = mutableListOf<Node>()
        active.filter { it.view.isAttachedToWindow && it.view.isShown }.forEach { root ->
            val sensitive = mutableSetOf<Int>(); val ancestors = mutableSetOf<Int>(); val visibility = mutableMapOf<Int, Boolean>()
            fun metadata(node: SemanticsNode, inheritedSensitive: Boolean, inheritedVisible: Boolean) {
                if (visibility.size >= 10000) fail("limit_exceeded", "Semantics tree exceeds the node limit")
                val hidden = node.config.getOrNull(AutomationVisible) == false || node.config.contains(SemanticsProperties.InvisibleToUser)
                val visible = inheritedVisible && !hidden; visibility[node.id] = visible
                val secret = inheritedSensitive || node.config.getOrNull(AutomationSensitive) == true || node.config.contains(SemanticsProperties.Password)
                if (secret) {
                    sensitive.add(node.id); var parent = node.parent
                    while (parent != null) { ancestors.add(parent.id); parent = parent.parent }
                }
                node.children.forEach { metadata(it, secret, visible) }
            }
            metadata(root.semanticsOwner.unmergedRootSemanticsNode, false, true)
            fun visit(node: SemanticsNode, parent: String?) {
                if (result.size >= 10000) fail("limit_exceeded", "Semantics tree exceeds the node limit")
                val bounds = node.boundsInWindow
                val item = Node(root, node, parent, node.id in sensitive || node.id in ancestors,
                    visibility[node.id] != false && bounds.width > 0 && bounds.height > 0)
                result.add(item); node.children.forEach { visit(it, item.id) }
            }
            visit(root.semanticsOwner.rootSemanticsNode, null)
        }
        return result
    }
    private fun actions(c: SemanticsConfiguration): List<String> = buildList {
        if (c.contains(SemanticsActions.OnClick)) add("invoke")
        if (c.contains(SemanticsActions.OnLongClick)) add("longClick")
        if (c.contains(SemanticsActions.SetText)) add("setValue")
        if (c.contains(SemanticsActions.SetSelection)) add("selectText")
        if (c.contains(SemanticsActions.ScrollBy) || c.contains(SemanticsActions.ScrollByOffset)) add("scroll")
        if (c.contains(SemanticsActions.ScrollToIndex)) add("realize")
        if (c.contains(SemanticsActions.RequestFocus)) add("focus")
        if (c.contains(SemanticsActions.Expand) || c.contains(SemanticsActions.Collapse)) add("setExpanded")
        if (c.contains(SemanticsActions.Dismiss)) add("dismiss")
        if (c.contains(SemanticsActions.SetProgress)) add("setProgress")
        if (c.contains(SemanticsActions.OnImeAction)) add("ime")
        if (c.contains(SemanticsActions.CopyText) || c.contains(SemanticsActions.CutText) || c.contains(SemanticsActions.PasteText)) add("edit")
        if (c.contains(SemanticsProperties.ToggleableState)) add("setChecked")
        if (c.contains(SemanticsProperties.Selected)) add("selectItem")
    }
    private fun snapshot(item: Node): JSONObject {
        val c = item.config; val value = c.getOrNull(SemanticsProperties.EditableText)?.text
        val text = c.getOrNull(SemanticsProperties.Text)?.joinToString("\n") { it.text }.orEmpty()
        val name = c.getOrNull(SemanticsProperties.ContentDescription)?.joinToString("\n").orEmpty()
        var identity = c.getOrNull(SemanticsProperties.TestTag)
        if (identity == null && (c.contains(SemanticsActions.ScrollBy) || c.contains(SemanticsActions.ScrollByOffset))) {
            var parent = item.node.parent
            while (parent != null) {
                if (parent.config.getOrNull(AutomationScrollOwner) == true) {
                    identity = parent.config.getOrNull(SemanticsProperties.TestTag)?.let { "$it.Scroll" }; break
                }; parent = parent.parent
            }
        }
        val result = JSONObject().put("id", item.id).put("parentId", item.parent ?: JSONObject.NULL)
            .put("automationId", identity ?: JSONObject.NULL)
            .put("dataKey", c.getOrNull(AutomationDataKey) ?: JSONObject.NULL)
            .put("type", c.getOrNull(SemanticsProperties.Role)?.toString() ?: if (value != null) "TextField" else "Semantics")
            .put("enabled", !c.contains(SemanticsProperties.Disabled)).put("visible", item.visible)
            .put("sensitive", item.sensitive).put("actions", JSONArray(actions(c)))
        if (item.sensitive) result.put("hasValue", !value.isNullOrEmpty())
        else {
            result.put("name", name.ifBlank { text }).put("text", text)
            if (value != null) result.put("value", value)
            c.getOrNull(SemanticsProperties.StateDescription)?.let { result.put("stateDescription", it) }
            c.getOrNull(SemanticsProperties.Selected)?.let { result.put("selected", it) }
            c.getOrNull(SemanticsProperties.Focused)?.let { result.put("focused", it) }
            c.getOrNull(SemanticsProperties.TextSelectionRange)?.let { result.put("selection", JSONObject().put("start", it.start).put("end", it.end)) }
            c.getOrNull(SemanticsProperties.ProgressBarRangeInfo)?.let { result.put("progress", JSONObject().put("value", it.current).put("minimum", it.range.start).put("maximum", it.range.endInclusive).put("steps", it.steps)) }
        }
        c.getOrNull(SemanticsProperties.ToggleableState)?.let { result.put("checked", when(it) { ToggleableState.On -> true; ToggleableState.Off -> false; ToggleableState.Indeterminate -> JSONObject.NULL }) }
        result.put("activeWindow", item.root.view.hasWindowFocus())
        return result
    }
    fun tree(includeHidden: Boolean): JSONArray = JSONArray(collect().filter { includeHidden || it.visible }.map(::snapshot))
    private fun matches(node: Node, selector: JSONObject): Boolean {
        if (selector.keys().asSequence().any { it !in setOf("id", "automationId", "dataKey", "name", "includeHidden") }) fail("invalid_argument", "Unknown control selector")
        val snap = snapshot(node)
        return (selector.optionalBoolean("includeHidden") || node.visible) && listOf("id", "automationId", "dataKey", "name").all { !selector.has(it) || snap.optString(it) == selector.requiredString(it) }
    }
    fun find(selector: JSONObject): JSONArray = JSONArray(collect().filter { matches(it, selector) }.map(::snapshot))
    fun actionCatalog() = JSONObject().put("actions", JSONArray(listOf("invoke", "longClick", "setValue", "setChecked", "selectItem", "selectText", "scroll", "realize", "focus", "setExpanded", "dismiss", "setProgress", "ime", "edit"))).put("coordinateInput", false)
    suspend fun act(args: JSONObject): Any {
        val selector = args.optJSONObject("target") ?: fail("invalid_argument", "target is required")
        val all = collect()
        val found = all.filter { matches(it, selector) }
        if (found.isEmpty()) fail("not_found", "Control was not found")
        if (found.size != 1) fail("ambiguous", "Selector matches more than one control; include dataKey or id")
        val item = found.single(); val c = item.config
        if (!item.visible || c.contains(SemanticsProperties.Disabled)) fail("unavailable", "Control is hidden or disabled")
        val dialogRoot = all.lastOrNull { it.visible && it.config.contains(SemanticsProperties.IsDialog) }?.root
        // A menu opened inside the active dialog is a newer, separate Compose root.
        // Reject windows below the modal while keeping its child popup actionable.
        val windows = all.map { it.root }.distinct()
        if (dialogRoot != null && windows.indexOf(item.root) < windows.indexOf(dialogRoot)) fail("unavailable", "A modal dialog is open")
        if (!item.root.view.hasWindowFocus() && all.any { it.root.view.hasWindowFocus() }) fail("unavailable", "A different application dialog owns input focus")
        val action = args.requiredString("action")
        if (action !in actions(c)) fail("unsupported_action", "Action is not supported by this control")
        val succeeded = when (action) {
            "invoke" -> c[SemanticsActions.OnClick].action?.invoke()
            "longClick" -> c[SemanticsActions.OnLongClick].action?.invoke()
            "setValue" -> {
                val value = args.requiredString("value"); if (value.length > 65536) fail("invalid_argument", "Input exceeds the size limit")
                // Editable dropdowns request focus when expanded. Focus the old, laid-out
                // value before changing it, as native text entry does, to avoid a stale
                // text-layout cursor during Material's expansion side effect.
                c.getOrNull(SemanticsActions.RequestFocus)?.action?.invoke()
                frame()
                val accepted = c[SemanticsActions.SetText].action?.invoke(AnnotatedString(value))
                frame(); frame(); accepted
            }
            "focus" -> c[SemanticsActions.RequestFocus].action?.invoke()
            "dismiss" -> c[SemanticsActions.Dismiss].action?.invoke()
            "setExpanded" -> if (args.requiredBoolean("value")) c.getOrNull(SemanticsActions.Expand)?.action?.invoke() else c.getOrNull(SemanticsActions.Collapse)?.action?.invoke()
            "setChecked" -> {
                val target = args.requiredBoolean("value"); val now = c[SemanticsProperties.ToggleableState] == ToggleableState.On
                if (target == now) true else c.getOrNull(SemanticsActions.OnClick)?.action?.invoke()
            }
            "selectItem" -> if (c[SemanticsProperties.Selected]) true else c.getOrNull(SemanticsActions.OnClick)?.action?.invoke()
            "selectText" -> {
                val start = args.getInt("start"); val length = args.getInt("length")
                if (start < 0 || length < 0 || start.toLong() + length > (c.getOrNull(SemanticsProperties.EditableText)?.length ?: 0)) fail("invalid_argument", "Invalid text selection")
                c[SemanticsActions.SetSelection].action?.invoke(start, start + length, false)
            }
            "scroll" -> {
                val x = args.optDouble("horizontal", 0.0); val y = args.optDouble("vertical", 0.0)
                if (!x.isFinite() || !y.isFinite() || kotlin.math.abs(x) > 10000 || kotlin.math.abs(y) > 10000) fail("invalid_argument", "Invalid scroll amount")
                val scroll = c.getOrNull(SemanticsActions.ScrollByOffset)
                if (scroll != null) { scroll(Offset(x.toFloat(), y.toFloat())); true }
                else c[SemanticsActions.ScrollBy].action?.invoke(x.toFloat(), y.toFloat())
            }
            "realize" -> c[SemanticsActions.ScrollToIndex].action?.invoke(args.getInt("index").also { if (it < 0) fail("invalid_argument", "Index must be nonnegative") })
            "setProgress" -> args.getDouble("value").let {
                val range = c.getOrNull(SemanticsProperties.ProgressBarRangeInfo)?.range
                if (!it.isFinite() || range != null && it.toFloat() !in range) fail("invalid_argument", "Invalid progress")
                c[SemanticsActions.SetProgress].action?.invoke(it.toFloat())
            }
            "ime" -> c[SemanticsActions.OnImeAction].action?.invoke()
            "edit" -> when (args.requiredString("value")) {
                "copy" -> c.getOrNull(SemanticsActions.CopyText)?.action?.invoke()
                "cut" -> c.getOrNull(SemanticsActions.CutText)?.action?.invoke()
                "paste" -> c.getOrNull(SemanticsActions.PasteText)?.action?.invoke()
                "selectAll" -> c.getOrNull(SemanticsActions.SetSelection)?.action?.invoke(0, c.getOrNull(SemanticsProperties.EditableText)?.length ?: 0, false)
                else -> fail("invalid_argument", "Unsupported edit action")
            }
            else -> false
        }
        if (succeeded != true) fail("action_failed", "Control did not accept the action")
        return JSONObject().put("accepted", true)
    }
    fun back() {
        collect().asReversed().firstOrNull { it.visible && it.config.contains(SemanticsActions.Dismiss) }?.let {
            it.config[SemanticsActions.Dismiss].action?.invoke(); return
        }
        // Dialogs own a dispatcher distinct from the activity dispatcher.
        collect().asReversed().forEach { item ->
            var parent = item.root.view.parent
            while (parent != null) {
                val window = (parent as? DialogWindowProvider)?.window
                val dialog = window?.callback as? ComponentDialog
                if (dialog != null) { dialog.onBackPressedDispatcher.onBackPressed(); return }
                parent = parent.parent
            }
        }
        AndroidAutomation.currentActivity.onBackPressedDispatcher.onBackPressed()
    }
    private suspend fun frame() = suspendCancellableCoroutine<Unit> { continuation ->
        Choreographer.getInstance().postFrameCallback { if (continuation.isActive) continuation.resume(Unit) }
    }
}
