package com.harness.portable

import android.annotation.SuppressLint
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings

/**
 * OEM battery savers (and stock Doze) will freeze or kill a long-lived SSH
 * tunnel unless the user exempts the app. The system dialog is requested
 * once; settings can open it again later.
 */
internal object BatteryExemption {

    private const val PREFS = "harness_portable"
    private const val KEY_PROMPTED = "battery_exemption_prompted"

    fun isExempt(ctx: Context): Boolean {
        if (Build.VERSION.SDK_INT < 23) return true
        val pm = ctx.getSystemService(Context.POWER_SERVICE) as PowerManager
        return pm.isIgnoringBatteryOptimizations(ctx.packageName)
    }

    fun shouldPrompt(ctx: Context): Boolean {
        if (isExempt(ctx)) return false
        return !ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getBoolean(KEY_PROMPTED, false)
    }

    fun markPrompted(ctx: Context) {
        ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .edit()
            .putBoolean(KEY_PROMPTED, true)
            .apply()
    }

    @SuppressLint("BatteryLife")
    fun request(ctx: Context) {
        markPrompted(ctx)
        val intent = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS)
            .setData(Uri.parse("package:${ctx.packageName}"))
        if (ctx !is Activity) intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        try {
            ctx.startActivity(intent)
        } catch (_: Exception) {
            try {
                val fallback = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
                if (ctx !is Activity) fallback.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                ctx.startActivity(fallback)
            } catch (_: Exception) {
            }
        }
    }
}
