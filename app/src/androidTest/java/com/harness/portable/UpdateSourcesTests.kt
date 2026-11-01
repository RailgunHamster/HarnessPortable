package com.harness.portable

import android.content.Context
import android.content.ContextWrapper
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.*
import org.junit.Test
import java.util.UUID

class UpdateSourcesTests {
    @Test fun legacyPreferencesMigrateAndManualGithubSelectionSurvives() {
        val base=ApplicationProvider.getApplicationContext<Context>()
        val fixture="update-fixture-${UUID.randomUUID()}"
        val isolated=object:ContextWrapper(base){override fun getSharedPreferences(name:String,mode:Int)=base.getSharedPreferences(fixture,mode)}
        try {
            val prefs=isolated.getSharedPreferences("harness_portable",Context.MODE_PRIVATE)
            prefs.edit().putString("update_source_selected","File").putString("update_source_home","\\\\old\\share").putString("update_source_gitee","https://gitee.com/g/r").putString("update_source_github","https://github.com/example/custom").commit()
            SecureStore.setPassword(isolated,"fixture","existing-ssh-password")
            SecureStore.setAuthInput(isolated,"fixture","existing-web-token")
            val migrated=UpdateSourceStore.load(isolated)
            assertEquals(UpdateSourceKind.CNB,migrated.selected);assertEquals(ApkUpdate.DEFAULT_SERVER,migrated.cnb)
            assertFalse(prefs.contains("update_source_home"));assertFalse(prefs.contains("update_source_gitee"))
            assertEquals("existing-ssh-password",SecureStore.getPassword(isolated,"fixture"))
            assertEquals("existing-web-token",SecureStore.getAuthInput(isolated,"fixture"))
            UpdateSourceStore.select(isolated,UpdateSourceKind.GitHub)
            val saved=UpdateSourceStore.load(isolated);assertEquals(UpdateSourceKind.GitHub,saved.selected);assertEquals(migrated.github,saved.github)
        } finally { base.deleteSharedPreferences(fixture) }
    }
}
