package com.harness.portable

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import android.os.SystemClock
import android.util.Log
import com.jcraft.jsch.HostKey
import com.jcraft.jsch.HostKeyRepository
import com.jcraft.jsch.JSch
import com.jcraft.jsch.JSchChangedHostKeyException
import com.jcraft.jsch.JSchException
import com.jcraft.jsch.Session
import com.jcraft.jsch.UserInfo
import java.util.Properties

/**
 * Foreground service that maintains an SSH local port forward
 * (the mobile equivalent of `ssh -N -L local:remoteHost:remotePort user@host`).
 *
 * Host keys: trust-on-first-use via [TofuHostKeyRepository] — the first
 * connection to a host remembers its key; a *changed* key is rejected.
 */
class SshTunnelService : Service() {

    companion object {
        const val ACTION_START = "com.harness.portable.START"
        const val ACTION_STOP = "com.harness.portable.STOP"
        const val EXTRA_PROFILE_ID = "profile_id"

        private const val CHANNEL_ID = "tunnel"
        private const val NOTIFICATION_ID = 1001
        private const val SVC_PREFS = "harness_portable_service"
        private const val PREF_ACTIVE = "active_tunnel_profile"
        private const val NO_PW_MSG = "未保存密码"
        private const val TAG = "HarnessTunnel"

        // Keep sending inexpensive SSH keepalives, but tolerate a full ten
        // minutes of unanswered probes while the UI is in the background.
        // The extra four probes are scheduling margin around the ten-minute
        // wake-lock window.
        private const val SERVER_ALIVE_INTERVAL_MS = 15_000
        private const val FOREGROUND_SERVER_ALIVE_COUNT_MAX = 4
        private const val BACKGROUND_SERVER_ALIVE_COUNT_MAX = 44
        private const val BACKGROUND_GRACE_MS = 10 * 60 * 1_000L

        fun start(ctx: Context, profileId: String) {
            val intent = Intent(ctx, SshTunnelService::class.java)
                .setAction(ACTION_START)
                .putExtra(EXTRA_PROFILE_ID, profileId)
            ctx.startForegroundService(intent)
        }

        fun stop(ctx: Context) {
            try {
                ctx.startService(
                    Intent(ctx, SshTunnelService::class.java).setAction(ACTION_STOP)
                )
            } catch (_: Exception) {
                // Starting a service is not allowed from background on newer
                // Androids; the OS will tear the tunnel down on its own then.
            }
        }

        fun activeProfileId(ctx: Context): String? =
            ctx.getSharedPreferences(SVC_PREFS, Context.MODE_PRIVATE)
                .getString(PREF_ACTIVE, null)
    }

    private var profile: TunnelProfile? = null
    @Volatile
    private var session: Session? = null

    private val sessionPolicyLock = Any()

    private var backgroundWakeLock: PowerManager.WakeLock? = null

    @Volatile
    private var backgroundSinceElapsed = 0L

    private val visibilityListener: (Boolean) -> Unit = { visible ->
        applyVisibilityPolicy(visible)
    }

    @Volatile
    private var generation = 0

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        backgroundWakeLock =
            (getSystemService(POWER_SERVICE) as PowerManager).newWakeLock(
                PowerManager.PARTIAL_WAKE_LOCK,
                "$packageName:ssh-background-grace"
            ).apply { setReferenceCounted(false) }
        AppVisibility.addListener(visibilityListener)
        if (Build.VERSION.SDK_INT >= 26) {
            val manager = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
            manager.createNotificationChannel(
                NotificationChannel(
                    CHANNEL_ID, "SSH 隧道", NotificationManager.IMPORTANCE_LOW
                )
            )
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        Log.d(TAG, "onStartCommand action=${intent?.action} startId=$startId")
        when (intent?.action) {
            ACTION_START -> {
                val p = ProfileStore.findTunnel(this, intent.getStringExtra(EXTRA_PROFILE_ID))
                if (p == null) {
                    TunnelState.set(TunnelState.Info(status = TunnelState.Status.FAILED, message = "隧道配置不存在"))
                    stopSelf()
                    return START_NOT_STICKY
                }
                profile = p
                svcPrefs().edit().putString(PREF_ACTIVE, p.id).apply()
                enterForeground("正在连接 ${p.name}…")
                applyVisibilityPolicy(AppVisibility.isVisible)
                startWorker(p)
            }

            ACTION_STOP -> {
                svcPrefs().edit().remove(PREF_ACTIVE).apply()
                shutdown(announce = true)
                stopSelf()
            }

            else -> {
                // Restarted by the system (START_STICKY): resume the last profile.
                val id = svcPrefs().getString(PREF_ACTIVE, null)
                val p = ProfileStore.findTunnel(this, id)
                if (p == null) {
                    stopSelf()
                    return START_NOT_STICKY
                }
                profile = p
                enterForeground("正在恢复 ${p.name}…")
                applyVisibilityPolicy(AppVisibility.isVisible)
                startWorker(p)
            }
        }
        return START_STICKY
    }

