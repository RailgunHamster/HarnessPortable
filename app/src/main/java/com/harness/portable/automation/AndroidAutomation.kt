package com.harness.portable.automation

import android.app.Activity
import android.content.BroadcastReceiver
import android.content.Context
import android.content.ContextWrapper
import android.content.Intent
import android.os.Handler
import android.os.Looper
import androidx.activity.ComponentActivity
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import com.harness.portable.HostResolver
import com.harness.portable.ProfileStore
import com.harness.portable.SecureStore
import com.harness.portable.TunnelState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.lang.ref.WeakReference
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.FutureTask
import java.util.concurrent.TimeUnit

internal class AutomationFailure(val code: String, message: String) : Exception(message)
internal fun fail(code: String, message: String): Nothing = throw AutomationFailure(code, message)
internal fun JSONObject.requiredString(key: String): String = (opt(key) as? String) ?: fail("invalid_argument", "Required string: $key")
internal fun JSONObject.requiredBoolean(key: String): Boolean = (opt(key) as? Boolean) ?: fail("invalid_argument", "Required boolean: $key")
internal fun JSONObject.optionalBoolean(key: String, default: Boolean = false): Boolean = if (has(key)) requiredBoolean(key) else default

internal data class AndroidOperation(
    val name: String,
    val description: String,
    val parameters: Set<String> = emptySet(),
    val mutates: Boolean = true,
    val read: ((JSONObject) -> Any?)? = null,
    val run: suspend (JSONObject) -> Any?
)
internal fun AndroidReadOperation(name: String, description: String, read: (JSONObject) -> Any?) =
    AndroidOperation(name, description, mutates = false, read = read, run = { read(it) })

/** Native Android adapter. Ordinary launches never create a socket or enable automation. */
object AndroidAutomation {
    private val main = Handler(Looper.getMainLooper())
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var activity = WeakReference<ComponentActivity>(null)
    private var server: AndroidAutomationServer? = null
    private var sessionId = ""
    private data class Registered(val owner: Any, val operation: AndroidOperation)
    private val registered = linkedMapOf<String, Registered>()
    private val jobs = ConcurrentHashMap<String, JSONObject>()
    private val queued = ConcurrentHashMap<String, () -> Unit>()
    private val events = ArrayDeque<JSONObject>()
    private var eventSequence = 0L
    private val builtins = linkedMapOf(
        "api.describe" to emptySet(), "ui.tree" to setOf("includeHidden"), "ui.find" to setOf("selector"),
        "ui.actions" to emptySet(), "ui.act" to setOf("target", "action", "value", "start", "length", "horizontal", "vertical", "index"),
        "operations.get" to setOf("operationId"), "events.read" to setOf("after"),
        "app.back" to emptySet(), "app.show" to emptySet(), "app.exit" to emptySet(), "library.list" to emptySet(),
        "network.resolve" to setOf("host"), "network.status" to emptySet(), "dialogs.get" to emptySet(),
        "dialogs.choose" to setOf("id", "cancel", "fileIds"), "files.put" to setOf("name", "base64"),
        "files.get" to setOf("fileId"), "files.list" to emptySet()
    )
    val enabled: Boolean get() = server != null
    internal val currentActivity: ComponentActivity get() = activity.get()?.takeUnless { it.isDestroyed } ?: fail("unavailable", "Application window is unavailable")

    fun attach(activity: ComponentActivity) {
        if (this.activity.get() !== activity) { stop(); registered.clear() }
        this.activity = WeakReference(activity)
        AndroidUiAutomation.install()
    }
    fun detach(activity: Activity) {
        if (this.activity.get() === activity) { stop(); this.activity.clear() }
    }
    internal fun <T> onMain(block: () -> T): T {
        if (Looper.myLooper() == Looper.getMainLooper()) return block()
        val task = FutureTask(block); main.post(task)
        return task.get(15, TimeUnit.SECONDS)
    }
    internal fun start(token: String, port: Int, sessionId: String): JSONObject {
        check(Looper.myLooper() == Looper.getMainLooper())
        currentActivity
        if (!token.matches(Regex("[a-fA-F0-9]{64}")) || port !in 0..65535 || !sessionId.matches(Regex("[A-Za-z0-9_-]{1,80}"))) fail("invalid_argument", "Invalid automation session settings")
        if (server != null) fail("busy", "An automation session is already active")
        stop(); jobs.clear(); queued.clear(); events.clear(); eventSequence = 0
        this.sessionId = sessionId
        val next = AndroidAutomationServer(token, port); server = next
        return JSONObject().put("protocol", 1).put("platform", "android").put("port", next.port)
            .put("sessionId", sessionId).put("processId", android.os.Process.myPid()).put("packageName", currentActivity.packageName)
    }
    internal fun stop(expectedSession: String? = null) {
        if (server != null && expectedSession != null && expectedSession != sessionId) fail("stale", "Automation session has changed")
        server?.close(); server = null; sessionId = ""; queued.clear(); AutomationDialogs.reset()
    }
    internal fun belongsToCurrentActivity(context: Context): Boolean = when {
        context === activity.get() -> true
        context is ContextWrapper && context.baseContext !== context -> belongsToCurrentActivity(context.baseContext)
        else -> false
    }
    internal fun register(owner: Any, operations: List<AndroidOperation>) {
        operations.forEach { value ->
            val prior = registered[value.name]
            check(prior == null || prior.owner === owner) { "Duplicate application operation: ${value.name}" }
            registered[value.name] = Registered(owner, value)
        }
    }
    internal fun unregister(owner: Any) { registered.entries.removeAll { it.value.owner === owner } }
    internal fun event(name: String, data: JSONObject = JSONObject()) {
        events.addLast(JSONObject().put("sequence", ++eventSequence).put("name", name).put("data", data))
        while (events.size > 200) events.removeFirst()
    }

