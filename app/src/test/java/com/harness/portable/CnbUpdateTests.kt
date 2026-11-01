package com.harness.portable

import org.junit.Assert.*
import org.junit.Test

class CnbUpdateTests {
    @Test fun publicFeedAndVersionedAssetHaveNoTokenOrApiDependency() {
        val feed="https://cnb.cool/railgunhamster/HarnessPortable/-/git/raw/updates/android.json"
        assertEquals(feed,ApkUpdate.feedUrl(""));assertEquals(feed,ApkUpdate.feedUrl(ApkUpdate.DEFAULT_SERVER+".git"))
        val apk="https://cnb.cool/railgunhamster/HarnessPortable/-/releases/download/v2.8.1/HarnessPortable-2.8.1.apk"
        assertEquals(apk,ApkUpdate.apkUrl(feed,apk))
        assertEquals("https://github.com/RailgunHamster/HarnessPortable/releases/latest/download/android.json",ApkUpdate.feedUrl(ApkUpdate.DEFAULT_GITHUB))
    }
    @Test fun onlyCnbAndGithubCanBeSelectedAndRetiredValuesMigrateToDefault() {
        assertEquals(listOf(UpdateSourceKind.CNB,UpdateSourceKind.GitHub),UpdateSourceKind.entries)
        for(legacy in listOf("File","Gitee","home","unknown")) assertNull(UpdateSourceStore.kindFromName(legacy))
        assertEquals(UpdateSourceKind.CNB,UpdateSourceStore.DEFAULT_SELECTED)
        assertEquals(UpdateSourceKind.CNB,UpdateSources(selected=UpdateSourceKind.GitHub).migrateOfficialSource().selected)
        assertEquals(UpdateSourceKind.GitHub,UpdateSources(github="https://github.com/example/custom",selected=UpdateSourceKind.GitHub).migrateOfficialSource().selected)
    }
    @Test fun independentAddressesAndStrictRepositoryValidation() {
        val sources=UpdateSources().withUrl(UpdateSourceKind.CNB,"https://cnb.cool/group/sub/repo.git").withUrl(UpdateSourceKind.GitHub,"https://github.com/example/custom")
        assertTrue(sources.selectedLooksUsable());assertEquals("https://github.com/example/custom",sources.copy(selected=UpdateSourceKind.GitHub).url())
        for(url in listOf("https://cnb.cool","https://cnb.cool.evil.example/g/r","http://cnb.cool/g/r","https://token@cnb.cool/g/r","https://gitee.com/g/r"))assertFalse(url,ApkUpdate.looksLikeCnb(url))
        for(url in listOf("smb://server/share","C:\\updates","\\\\server\\share"))assertTrue(runCatching { ApkUpdate.feedUrl(url) }.isFailure)
    }
}