    override fun onDestroy() {
        Log.d(TAG, "onDestroy")
        AppVisibility.removeListener(visibilityListener)
        generation++
        try { session?.disconnect() } catch (_: Exception) {}
        session = null
        releaseBackgroundWakeLock()
        super.onDestroy()
    }

    /** Progress marker: each stage transition is logged with elapsed time. */
    private fun stage(name: String) {
        Log.d(TAG, "stage: $name")
    }

    private fun svcPrefs() = getSharedPreferences(SVC_PREFS, Context.MODE_PRIVATE)

    private fun startWorker(p: TunnelProfile) {
        generation++
        val g = generation
        Thread {
            TunnelState.set(
                TunnelState.Info(p.id, p.name, TunnelState.Status.CONNECTING)
            )
            updateNotification("正在连接 ${p.name}…")
            // Watchdog: if a connect attempt silently stalls (vendor power
            // management, wedged keystore, unresponsive stack), tear the
            // worker down and start over. Never fires while CONNECTED —
            // the monitor loop is expected to stay there indefinitely.
            var watchdogFired = false
            val watchdog = Thread {
                try {
                    Thread.sleep(45_000)
                } catch (_: InterruptedException) {
                    return@Thread
                }
                val st = TunnelState.flow.value.status
                if (g == generation && st != TunnelState.Status.CONNECTED) {
                    watchdogFired = true
                    Log.w(TAG, "watchdog: stalled in $st, restarting worker")
                    try { session?.disconnect() } catch (_: Exception) {}
                    startWorker(p)
                }
            }.apply { isDaemon = true; start() }
            try {
                runLoop(g, p)
            } finally {
                watchdog.interrupt()
                if (watchdogFired) {
                    Log.d(TAG, "worker g=$g exited via watchdog")
                }
            }
        }.start()
    }

    private fun shutdown(announce: Boolean) {
        generation++
        try { session?.disconnect() } catch (_: Exception) {}
        session = null
        profile = null
        backgroundSinceElapsed = 0L
        releaseBackgroundWakeLock()
        if (announce) {
            TunnelState.set(TunnelState.Info(status = TunnelState.Status.STOPPED))
        }
        stopForeground(STOP_FOREGROUND_REMOVE)
    }

