package com.harness.portable.automation

import androidx.activity.compose.setContent
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.collapse
import androidx.compose.ui.semantics.expand
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.unit.dp
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.harness.portable.MainActivity
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.net.Socket
import java.util.UUID

/** Actual TCP requests exercise native widgets and callbacks in an isolated application. */
@RunWith(AndroidJUnit4::class)
class AndroidAutomationNativeTests {
    private val token = "a".repeat(64)
    private var port = 0
    private class Model {
        var input by mutableStateOf(TextFieldValue(""))
        var checked by mutableStateOf(false)
        var progress by mutableStateOf(0.2f)
        var counter by mutableStateOf(0)
        var ime by mutableStateOf(0)
        var expanded by mutableStateOf(false)
        var dialog by mutableStateOf(false)
        var menu by mutableStateOf(false)
        var visible by mutableStateOf(false)
    }
    @OptIn(ExperimentalFoundationApi::class)
    private fun fixture(run: (Model) -> Unit) {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        check(context.packageName.endsWith(".syncqa")) { "Native automation tests require the isolated QA package" }
        val model = Model()
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            scenario.onActivity { activity ->
                activity.setContent {
                    MaterialTheme {
                        Column {
                            OutlinedTextField(model.input, { model.input = it }, label = { Text("Fixture credential") },
                                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done), keyboardActions = KeyboardActions(onDone = { model.ime++ }),
                                modifier = Modifier.automationId("Fixture.Credential").automationSensitive())
                            Checkbox(model.checked, { model.checked = it }, modifier = Modifier.automationId("Fixture.Check"))
                            Slider(model.progress, { model.progress = it }, modifier = Modifier.automationId("Fixture.Range"))
                            Text("Action", modifier = Modifier.automationId("Fixture.Action").combinedClickable(onClick = { model.counter++ }, onLongClick = { model.counter += 10 }))
                            Button(onClick = { model.counter++ }, enabled = false, modifier = Modifier.automationId("Fixture.Disabled")) { Text("Disabled") }
                            Button(onClick = { model.counter++ }, modifier = Modifier.automationId("Fixture.Hidden").automationVisible(false)) { Text("Hidden") }
                            Button(onClick = { model.counter++ }, modifier = Modifier.automationId("Fixture.Repeated", "a")) { Text("A") }
                            Button(onClick = { model.counter += 2 }, modifier = Modifier.automationId("Fixture.Repeated", "b")) { Text("B") }
                            Text("Expansion", modifier = Modifier.automationId("Fixture.Expansion").semantics {
                                stateDescription = if (model.expanded) "expanded" else "collapsed"
                                expand { model.expanded = true; true }; collapse { model.expanded = false; true }
                            })
                            Button(onClick = { model.dialog = true }, modifier = Modifier.automationId("Fixture.OpenDialog")) { Text("Open dialog") }
                            LazyColumn(Modifier.height(180.dp).automationId("Fixture.List")) {
                                items((0..99).toList()) { index -> Button(onClick = { model.counter = index }, modifier = Modifier.automationId("Fixture.Item", index.toString())) { Text("Item $index") } }
                            }
                        }
                        if (model.dialog) AutomationAlertDialog(onDismissRequest = { model.dialog = false }, modifier = Modifier.automationId("Fixture.Dialog"),
                            title = { Text("Fixture dialog") }, confirmButton = {
                                Column {
                                    Button(onClick = { model.menu = true }, modifier = Modifier.automationId("Fixture.Dialog.Menu.Open")) { Text("Menu") }
                                    AutomationDropdownMenu(model.menu, { model.menu = false }, Modifier.automationId("Fixture.Dialog.Menu")) { Text("Nested menu") }
                                    Button(onClick = { model.dialog = false }, modifier = Modifier.automationId("Fixture.Dialog.Close")) { Text("Close") }
                                }
                            })
                    }
                }
                port = AndroidAutomation.start(token, 0, UUID.randomUUID().toString()).getInt("port")
            }
            try { waitNode("Fixture.Credential"); run(model) }
            finally { scenario.onActivity { AndroidAutomation.stop() } }
        }
    }
    private fun rpc(method: String, params: JSONObject = JSONObject(), auth: String = token): JSONObject = Socket("127.0.0.1", port).use { socket ->
        socket.soTimeout = 10000
        socket.getOutputStream().write((JSONObject().put("jsonrpc", "2.0").put("id", "fixture").put("auth", auth).put("method", method).put("params", params).toString() + "\n").toByteArray())
        JSONObject(socket.getInputStream().bufferedReader().readLine())
    }
    private fun call(method: String, params: JSONObject = JSONObject()): Any {
        val reply = rpc(method, params); check(!reply.has("error")) { reply.optJSONObject("error")?.toString().orEmpty() }
        val result = reply.get("result")
        if (result !is JSONObject || !result.has("operationId") || result.has("state")) return result
        val id = result.getString("operationId"); val end = android.os.SystemClock.elapsedRealtime() + 10000
        while (android.os.SystemClock.elapsedRealtime() < end) {
            val job = rpc("operations.get", JSONObject().put("operationId", id)).getJSONObject("result")
            if (job.getString("state") == "returned") return job.get("result")
            if (job.getString("state") == "failed") return JSONObject().put("failure", job.getJSONObject("error").getString("code"))
            Thread.sleep(20)
        }
        error("Native operation timed out: $method")
    }
    private fun act(id: String, action: String, values: JSONObject = JSONObject()): Any = call("ui.act", values.put("target", JSONObject().put("automationId", id)).put("action", action))
    private fun waitNode(id: String): JSONObject {
        val end = android.os.SystemClock.elapsedRealtime() + 10000
        while (android.os.SystemClock.elapsedRealtime() < end) {
            val nodes = rpc("ui.find", JSONObject().put("selector", JSONObject().put("automationId", id))).getJSONArray("result")
            if (nodes.length() > 0) return nodes.getJSONObject(0)
            Thread.sleep(30)
        }
        error("Missing native control: $id")
    }
    @Test fun actualNativeTextSelectionImeAndWriteOnlySnapshots() = fixture { model ->
        act("Fixture.Credential", "setValue", JSONObject().put("value", "fixture-private-password"))
        waitNode("Fixture.Credential")
        assertEquals("fixture-private-password", model.input.text)
        val tree = rpc("ui.tree").getJSONArray("result").toString()
        assertFalse(tree.contains("fixture-private-password"))
        val field = waitNode("Fixture.Credential"); assertTrue(field.getBoolean("sensitive")); assertTrue(field.getBoolean("hasValue")); assertFalse(field.has("value"))
        assertTrue((act("Fixture.Credential", "selectText", JSONObject().put("start", 1).put("length", 3)) as JSONObject).optBoolean("accepted"))
        assertEquals(1, model.input.selection.start); assertEquals(4, model.input.selection.end)
        fun inputBecomes(expected: String) {
            val deadline = android.os.SystemClock.elapsedRealtime() + 10000
            while (model.input.text != expected && android.os.SystemClock.elapsedRealtime() < deadline) Thread.sleep(20)
            assertEquals(expected, model.input.text)
        }
        assertTrue((act("Fixture.Credential", "edit", JSONObject().put("value", "copy")) as JSONObject).optBoolean("accepted"))
        act("Fixture.Credential", "setValue", JSONObject().put("value", ""))
        act("Fixture.Credential", "edit", JSONObject().put("value", "paste")); inputBecomes("ixt")
        act("Fixture.Credential", "edit", JSONObject().put("value", "selectAll"))
        act("Fixture.Credential", "edit", JSONObject().put("value", "cut")); inputBecomes("")
        act("Fixture.Credential", "edit", JSONObject().put("value", "paste")); inputBecomes("ixt")
        act("Fixture.Credential", "focus"); act("Fixture.Credential", "ime"); assertEquals(1, model.ime)
    }
    @Test fun nativeToggleRangeLongClickAndExpansionUseActualCallbacks() = fixture { model ->
        act("Fixture.Check", "setChecked", JSONObject().put("value", true)); assertTrue(model.checked)
        act("Fixture.Check", "setChecked", JSONObject().put("value", false)); assertFalse(model.checked)
        assertFalse(waitNode("Fixture.Check").getBoolean("checked"))
        act("Fixture.Check", "setChecked", JSONObject().put("value", false)); assertFalse(model.checked)
        act("Fixture.Check", "setChecked", JSONObject().put("value", true)); assertTrue(model.checked)
        act("Fixture.Range", "setProgress", JSONObject().put("value", 0.8)); assertEquals(0.8f, model.progress, 0.001f)
        assertEquals("invalid_argument", (act("Fixture.Range", "setProgress", JSONObject().put("value", 20)) as JSONObject).getString("failure"))
        act("Fixture.Action", "invoke"); act("Fixture.Action", "longClick"); assertEquals(11, model.counter)
        act("Fixture.Expansion", "setExpanded", JSONObject().put("value", true)); assertTrue(model.expanded)
        act("Fixture.Expansion", "setExpanded", JSONObject().put("value", false)); assertFalse(model.expanded)
    }
    @Test fun hiddenDisabledAmbiguousAndModalControlsCannotBypassTheUi() = fixture { model ->
        assertEquals("unavailable", (act("Fixture.Disabled", "invoke") as JSONObject).getString("failure"))
        assertEquals("not_found", (act("Fixture.Hidden", "invoke") as JSONObject).getString("failure"))
        assertEquals("ambiguous", (act("Fixture.Repeated", "invoke") as JSONObject).getString("failure"))
        call("ui.act", JSONObject().put("target", JSONObject().put("automationId", "Fixture.Repeated").put("dataKey", "b")).put("action", "invoke")); assertEquals(2, model.counter)
        act("Fixture.OpenDialog", "invoke"); waitNode("Fixture.Dialog")
        assertEquals("unavailable", (act("Fixture.Action", "invoke") as JSONObject).getString("failure"))
        act("Fixture.Dialog.Menu.Open", "invoke"); waitNode("Fixture.Dialog.Menu")
        act("Fixture.Dialog.Menu", "dismiss"); assertFalse(model.menu)
        call("app.back"); assertFalse(model.dialog)
        assertEquals("android.permission.DUMP", InstrumentationRegistry.getInstrumentation().targetContext.packageManager.getReceiverInfo(
            android.content.ComponentName(InstrumentationRegistry.getInstrumentation().targetContext, AutomationReceiver::class.java), 0).permission)
    }
    @Test fun listRealizationAndSessionAuthenticationAreNativeAndBounded() = fixture { model ->
        assertEquals("unauthorized", rpc("api.describe", auth = "b".repeat(64)).getJSONObject("error").getString("code"))
        assertEquals("invalid_argument", rpc("ui.tree", JSONObject().put("includeHidden", "false")).getJSONObject("error").getString("code"))
        assertEquals("invalid_argument", rpc("api.describe", JSONObject().put("unknown", true)).getJSONObject("error").getString("code"))
        val activePort = port
        AndroidAutomationServer("c".repeat(64), 0).use { stale ->
            try {
                port = stale.port
                assertEquals("stale", rpc("api.describe", auth = "c".repeat(64)).getJSONObject("error").getString("code"))
            } finally { port = activePort }
        }
        act("Fixture.List", "realize", JSONObject().put("index", 80))
        val end = android.os.SystemClock.elapsedRealtime() + 10000
        while (android.os.SystemClock.elapsedRealtime() < end) {
            val found = rpc("ui.find", JSONObject().put("selector", JSONObject().put("automationId", "Fixture.Item").put("dataKey", "80"))).getJSONArray("result")
            if (found.length() > 0) break
            Thread.sleep(30)
        }
        call("ui.act", JSONObject().put("target", JSONObject().put("automationId", "Fixture.Item").put("dataKey", "80")).put("action", "invoke")); assertEquals(80, model.counter)
        val scrolled = act("Fixture.List", "scroll", JSONObject().put("vertical", -160)) as JSONObject
        assertTrue(scrolled.optBoolean("accepted"))
    }
}
