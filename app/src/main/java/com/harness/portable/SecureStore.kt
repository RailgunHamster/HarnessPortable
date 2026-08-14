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
 * Password vault backed by the Android Keystore.
 *
 * A non-exportable AES-256-GCM key lives inside AndroidKeyStore; the ciphertext
 * (iv || ciphertext) is persisted in app-private preferences. The plaintext
 * password never touches disk and never leaves the process except into JSch.
 */
object SecureStore {

    private const val PREFS = "harness_portable_secure"
    private const val KEY_ALIAS = "harness_portable_master"
    private const val IV_LEN = 12

    private fun prefKey(profileId: String) = "pw_$profileId"

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
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, obtainKey())
        val blob = Base64.encodeToString(
            cipher.iv + cipher.doFinal(password.toByteArray(Charsets.UTF_8)),
            Base64.NO_WRAP
        )
        prefs(ctx).edit().putString(prefKey(profileId), blob).apply()
    }

    fun hasPassword(ctx: Context, profileId: String): Boolean =
        prefs(ctx).contains(prefKey(profileId))

    /** Returns null when nothing is stored or when the master key was invalidated. */
    fun getPassword(ctx: Context, profileId: String): String? {
        val blob = prefs(ctx).getString(prefKey(profileId), null) ?: return null
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

    fun clearPassword(ctx: Context, profileId: String) {
        prefs(ctx).edit().remove(prefKey(profileId)).apply()
    }
}