    private fun runLoop(g: Int, p: TunnelProfile) {
        var backoff = 3_000L
        while (g == generation) {
            var s: Session? = null
            try {
                Log.d(TAG, "connecting ${p.user}@${p.sshHost}:${p.sshPort} -> ${p.remoteHost}:${p.remotePort} (local ${p.localPort})")
                stage("reading password")
                val password = SecureStore.getPassword(this, p.id)
                    ?: throw JSchException(NO_PW_MSG)
                stage("building session")

                val jsch = JSch()
                jsch.setHostKeyRepository(TofuHostKeyRepository(this))
                val sess = jsch.getSession(p.user, p.sshHost, p.sshPort)
                sess.setPassword(password)
                sess.setConfig(Properties().apply {
                    // The TOFU repository decides: new key -> remember + OK,
                    // matching key -> OK, changed key -> CHANGED -> rejected.
                    put("StrictHostKeyChecking", "yes")
                    put("PreferredAuthentications", "password,keyboard-interactive")
                })
                // Bare host names: resolve ourselves (NetBIOS broadcast on the
                // LAN, system DNS for Tailscale MagicDNS) instead of relying
                // on the OS resolver, which ignores NetBIOS names.
                var viaLabel = ""
                if (!HostResolver.isIpLiteral(p.sshHost) && !p.sshHost.contains('.')) {
                    sess.setSocketFactory(ResolvingSocketFactory { r ->
                        viaLabel = when (r.source) {
                            "tailscale" -> " · Tailscale ${r.ip}"
                            "netbios" -> " · NetBIOS ${r.ip}"
                            "dns" -> " · DNS ${r.ip}"
                            else -> " · ${r.ip}"
                        }
                    })
                }
                // In the foreground, detect a dead link within about a minute.
                // In the background, allow the requested ten-minute grace
                // window before JSch tears down an otherwise healthy session.
                s = sess
                synchronized(sessionPolicyLock) {
                    session = sess
                    sess.setServerAliveInterval(SERVER_ALIVE_INTERVAL_MS)
                    sess.setServerAliveCountMax(serverAliveCountMax())
                }
                stage("ssh connect")
                sess.connect(20_000)
                stage("setting up forward")

                var bound = -1
                var lastBindError: Exception? = null
                for (delta in 0 until 10) {
                    val candidate = p.localPort + delta
                    if (candidate > 65535) break
                    try {
                        sess.setPortForwardingL(candidate, p.remoteHost, p.remotePort)
                        bound = candidate
                        break
                    } catch (e: Exception) {
                        lastBindError = e
                    }
                }
                if (bound < 0) throw (lastBindError ?: JSchException("无法绑定本地端口"))

                stage("connected")
                backoff = 3_000L

                // dsh-web style services gate the browser behind a launch
                // token printed on the server. In NSSM mode grab it on every
                // connect (cheap) so the first navigation carries it;
                // failure falls back to the bare URL, whose 401 page opens
                // the manual paste dialog.
                var authUrl: String? = null
                if (p.authMode == TunnelProfile.AUTH_MODE_NSSM) {
                    stage("auth-url fetch")
                    authUrl = NssmAuthUrl.fetch(sess, p.remotePort)
                    stage("auth-url " + (authUrl?.let { "ok" } ?: "miss"))
                }

                TunnelState.set(
                    TunnelState.Info(
                        p.id, p.name, TunnelState.Status.CONNECTED,
                        "127.0.0.1:$bound → ${p.remoteHost}:${p.remotePort}$viaLabel", bound,
                        authUrl
                    )
                )
                updateNotification("${p.name} 已连接 · 127.0.0.1:$bound → ${p.remoteHost}:${p.remotePort}$viaLabel")

                // Monitor: leave the connected state as soon as the session dies.
                while (g == generation && sess.isConnected) {
                    Thread.sleep(2_000)
                }
                if (g != generation) return

                TunnelState.set(
                    TunnelState.Info(p.id, p.name, TunnelState.Status.RETRYING, "连接中断，正在重连…")
                )
                updateNotification("${p.name} 连接中断，正在重连…")
            } catch (e: Exception) {
                if (g != generation) return
                val msg = e.message ?: e.javaClass.simpleName
                Log.w(TAG, "connect failed: ${e.javaClass.simpleName}: $msg", e)
                val resolveFail = msg.contains("无法解析")
                val authFail = !resolveFail && (
                        msg.contains("auth", ignoreCase = true) ||
                                msg.contains(NO_PW_MSG)
                        )
                val hostKeyChanged = e is JSchChangedHostKeyException ||
                        msg.contains("hostkey", ignoreCase = true) ||
                        msg.contains("host key", ignoreCase = true)
                when {
                    resolveFail -> {
                        TunnelState.set(
                            TunnelState.Info(p.id, p.name, TunnelState.Status.FAILED, msg)
                        )
                        updateNotification("${p.name} 无法解析主机名")
                        svcPrefs().edit().remove(PREF_ACTIVE).apply()
                        stopSelf()
                        return
                    }

                    hostKeyChanged -> {
                        TunnelState.set(
                            TunnelState.Info(
                                p.id, p.name, TunnelState.Status.FAILED,
                                "服务器主机密钥已改变，拒绝连接（可能存在中间人攻击）"
                            )
                        )
                        updateNotification("${p.name} 主机密钥改变，已拒绝")
                        svcPrefs().edit().remove(PREF_ACTIVE).apply()
                        stopSelf()
                        return
                    }

                    authFail -> {
                        TunnelState.set(
                            TunnelState.Info(p.id, p.name, TunnelState.Status.FAILED, "认证失败：$msg")
                        )
                        updateNotification("${p.name} 认证失败")
                        svcPrefs().edit().remove(PREF_ACTIVE).apply()
                        stopSelf()
                        return
                    }

                    else -> {
                        TunnelState.set(
                            TunnelState.Info(p.id, p.name, TunnelState.Status.RETRYING, msg)
                        )
                        updateNotification("${p.name} 重连中：$msg")
                    }
                }
            } finally {
                try { s?.disconnect() } catch (_: Exception) {}
                if (session === s) session = null
            }

            // Interruptible backoff before the next attempt.
            var waited = 0L
            while (g == generation && waited < backoff) {
                try {
                    Thread.sleep(250)
                    waited += 250
                } catch (_: InterruptedException) {
                    return
                }
            }
            backoff = (backoff * 2).coerceAtMost(30_000L)
        }
    }

    // ----- foreground plumbing -----

