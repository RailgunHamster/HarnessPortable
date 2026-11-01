package com.harness.portable

import android.content.Context

enum class UpdateSourceKind { CNB, GitHub }
fun UpdateSourceKind.label(): String = when(this) { UpdateSourceKind.CNB -> "CNB"; UpdateSourceKind.GitHub -> "GitHub" }

data class UpdateSources(
    val cnb: String = ApkUpdate.DEFAULT_SERVER,
    val github: String = ApkUpdate.DEFAULT_GITHUB,
    val selected: UpdateSourceKind = UpdateSourceKind.CNB
) {
    fun url(kind: UpdateSourceKind = selected): String = (if(kind == UpdateSourceKind.GitHub) github else cnb).trim()
    fun withUrl(kind: UpdateSourceKind, value: String): UpdateSources = if(kind == UpdateSourceKind.GitHub) copy(github=value) else copy(cnb=value)
    fun feedUrl(kind: UpdateSourceKind = selected): String = ApkUpdate.feedUrl(url(kind))
    fun selectedLooksUsable(): Boolean = if(selected == UpdateSourceKind.GitHub) ApkUpdate.looksLikeGitHub(url()) else ApkUpdate.looksLikeCnb(url())
    fun migrateOfficialSource(): UpdateSources = if(selected == UpdateSourceKind.GitHub && ApkUpdate.isOfficialGitHub(github)) copy(selected=UpdateSourceKind.CNB) else this
}

object UpdateSourceStore {
    const val PREF_CNB = "update_source_cnb"
    const val PREF_GITHUB = "update_source_github"
    const val PREF_SELECTED = "update_source_selected"
    val DEFAULT_SELECTED = UpdateSourceKind.CNB
    fun load(ctx: Context): UpdateSources {
        val p=ctx.getSharedPreferences("harness_portable",Context.MODE_PRIVATE)
        val sources=UpdateSources(
            cnb=p.getString(PREF_CNB,"")?.trim().orEmpty().ifEmpty { ApkUpdate.DEFAULT_SERVER },
            github=p.getString(PREF_GITHUB,"")?.trim().orEmpty().ifEmpty { ApkUpdate.DEFAULT_GITHUB },
            selected=kindFromName(p.getString(PREF_SELECTED,null)) ?: DEFAULT_SELECTED)
        return (if(p.contains(PREF_CNB)) sources else sources.migrateOfficialSource()).also { if(!p.contains(PREF_CNB)) save(ctx,it) }
    }
    fun save(ctx: Context,sources: UpdateSources) {
        ctx.getSharedPreferences("harness_portable",Context.MODE_PRIVATE).edit()
            .putString(PREF_CNB,sources.cnb.trim()).putString(PREF_GITHUB,sources.github.trim()).putString(PREF_SELECTED,sources.selected.name)
            .remove("update_source_home").remove("update_source_gitee").remove("update_share_account").remove(ApkUpdate.PREF_SERVER).apply()
    }
    fun select(ctx: Context,kind: UpdateSourceKind): UpdateSources = load(ctx).copy(selected=kind).also { save(ctx,it) }
    fun migrateLegacy(ctx: Context,legacy: String) {
        val p=ctx.getSharedPreferences("harness_portable",Context.MODE_PRIVATE)
        if(p.contains(PREF_CNB) || p.contains(PREF_GITHUB)) return
        val sources=if(ApkUpdate.looksLikeGitHub(legacy)) UpdateSources(github=legacy,selected=UpdateSourceKind.GitHub).migrateOfficialSource() else UpdateSources()
        save(ctx,sources)
    }
    fun kindFromName(name: String?): UpdateSourceKind? = UpdateSourceKind.entries.firstOrNull { it.name.equals(name,true) }
}
