package com.harness.portable

import org.junit.Assert.*
import org.junit.Test

class GiteeUpdateTests {
    @Test fun defaultFeedUsesPublicStaticIndexAndKeepsGitHubAvailable() {
        assertEquals(UpdateSourceKind.Gitee, UpdateSourceStore.DEFAULT_SELECTED)
        assertEquals("https://gitee.com/railgunhamster/HarnessPortable/raw/updates/android.json", ApkUpdate.feedUrl(""))
        assertEquals("https://gitee.com/railgunhamster/HarnessPortable/raw/updates/android.json", ApkUpdate.feedUrl(ApkUpdate.DEFAULT_SERVER + ".git"))
        assertEquals("https://github.com/RailgunHamster/HarnessPortable/releases/latest/download/android.json", ApkUpdate.feedUrl(ApkUpdate.DEFAULT_GITHUB))
        val apk = "https://gitee.com/railgunhamster/HarnessPortable/releases/download/v2.7.9/HarnessPortable-2.7.9.apk"
        assertEquals(apk, ApkUpdate.apkUrl(ApkUpdate.feedUrl(""), apk))
    }

    @Test fun officialGitHubMigratesButHomeAndCustomSourcesRemain() {
        val original = UpdateSources(UpdateSourceStore.DEFAULT_HOME, ApkUpdate.DEFAULT_GITHUB, UpdateSourceKind.GitHub)
        assertEquals(UpdateSourceKind.Gitee, original.migrateOfficialSource().selected)
        assertEquals(UpdateSourceKind.File, original.copy(selected = UpdateSourceKind.File).migrateOfficialSource().selected)
        assertEquals(UpdateSourceKind.GitHub, original.copy(github = "https://github.com/example/custom").migrateOfficialSource().selected)
        assertTrue(original.copy(selected = UpdateSourceKind.Gitee).selectedLooksUsable())
        assertFalse(original.copy(selected = UpdateSourceKind.Gitee, gitee = "https://gitee.com").selectedLooksUsable())
        assertFalse(ApkUpdate.looksLikeGitee("https://gitee.com.evil.example/owner/repo"))
    }

    @Test fun switchingPreservesThreeIndependentAddresses() {
        val sources = UpdateSources("https://nas/releases", "https://github.com/example/custom", UpdateSourceKind.Gitee)
            .withUrl(UpdateSourceKind.Gitee, "https://gitee.com/example/custom")
        assertEquals("https://nas/releases", sources.copy(selected = UpdateSourceKind.File).url())
        assertEquals("https://github.com/example/custom", sources.copy(selected = UpdateSourceKind.GitHub).url())
        assertEquals("https://gitee.com/example/custom", sources.url())
    }
}