    private fun serverAliveCountMax(): Int =
        if (AppVisibility.isVisible) {
            FOREGROUND_SERVER_ALIVE_COUNT_MAX
        } else {
            BACKGROUND_SERVER_ALIVE_COUNT_MAX
        }

    private fun applyVisibilityPolicy(visible: Boolean) {
        synchronized(sessionPolicyLock) {
            session?.setServerAliveCountMax(
                if (visible) {
                    FOREGROUND_SERVER_ALIVE_COUNT_MAX
                } else {
                    BACKGROUND_SERVER_ALIVE_COUNT_MAX
                }
            )
        }

        if (visible || profile == null) {
            backgroundSinceElapsed = 0L
            releaseBackgroundWakeLock()
            return
        }

        val now = SystemClock.elapsedRealtime()
        val since = backgroundSinceElapsed.takeIf { it != 0L } ?: now.also {
            backgroundSinceElapsed = it
        }
        val remaining = BACKGROUND_GRACE_MS - (now - since)
        val lock = backgroundWakeLock ?: return
        if (remaining > 0L && !lock.isHeld) {
            try {
                lock.acquire(remaining)
                Log.d(TAG, "background grace: wake lock held for ${remaining}ms")
            } catch (e: RuntimeException) {
                Log.w(TAG, "background grace: unable to acquire wake lock", e)
            }
        }
    }

    private fun releaseBackgroundWakeLock() {
        val lock = backgroundWakeLock ?: return
        if (lock.isHeld) {
            try {
                lock.release()
            } catch (_: RuntimeException) {
                // A timed wake lock may have expired between isHeld and release.
            }
        }
    }

    private fun enterForeground(text: String) {
        val n = buildNotification(text)
        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(
                NOTIFICATION_ID,
                n,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE
            )
        } else {
            startForeground(NOTIFICATION_ID, n)
        }
    }

    private fun updateNotification(text: String) {
        val manager = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        manager.notify(NOTIFICATION_ID, buildNotification(text))
    }

    private fun buildNotification(text: String): Notification {
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE
        )
        val stop = PendingIntent.getService(
            this, 1,
            Intent(this, SshTunnelService::class.java).setAction(ACTION_STOP),
            PendingIntent.FLAG_IMMUTABLE
        )
        val builder = if (Build.VERSION.SDK_INT >= 26) {
            Notification.Builder(this, CHANNEL_ID)
        } else {
            @Suppress("DEPRECATION")
            Notification.Builder(this)
        }
        return builder
            .setSmallIcon(R.drawable.ic_stat_tunnel)
            .setContentTitle("Harness Portable 隧道")
            .setContentText(text)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setContentIntent(open)
            .addAction(0, "断开", stop)
            .build()
    }
}

/**
 * Trust-on-first-use host key store persisted in app-private preferences.
 * The first connection to a host remembers its key; a *changed* key later
 * yields CHANGED, which makes JSch abort the connection (MITM protection).
 */
private class TofuHostKeyRepository(appContext: android.content.Context) : HostKeyRepository {

    private val prefs = appContext.getSharedPreferences(
        "harness_portable_known_hosts", android.content.Context.MODE_PRIVATE
    )

    private fun prefKey(host: String, type: String) = "hk_${host}_$type"

    override fun check(host: String?, key: ByteArray?): Int {
        if (host == null || key == null) return HostKeyRepository.OK
        return try {
            val hk = HostKey(host, key)
            val type = hk.getType()
            val blob = hk.getKey()
            val stored = prefs.getString(prefKey(host, type), null)
            when {
                stored == null -> {
                    prefs.edit().putString(prefKey(host, type), blob).apply()
                    HostKeyRepository.OK
                }
                stored == blob -> HostKeyRepository.OK
                else -> HostKeyRepository.CHANGED
            }
        } catch (e: Exception) {
            // Unparseable key: don't lock the user out of their own server.
            HostKeyRepository.OK
        }
    }

    override fun add(hostkey: HostKey?, ui: UserInfo?) {
        val hk = hostkey ?: return
        prefs.edit().putString(prefKey(hk.getHost(), hk.getType()), hk.getKey()).apply()
    }

    override fun remove(host: String?, type: String?) {
        if (host != null && type != null) {
            prefs.edit().remove(prefKey(host, type)).apply()
        }
    }

    override fun remove(host: String?, type: String?, parsedData: ByteArray?) {
        remove(host, type)
    }

    override fun getKnownHostsRepositoryID(): String = "harness-portable-tofu"

    override fun getHostKey(): Array<HostKey> = emptyArray()

    override fun getHostKey(host: String?, type: String?): Array<HostKey> = emptyArray()
}
