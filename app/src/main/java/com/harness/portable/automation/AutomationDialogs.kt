package com.harness.portable.automation

import android.net.Uri
import android.util.Base64
import androidx.core.content.FileProvider
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID

/** API sessions own pending file choices; ordinary launches retain Android's native picker. */
object AutomationDialogs {
    private data class Choice(val id: String, val kind: String, val types: Array<String>, val multiple: Boolean, val name: String?, val complete: (Array<Uri>?) -> Unit)
    private var pending: Choice? = null
    private val files = linkedMapOf<String, File>()
    private val exported = mutableSetOf<String>()
    private fun directory() = File(AndroidAutomation.currentActivity.cacheDir, "automation").apply { mkdirs() }
    private fun uri(file: File) = FileProvider.getUriForFile(AndroidAutomation.currentActivity, AndroidAutomation.currentActivity.packageName + ".fileprovider", file)
    fun open(types: Array<String>, multiple: Boolean = false, complete: (Array<Uri>?) -> Unit): Boolean {
        if (!AndroidAutomation.enabled) return false
        cancelPending(); pending = Choice(UUID.randomUUID().toString(), "open", types, multiple, null, complete)
        AndroidAutomation.event("dialog.opened", status()); return true
    }
    fun create(name: String, complete: (Uri?) -> Unit): Boolean {
        if (!AndroidAutomation.enabled) return false
        cancelPending(); pending = Choice(UUID.randomUUID().toString(), "create", arrayOf("application/json"), false, name) { complete(it?.singleOrNull()) }
        AndroidAutomation.event("dialog.opened", status()); return true
    }
    internal fun status(): JSONObject = pending?.let { JSONObject().put("id", it.id).put("kind", it.kind).put("mimeTypes", JSONArray(it.types.toList())).put("multiple", it.multiple).put("suggestedName", it.name ?: JSONObject.NULL) } ?: JSONObject().put("pending", false)
    internal fun cancelPending() { val old = pending; pending = null; old?.complete?.invoke(null) }
    internal fun choose(args: JSONObject) {
        val choice = pending ?: fail("not_found", "No file dialog is pending")
        if (args.requiredString("id") != choice.id) fail("stale", "File dialog has changed")
        if (args.optionalBoolean("cancel")) { cancelPending(); return }
        val selected = if (choice.kind == "create") {
            if (files.size >= 100) fail("limit_exceeded", "Automation file limit exceeded")
            val id = UUID.randomUUID().toString(); val file = File(directory(), id + ".json"); file.createNewFile(); files[id] = file; exported.add(id)
            arrayOf(uri(file))
        } else {
            val ids = args.optJSONArray("fileIds") ?: fail("invalid_argument", "fileIds is required")
            if (ids.length() == 0 || !choice.multiple && ids.length() != 1 || ids.length() > 20) fail("invalid_argument", "Invalid file count")
            Array(ids.length()) { uri(files[ids.getString(it)] ?: fail("not_found", "Unknown automation file")) }
        }
        pending = null; choice.complete(selected)
    }
    internal fun putFile(args: JSONObject): JSONObject {
        val bytes = runCatching { Base64.decode(args.requiredString("base64"), Base64.DEFAULT) }.getOrElse { fail("invalid_argument", "Invalid file encoding") }
        if (bytes.size > 512 * 1024 || files.size >= 100) fail("limit_exceeded", "Automation file limit exceeded")
        val name = if (args.has("name")) args.requiredString("name") else "fixture.bin"
        if (name.length !in 1..80 || name.toByteArray(Charsets.UTF_8).size > 180 || name.any { it.isISOControl() || it == '/' || it == '\\' } || name in listOf(".", "..")) fail("invalid_argument", "Invalid file name")
        val id = UUID.randomUUID().toString()
        val folder = File(directory(), id).apply { check(mkdirs()) }
        val file = File(folder, name); file.writeBytes(bytes); files[id] = file
        return JSONObject().put("fileId", id).put("size", bytes.size)
    }
    internal fun getFile(args: JSONObject): JSONObject {
        val id = args.requiredString("fileId"); val file = files[id] ?: fail("not_found", "Unknown automation file")
        if (id !in exported) fail("write_only", "Uploaded files, including private keys, are write-only")
        if (file.length() > 512 * 1024) fail("limit_exceeded", "Automation file exceeds the response limit")
        return JSONObject().put("fileId", id).put("base64", Base64.encodeToString(file.readBytes(), Base64.NO_WRAP))
    }
    internal fun listFiles() = JSONArray(files.map { JSONObject().put("fileId", it.key).put("name", it.value.name).put("size", it.value.length()) })
    internal fun reset() {
        cancelPending()
        files.forEach { (id, file) -> file.delete(); file.parentFile?.takeIf { it.name == id }?.delete() }
        files.clear(); exported.clear()
    }
}
