package com.harness.portable

import android.content.Intent
import android.graphics.Bitmap
import android.os.SystemClock
import android.view.accessibility.AccessibilityNodeInfo
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

/** Exercises the actual Compose settings UI in a dedicated QA application identity. */
@RunWith(AndroidJUnit4::class)
class SettingsTabsNativeTests {
    private val instrumentation get() = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private fun nodes(): List<AccessibilityNodeInfo> {
        val result = mutableListOf<AccessibilityNodeInfo>()
        fun visit(node: AccessibilityNodeInfo?) { if (node == null || result.size > 1500) return; result.add(node); for (i in 0 until node.childCount) visit(node.getChild(i)) }
        visit(instrumentation.uiAutomation.rootInActiveWindow); return result
    }
    private fun find(label: String): AccessibilityNodeInfo? = nodes().firstOrNull { it.text?.toString() == label || it.contentDescription?.toString() == label || it.hintText?.toString() == label }
    private fun waitFor(label: String): AccessibilityNodeInfo {
        val end = SystemClock.uptimeMillis() + 15000
        while (SystemClock.uptimeMillis() < end) { find(label)?.let { return it }; SystemClock.sleep(100) }
        error("Missing UI label: $label")
    }
    private fun click(label: String) {
        if (label in listOf("通用", "数据与安全", "WebDAV 同步", "更新与关于")) {
            repeat(8) {
                if (find(label) == null) {
                    val horizontal = nodes().firstOrNull { it.isScrollable && (it.className?.toString() == "android.widget.HorizontalScrollView" || it.actionList.any { action -> action.id == AccessibilityNodeInfo.AccessibilityAction.ACTION_SCROLL_LEFT.id }) }
                        ?: error("Missing horizontal category scroller for $label")
                    assertTrue(horizontal.performAction(if (label == "通用") AccessibilityNodeInfo.ACTION_SCROLL_BACKWARD else AccessibilityNodeInfo.ACTION_SCROLL_FORWARD))
                    instrumentation.waitForIdleSync(); SystemClock.sleep(200)
                }
            }
        }
        var node = waitFor(label)
        while (!node.isClickable) node = node.parent ?: error("No clickable ancestor for $label")
        assertTrue(node.performAction(AccessibilityNodeInfo.ACTION_CLICK)); instrumentation.waitForIdleSync(); SystemClock.sleep(350)
    }
    private fun capture(name: String) {
        instrumentation.waitForIdleSync(); SystemClock.sleep(350)
        val bitmap = instrumentation.uiAutomation.takeScreenshot() ?: error("No rendered screenshot")
        val output = File(context.filesDir, "qa-shots").apply { mkdirs() }
        File(output, "$name.png").outputStream().use { assertTrue(bitmap.compress(Bitmap.CompressFormat.PNG, 100, it)) }; bitmap.recycle()
    }
    @Test fun settingsCategoriesKeepTheirContentSeparateInLightAndDarkAppearance() {
        check(context.packageName.endsWith(".syncqa")) { "Only run this UI test in the isolated syncqa package" }
        context.getSharedPreferences("harness_portable", 0).edit().clear().commit()
        context.startActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK))
        click("设置"); waitFor("外观"); click("浅色"); capture("general-light")
        click("数据与安全"); waitFor("管理连接"); assertNull(find("进入服务后全屏")); capture("security")
        click("WebDAV 同步"); waitFor("WebDAV 私有目录"); assertNull(find("管理连接")); capture("sync-light")
        click("更新与关于"); waitFor("检查更新"); assertNull(find("WebDAV 私有目录")); capture("updates")
        click("通用"); waitFor("外观"); click("深色")
        click("WebDAV 同步"); waitFor("WebDAV 私有目录"); capture("sync-dark")
        assertEquals("dark", context.getSharedPreferences("harness_portable", 0).getString(UiPreferences.APPEARANCE, ""))
    }
}