    internal fun dispatch(method: String, args: JSONObject): Any? = onMain {
        builtins[method]?.let { allowed -> if (args.keys().asSequence().any { it !in allowed }) fail("invalid_argument", "Unknown operation argument") }
        when (method) {
            "api.describe" -> describe()
            "ui.tree" -> AndroidUiAutomation.tree(args.optionalBoolean("includeHidden"))
            "ui.find" -> AndroidUiAutomation.find(args.optJSONObject("selector") ?: fail("invalid_argument", "selector is required"))
            "ui.actions" -> AndroidUiAutomation.actionCatalog()
            "ui.act" -> enqueue(method, args) { AndroidUiAutomation.act(it) }
            "operations.get" -> jobs[args.requiredString("operationId")] ?: fail("not_found", "Operation is unavailable")
            "events.read" -> JSONArray(events.filter { it.getLong("sequence") > args.optLong("after", 0) })
            "app.back" -> enqueue(method, args) { AndroidUiAutomation.back(); true }
            "app.show" -> enqueue(method, args) {
                val window = currentActivity
                window.startActivity(Intent(window, window.javaClass).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT or Intent.FLAG_ACTIVITY_SINGLE_TOP)); true
            }
            "app.exit" -> enqueue(method, args) { main.postDelayed({ currentActivity.finishAndRemoveTask() }, 120); true }
            "library.list" -> library(currentActivity)
            "network.status" -> {
                val manager = currentActivity.getSystemService(Context.CONNECTIVITY_SERVICE) as android.net.ConnectivityManager
                val network = manager.activeNetwork; val properties = network?.let(manager::getLinkProperties)
                val vpn = manager.allNetworks.any { manager.getNetworkCapabilities(it)?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_VPN) == true }
                JSONObject().put("connected", network != null).put("vpnActive", vpn).put("interface", properties?.interfaceName ?: JSONObject.NULL)
                    .put("dnsServers", JSONArray(properties?.dnsServers?.mapNotNull { it.hostAddress }.orEmpty())).put("searchDomains", properties?.domains ?: JSONObject.NULL)
            }
            "network.resolve" -> enqueue(method, args) {
                val host = it.requiredString("host").trim(); if (host.isEmpty() || host.length > 253 || host.any { ch -> ch.isWhitespace() || ch.isISOControl() }) fail("invalid_argument", "Invalid host name")
                withContext(Dispatchers.IO) {
                    HostResolver.invalidate(host)
                    val result = HostResolver.resolve(host)
                    JSONObject().put("host", host).put("resolved", result != null).put("address", result?.ip ?: JSONObject.NULL).put("source", result?.source ?: JSONObject.NULL)
                }
            }
            "dialogs.get" -> AutomationDialogs.status()
            "dialogs.choose" -> enqueue(method, args) { AutomationDialogs.choose(it); true }
            "files.put" -> AutomationDialogs.putFile(args)
            "files.get" -> AutomationDialogs.getFile(args)
            "files.list" -> AutomationDialogs.listFiles()
            else -> {
                val operation = registered[method]?.operation ?: fail("method_not_found", "Operation is not available in this application state")
                if (args.keys().asSequence().any { it !in operation.parameters }) fail("invalid_argument", "Unknown operation argument")
                if (operation.read != null) operation.read.invoke(args) else enqueue(method, args, operation.run)
            }
        }
    }
    private fun enqueue(method: String, args: JSONObject, run: suspend (JSONObject) -> Any?): JSONObject {
        if (jobs.size >= 200) jobs.entries.firstOrNull { it.value.optString("state") in listOf("returned", "failed") }?.let { jobs.remove(it.key) }
        if (jobs.size >= 200) fail("busy", "Too many pending automation operations")
        val id = UUID.randomUUID().toString()
        val jobSession = sessionId
        jobs[id] = JSONObject().put("operationId", id).put("method", method).put("state", "queued")
        // Queuing preserves the acknowledgement-before-exit rule and releases the UI thread.
        queued[id] = { main.post { scope.launch {
            if (jobSession != sessionId || !enabled) return@launch
            jobs[id] = JSONObject().put("operationId", id).put("method", method).put("state", "running")
            try {
                val result = run(args)
                if (jobSession != sessionId) return@launch
                jobs[id] = JSONObject().put("operationId", id).put("method", method).put("state", "returned").put("result", result ?: JSONObject.NULL)
                event("operation.returned", JSONObject().put("operationId", id).put("method", method))
            } catch (e: Exception) {
                if (jobSession != sessionId) return@launch
                val failure = e as? AutomationFailure
                jobs[id] = JSONObject().put("operationId", id).put("method", method).put("state", "failed")
                    .put("error", JSONObject().put("code", failure?.code ?: "operation_failed").put("message", failure?.message ?: "Application operation failed; inspect application status"))
                event("operation.failed", JSONObject().put("operationId", id).put("method", method))
            }
        } } }
        return JSONObject().put("operationId", id)
    }
    /** Called by the transport only after the acknowledgement has been flushed. */
    internal fun afterResponse(result: Any?) { (result as? JSONObject)?.optString("operationId")?.let { queued.remove(it)?.invoke() } }
    private fun describe(): JSONObject {
        val methods = JSONArray()
        builtins.forEach { (name, parameters) -> methods.put(JSONObject().put("name", name).put("parameters", JSONArray(parameters.toList()))) }
        registered.values.forEach { methods.put(JSONObject().put("name", it.operation.name).put("description", it.operation.description).put("parameters", JSONArray(it.operation.parameters.toList())).put("mutates", it.operation.mutates)) }
        return JSONObject().put("protocol", 1).put("platform", "android").put("methods", methods).put("writeOnlyCredentials", true)
    }
    internal fun library(context: Context): JSONArray {
        val items = JSONArray()
        ProfileStore.loadTunnels(context).forEach { p -> items.put(JSONObject().put("key", "tunnel:" + p.id).put("id", p.id).put("name", p.name).put("sshHost", p.sshHost).put("sshPort", p.sshPort).put("user", p.user).put("remoteHost", p.remoteHost).put("remotePort", p.remotePort).put("localPort", p.localPort).put("authMode", p.authMode).put("hasPassword", SecureStore.hasPassword(context, p.id)).put("hasAuthInput", SecureStore.hasAuthInput(context, p.id))) }
        ProfileStore.loadDirect(context).forEach { url -> items.put(JSONObject().put("key", directKey(url)).put("url", publicUrl(url)).put("kind", "direct")) }
        return items
    }
    fun directKey(url: String) = "direct:" + MessageDigest.getInstance("SHA-256").digest(url.toByteArray()).joinToString("") { "%02x".format(it) }
    fun dataKey(key: String) = if (key.startsWith("direct:")) directKey(key.removePrefix("direct:")) else key
    fun publicUrl(url: String): String = runCatching {
        val u = java.net.URI(url); java.net.URI(u.scheme, null, u.host, u.port, u.path, null, null).toASCIIString()
    }.getOrDefault("")
    internal fun tunnelState(): JSONObject = TunnelState.flow.value.let { JSONObject().put("profileId", it.profileId ?: JSONObject.NULL).put("status", it.status.name.lowercase()).put("message", it.message ?: JSONObject.NULL).put("localPort", it.localPort) }
}

/** Entry point is protected by Android's signature/development DUMP permission (ADB shell). */
class AutomationReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        try {
            when (intent.getStringExtra("command")) {
                "start" -> {
                    val descriptor = AndroidAutomation.start(intent.getStringExtra("token") ?: "", intent.getIntExtra("port", 0), intent.getStringExtra("sessionId") ?: "")
                    resultCode = 0; resultData = descriptor.toString()
                }
                "stop" -> { AndroidAutomation.stop(intent.getStringExtra("sessionId")); resultCode = 0; resultData = "stopped" }
                else -> { resultCode = 1; resultData = "Unsupported automation command" }
            }
        } catch (_: Exception) { resultCode = 1; resultData = "Unable to start automation; open the application and check the session arguments" }
    }
}

@Composable
internal fun AutomationOperations(vararg operations: AndroidOperation) {
    val owner = remember { Any() }
    val context = LocalContext.current
    SideEffect { if (AndroidAutomation.belongsToCurrentActivity(context)) AndroidAutomation.register(owner, operations.toList()) }
    DisposableEffect(owner) { onDispose { AndroidAutomation.unregister(owner) } }
}
