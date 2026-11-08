package com.harness.portable

import android.content.Intent
import android.graphics.Bitmap
import android.os.SystemClock
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.harness.portable.automation.AndroidAutomation
import com.harness.portable.automation.AutomationDataKey
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

@RunWith(AndroidJUnit4::class)
class LibraryOrderNativeTests {
    @get:Rule val compose = createEmptyComposeRule()
    private val instrumentation get() = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private fun control(id: String, key: String? = null): SemanticsNodeInteraction {
        val matcher = hasTestTag(id).let { if (key == null) it else it and SemanticsMatcher.expectValue(AutomationDataKey, AndroidAutomation.dataKey(key)) }
        compose.waitUntil(15000) {
            try { compose.onAllNodes(matcher, useUnmergedTree = true).fetchSemanticsNodes().isNotEmpty() }
            catch (e: IllegalStateException) { if (e.message?.startsWith("No compose hierarchies found") == true) false else throw e }
        }
        return compose.onNode(matcher, useUnmergedTree = true)
    }
    private fun capture(name: String) {
        compose.waitForIdle(); instrumentation.waitForIdleSync(); SystemClock.sleep(300)
        val bitmap = instrumentation.uiAutomation.takeScreenshot() ?: error("No rendered screenshot")
        val output = File(context.filesDir, "qa-shots").apply { mkdirs() }
        File(output, "$name.png").outputStream().use { assertTrue(bitmap.compress(Bitmap.CompressFormat.PNG, 100, it)) }; bitmap.recycle()
    }
    @Test fun mixedLibraryOrderSavesCancelsScrollsAndSurvivesActivityRecreation() {
        check(context.packageName.endsWith(".syncqa"))
        UiPreferences.prefs(context).edit().clear().commit()
        val first = TunnelProfile(id = "order-first", name = "开发服务器 · 一段很长的连接名称用于验证窄屏截断", sshHost = "fixture.invalid", user = "fixture")
        ProfileStore.saveTunnels(context, listOf(first, first.copy(id = "order-second", name = "测试服务器")))
        ProfileStore.saveDirect(context, (1..7).map { "https://fixture.invalid/console/$it" })
        val original = ConnectionOrdering.load(context)
        context.startActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK))
        control("Library.Order").performClick()
        control("Library.Order.Up", original.first()).assertIsNotEnabled()
        control("Library.Order.Save").assertIsNotEnabled(); capture("order-light")
        control("Library.Order.Down", original.first()).performClick()
        control("Library.Order.Cancel").performClick()
        assertEquals(original, ConnectionOrdering.load(context))
        control("Library.Order").performClick(); control("Library.Order.Down", original.first()).performClick()
        control("Library.Order.Save").performClick()
        val expected = original.toMutableList().apply { add(1, removeAt(0)) }
        assertEquals(expected, ConnectionOrdering.load(context))
        control("Library.Order").performClick()
        control("Library.Order.Items").performScrollToIndex(original.lastIndex)
        control("Library.Order.Down", original.last()).assertIsNotEnabled()
        control("Library.Order.Cancel").performClick()
        UiPreferences.setAppearance(context, "dark")
        context.startActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK))
        control("Library.Order").performClick(); capture("order-dark")
        assertEquals(expected, ConnectionOrdering.load(context))
        control("Library.Order.Cancel").performClick()
    }
}
