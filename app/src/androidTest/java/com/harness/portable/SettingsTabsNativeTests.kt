package com.harness.portable

import android.content.Intent
import android.graphics.Bitmap
import android.os.SystemClock
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

/** Exercises the actual Compose settings UI in a dedicated QA application identity. */
@RunWith(AndroidJUnit4::class)
class SettingsTabsNativeTests {
    @get:Rule val compose = createEmptyComposeRule()
    private val instrumentation get() = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private fun waitFor(label: String) {
        compose.waitUntil(15000) {
            compose.onAllNodes(hasText(label) or hasContentDescription(label), useUnmergedTree = true).fetchSemanticsNodes().isNotEmpty()
        }
    }
    private fun click(label: String) {
        waitFor(label)
        val control = compose.onNode((hasText(label) or hasContentDescription(label)) and hasClickAction())
        if (label in listOf("通用", "数据与安全", "WebDAV 同步", "更新与关于")) {
            control.performScrollTo()
        }
        control.performClick(); compose.waitForIdle()
    }
    private fun capture(name: String) {
        compose.waitForIdle(); instrumentation.waitForIdleSync(); SystemClock.sleep(350)
        val bitmap = instrumentation.uiAutomation.takeScreenshot() ?: error("No rendered screenshot")
        val output = File(context.filesDir, "qa-shots").apply { mkdirs() }
        File(output, "$name.png").outputStream().use { assertTrue(bitmap.compress(Bitmap.CompressFormat.PNG, 100, it)) }; bitmap.recycle()
    }
    @Test fun settingsCategoriesKeepTheirContentSeparateInLightAndDarkAppearance() {
        check(context.packageName.endsWith(".syncqa")) { "Only run this UI test in the isolated syncqa package" }
        context.getSharedPreferences("harness_portable", 0).edit().clear().commit()
        context.startActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK))
        click("设置"); waitFor("外观"); click("浅色"); capture("general-light")
        click("数据与安全"); waitFor("管理连接"); compose.onAllNodes(hasText("进入服务后全屏"), useUnmergedTree = true).assertCountEquals(0); capture("security")
        click("WebDAV 同步"); waitFor("WebDAV 私有目录"); compose.onAllNodes(hasText("管理连接"), useUnmergedTree = true).assertCountEquals(0); capture("sync-light")
        click("更新与关于"); waitFor("检查更新"); compose.onAllNodes(hasText("WebDAV 私有目录"), useUnmergedTree = true).assertCountEquals(0); capture("updates")
        click("通用"); waitFor("外观"); click("深色")
        click("WebDAV 同步"); waitFor("WebDAV 私有目录"); capture("sync-dark")
        assertEquals("dark", context.getSharedPreferences("harness_portable", 0).getString(UiPreferences.APPEARANCE, ""))
    }
}
