package com.harness.portable

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Credential vault backed by the Android Keystore.
 *
 * A non-exportable AES-256-GCM key lives inside AndroidKeyStore; the ciphertext
 * (iv || ciphertext) is persisted in app-private preferences. The plaintext
 * password / web login token never touches disk and never leaves the process
 * except into JSch or the WebView.
 */
object SecureStore {

    private const val PREFS = "harness_portable_secure"
    private const val KEY_ALIAS = "harness_portable_master"
    private const val IV_LEN = 12

    private fun prefKey(profileId: String) = "pw_$profileId"

    private fun authPrefKey(profileId: String) = "web_$profileId"

    private fun prefs(ctx: Context) =
        ctx.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    private fun obtainKey(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore"
        )
        gen.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build()
        )
        return gen.generateKey()
    }

    fun setPassword(ctx: Context, profileId: String, password: String) {
        writeEncrypted(ctx, prefKey(profileId), password)
    }

    fun hasPassword(ctx: Context, profileId: String): Boolean =
        prefs(ctx).contains(prefKey(profileId))

    /** Returns null when nothing is stored or when the master key was invalidated. */
    fun getPassword(ctx: Context, profileId: String): String? =
        readEncrypted(ctx, prefKey(profileId))

    fun clearPassword(ctx: Context, profileId: String) {
        prefs(ctx).edit().remove(prefKey(profileId)).apply()
    }

    /**
     * Optional per-profile web login input (token URL, query, key=value pair or
     * the bare token); consumed only to build the first URL the WebView opens.
     */
    fun setAuthInput(ctx: Context, profileId: String, value: String) {
        writeEncrypted(ctx, authPrefKey(profileId), value)
    }

    fun hasAuthInput(ctx: Context, profileId: String): Boolean =
        prefs(ctx).contains(authPrefKey(profileId))

    /** Returns null when nothing is stored or when the master key was invalidated. */
    fun getAuthInput(ctx: Context, profileId: String): String? =
        readEncrypted(ctx, authPrefKey(profileId))

    fun clearAuthInput(ctx: Context, profileId: String) {
        prefs(ctx).edit().remove(authPrefKey(profileId)).apply()
    }

    private fun writeEncrypted(ctx: Context, key: String, value: String) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, obtainKey())
        val blob = Base64.encodeToString(
            cipher.iv + cipher.doFinal(value.toByteArray(Charsets.UTF_8)),
            Base64.NO_WRAP
        )
        prefs(ctx).edit().putString(key, blob).apply()
    }

    /** Returns null when nothing is stored or when the master key was invalidated. */
    private fun readEncrypted(ctx: Context, key: String): String? {
        val blob = prefs(ctx).getString(key, null) ?: return null
        return try {
            val raw = Base64.decode(blob, Base64.NO_WRAP)
            if (raw.size <= IV_LEN) return null
            val iv = raw.copyOfRange(0, IV_LEN)
            val ct = raw.copyOfRange(IV_LEN, raw.size)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, obtainKey(), GCMParameterSpec(128, iv))
            String(cipher.doFinal(ct), Charsets.UTF_8)
        } catch (e: Exception) {
            null
        }
    }
}
