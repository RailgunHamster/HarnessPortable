package com.harness.portable

import android.content.Context

/**
 * Which of the two known update feeds a source points at.
 *
 * [File] covers a directory path — a Windows UNC share such as
 * `\\server-home\public\Software\HarnessPortable-Releases`, or a plain local
 * path. Only a PC can read one of those, so on Android it is offered but never
 * the default.
 */
enum class UpdateSourceKind { File, GitHub }

/** Short name used on the source picker. */
fun UpdateSourceKind.label(): String = when (this) {
    UpdateSourceKind.File -> "家庭目录"
    UpdateSourceKind.GitHub -> "GitHub"
}

/**
 * A selectable place to fetch `android.json` from.
 *
 * Two are kept at once — the home directory and the GitHub repository — so
 * switching between them never loses the other address. [selected] only
 * chooses which one a check uses.
 */
data class UpdateSources(
    val home: String,
    val github: String,
    val selected: UpdateSourceKind
) {
    fun url(kind: UpdateSourceKind = selected): String =
        (if (kind == UpdateSourceKind.GitHub) github else home).trim()

    /** The feed address a check would actually request. */
    fun feedUrl(kind: UpdateSourceKind = selected): String =
        ApkUpdate.feedUrl(url(kind))

    /** True when the selected source is usable on this platform. */
    fun selectedLooksUsable(): Boolean =
        selected != UpdateSourceKind.File || ApkUpdate.looksLikeReadablePath(url())
}

object UpdateSourceStore {
    const val PREF_HOME = "update_source_home"
    const val PREF_GITHUB = "update_source_github"
    const val PREF_SELECTED = "update_source_selected"

    /** The Windows PC reaches the LAN share by its UNC path. */
    const val DEFAULT_HOME = ApkUpdate.DEFAULT_HOME_DIR

    /**
     * Phones cannot read a UNC path, so the default is GitHub; the home
     * directory is still pre-filled for anyone running an http mirror.
     */
    val DEFAULT_SELECTED = UpdateSourceKind.GitHub

    fun load(ctx: Context): UpdateSources {
        val p = ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE)
        val home = p.getString(PREF_HOME, "")?.trim().orEmpty().ifEmpty { DEFAULT_HOME }
        val github = p.getString(PREF_GITHUB, "")?.trim().orEmpty().ifEmpty { ApkUpdate.DEFAULT_SERVER }
        val selected = kindFromName(p.getString(PREF_SELECTED, null)) ?: DEFAULT_SELECTED
        return UpdateSources(home, github, selected)
    }

    fun save(ctx: Context, sources: UpdateSources) {
        ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE)
            .edit()
            .putString(PREF_HOME, sources.home.trim())
            .putString(PREF_GITHUB, sources.github.trim())
            .putString(PREF_SELECTED, sources.selected.name)
            .apply()
    }

    /**
     * One-time import of the single-source setting that predates the two-slot
     * model, so an existing install keeps pointing where the user aimed it.
     */
    fun migrateLegacy(ctx: Context, legacy: String) {
        val url = legacy.trim()
        if (url.isEmpty()) return
        val p = ctx.getSharedPreferences("harness_portable", Context.MODE_PRIVATE)
        if (!p.getString(PREF_HOME, "").isNullOrEmpty() || !p.getString(PREF_GITHUB, "").isNullOrEmpty()) {
            return
        }
        val kind = ApkUpdate.classify(url)
        save(
            ctx,
            UpdateSources(
                home = if (kind == UpdateSourceKind.File) url else DEFAULT_HOME,
                github = if (kind == UpdateSourceKind.GitHub) url else ApkUpdate.DEFAULT_SERVER,
                selected = kind
            )
        )
    }

    fun kindFromName(name: String?): UpdateSourceKind? = when (name) {
        UpdateSourceKind.File.name -> UpdateSourceKind.File
        UpdateSourceKind.GitHub.name -> UpdateSourceKind.GitHub
        else -> null
    }
}
