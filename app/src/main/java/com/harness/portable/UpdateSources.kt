package com.harness.portable

import android.content.Context

enum class UpdateSourceKind { File, GitHub, Gitee }

fun UpdateSourceKind.label(): String = when (this) {
    UpdateSourceKind.File -> "家庭服务器"
    UpdateSourceKind.GitHub -> "GitHub"
    UpdateSourceKind.Gitee -> "Gitee"
}

/** All addresses survive switching between sources. */
data class UpdateSources(
    val home: String,
    val github: String,
    val selected: UpdateSourceKind,
    val gitee: String = ApkUpdate.DEFAULT_SERVER
) {
    fun url(kind: UpdateSourceKind = selected): String = when (kind) {
        UpdateSourceKind.File -> home
        UpdateSourceKind.GitHub -> github
        UpdateSourceKind.Gitee -> gitee
    }.trim()

    fun withUrl(kind: UpdateSourceKind, value: String): UpdateSources = when (kind) {
        UpdateSourceKind.File -> copy(home = value)
        UpdateSourceKind.GitHub -> copy(github = value)
        UpdateSourceKind.Gitee -> copy(gitee = value)
    }

    fun feedUrl(kind: UpdateSourceKind = selected): String = ApkUpdate.feedUrl(url(kind))

    fun selectedLooksUsable(): Boolean = when (selected) {
        UpdateSourceKind.GitHub -> ApkUpdate.looksLikeGitHub(url())
        UpdateSourceKind.Gitee -> ApkUpdate.looksLikeGitee(url())
        UpdateSourceKind.File -> if (SmbUpdateAddress.looksLikeSmb(url())) {
            runCatching { SmbUpdateAddress.parse(url()) }.isSuccess
        } else runCatching {
            val uri = java.net.URI(url())
            uri.scheme?.lowercase() in listOf("http", "https") && !uri.host.isNullOrBlank()
        }.getOrDefault(false)
    }

    /** Only used once when the Gitee slot is first introduced. */
    fun migrateOfficialSource(): UpdateSources =
        if (selected == UpdateSourceKind.GitHub && ApkUpdate.isOfficialGitHub(github))
            copy(selected = UpdateSourceKind.Gitee) else this
}

object UpdateSourceStore {
    const val PREF_HOME = "update_source_home"
    const val PREF_GITHUB = "update_source_github"
    const val PREF_GITEE = "update_source_gitee"
    const val PREF_SELECTED = "update_source_selected"
    const val DEFAULT_HOME = ApkUpdate.DEFAULT_HOME_DIR
    val DEFAULT_SELECTED = UpdateSourceKind.Gitee

    fun load(ctx: Context): UpdateSources {
        val p = ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE)
        val sources = UpdateSources(
            home = p.getString(PREF_HOME, "")?.trim().orEmpty().ifEmpty { DEFAULT_HOME },
            github = p.getString(PREF_GITHUB, "")?.trim().orEmpty().ifEmpty { ApkUpdate.DEFAULT_GITHUB },
            selected = kindFromName(p.getString(PREF_SELECTED, null)) ?: DEFAULT_SELECTED,
            gitee = p.getString(PREF_GITEE, "")?.trim().orEmpty().ifEmpty { ApkUpdate.DEFAULT_SERVER }
        )
        if (p.contains(PREF_GITEE)) return sources
        return sources.migrateOfficialSource().also { save(ctx, it) }
    }

    fun save(ctx: Context, sources: UpdateSources) {
        ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE).edit()
            .putString(PREF_HOME, sources.home.trim())
            .putString(PREF_GITHUB, sources.github.trim())
            .putString(PREF_GITEE, sources.gitee.trim())
            .putString(PREF_SELECTED, sources.selected.name)
            .apply()
    }

    fun select(ctx: Context, kind: UpdateSourceKind): UpdateSources = load(ctx).copy(selected = kind).also { save(ctx, it) }

    fun migrateLegacy(ctx: Context, legacy: String) {
        val url = legacy.trim()
        if (url.isEmpty()) return
        val p = ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE)
        if (listOf(PREF_HOME, PREF_GITHUB, PREF_GITEE).any { !p.getString(it, "").isNullOrEmpty() }) return
        val kind = ApkUpdate.classify(url)
        val sources = UpdateSources(DEFAULT_HOME, ApkUpdate.DEFAULT_GITHUB, kind).withUrl(kind, url)
        save(ctx, sources.migrateOfficialSource())
    }

    fun kindFromName(name: String?): UpdateSourceKind? = UpdateSourceKind.entries.firstOrNull { it.name == name }
}
