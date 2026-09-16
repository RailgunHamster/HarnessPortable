package com.harness.portable

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.wifi.WifiManager
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
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
        const val EXTRA_RESTART = "restart"

        private const val CHANNEL_ID = "tunnel"
        private const val NOTIFICATION_ID = 1001
        private const val SVC_PREFS = "harness_portable_service"
        private const val PREF_ACTIVE = "active_tunnel_profile"
        private const val NO_PW_MSG = "未保存密码"
        private const val TAG = "HarnessTunnel"

        // SSH-level keepalives. 15s × 4 unanswered probes ≈ 1 minute to
        // detect a dead link, then the worker reconnects. A wake lock is
        // held for the whole tunnel lifetime so these probes actually fire
        // while the UI is in the background.
        private const val SERVER_ALIVE_INTERVAL_MS = 15_000
        private const val SERVER_ALIVE_COUNT_MAX = 4
        private const val WAKE_LOCK_CHUNK_MS = 60 * 60 * 1_000L

        fun start(ctx: Context, profileId: String, restart: Boolean = false) {
            val intent = Intent(ctx, SshTunnelService::class.java)
                .setAction(ACTION_START)
                .putExtra(EXTRA_PROFILE_ID, profileId)
                .putExtra(EXTRA_RESTART, restart)
            ctx.startForegroundService(intent)
        }

        /**
         * If the last tunnel was supposed to stay up (PREF_ACTIVE) but the
         * in-memory session is gone — process death, OEM kill, swipe-away —
         * start the service again. No-op while that profile is already
         * connecting / connected / retrying.
         */
        fun resumeIfNeeded(ctx: Context) {
            val id = activeProfileId(ctx) ?: return
            val st = TunnelState.flow.value
            if (st.profileId == id &&
                (st.status == TunnelState.Status.CONNECTED ||
                    st.status == TunnelState.Status.CONNECTING ||
                    st.status == TunnelState.Status.RETRYING)
            ) {
                return
            }
            Log.d(TAG, "resumeIfNeeded: restarting $id (was ${st.status})")
            start(ctx, id, restart = false)
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

    private var tunnelWakeLock: PowerManager.WakeLock? = null
    private var wifiLock: WifiManager.WifiLock? = null

    private val visibilityListener: (Boolean) -> Unit = { visible ->
        applyKeepalivePolicy(visible)
    }

    @Volatile
    private var generation = 0

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        tunnelWakeLock =
            (getSystemService(POWER_SERVICE) as PowerManager).newWakeLock(
                PowerManager.PARTIAL_WAKE_LOCK,
                "$packageName:ssh-tunnel"
            ).apply { setReferenceCounted(false) }
        try {
            @Suppress("DEPRECATION")
            wifiLock = (applicationContext.getSystemService(WIFI_SERVICE) as WifiManager)
                .createWifiLock(
                    WifiManager.WIFI_MODE_FULL_HIGH_PERF,
                    "$packageName:ssh-wifi"
                ).apply { setReferenceCounted(false) }
        } catch (e: RuntimeException) {
            Log.w(TAG, "wifi lock unavailable: ${e.message}")
        }
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
                val restart = intent.getBooleanExtra(EXTRA_RESTART, false)
                if (!restart && isWorkingOn(p)) {
                    Log.d(TAG, "ACTION_START ignored — already working on ${p.id}")
                    enterForeground(notificationTextFor(p))
                    holdRuntimeLocks()
                    applyKeepalivePolicy(AppVisibility.isVisible)
                    return START_STICKY
                }
                profile = p
                svcPrefs().edit().putString(PREF_ACTIVE, p.id).apply()
                enterForeground("正在连接 ${p.name}…")
                holdRuntimeLocks()
                applyKeepalivePolicy(AppVisibility.isVisible)
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
                holdRuntimeLocks()
                applyKeepalivePolicy(AppVisibility.isVisible)
                startWorker(p)
            }
        }
        return START_STICKY
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        // Swiping the task away must not tear the tunnel down. The
        // foreground service + wake lock keep SSH keepalives running.
        Log.d(TAG, "onTaskRemoved — keeping foreground tunnel")
        if (profile != null) {
            holdRuntimeLocks()
            updateNotification("${profile?.name ?: "隧道"} 后台保活中")
        }
    }

    override fun onDestroy() {
        Log.d(TAG, "onDestroy")
        AppVisibility.removeListener(visibilityListener)
        generation++
        try { session?.disconnect() } catch (_: Exception) {}
        session = null
        releaseRuntimeLocks()
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
                if (g == generation &&
                    st != TunnelState.Status.CONNECTED &&
                    st != TunnelState.Status.FAILED &&
                    st != TunnelState.Status.STOPPED
                ) {
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
        releaseRuntimeLocks()
        if (announce) {
            TunnelState.set(TunnelState.Info(status = TunnelState.Status.STOPPED))
        }
        stopForeground(STOP_FOREGROUND_REMOVE)
    }

    private fun isWorkingOn(p: TunnelProfile): Boolean {
        if (profile?.id != p.id) return false
        val st = TunnelState.flow.value
        if (st.profileId != p.id) return false
        return st.status == TunnelState.Status.CONNECTED ||
            st.status == TunnelState.Status.CONNECTING ||
            st.status == TunnelState.Status.RETRYING
    }

    private fun notificationTextFor(p: TunnelProfile): String {
        val st = TunnelState.flow.value
        return when (st.status) {
            TunnelState.Status.CONNECTED ->
                st.message ?: "${p.name} 已连接"
            TunnelState.Status.RETRYING ->
                "${p.name} 连接中断，正在重连…"
            else -> "正在连接 ${p.name}…"
        }
    }

    private fun runLoop(g: Int, p: TunnelProfile) {
        var backoff = 3_000L
        while (g == generation) {
            holdRuntimeLocks()
            var s: Session? = null
            try {
                Log.d(TAG, "connecting ${p.user}@${p.sshHost}:${p.sshPort} -> ${p.remoteHost}:${p.remotePort} (local ${p.localPort})")
                stage("reading credentials")
                val password = SecureStore.getPassword(this, p.id)
                val identity = p.identityFile.trim()
                if (password.isNullOrEmpty() && identity.isEmpty()) {
                    throw JSchException(NO_PW_MSG)
                }
                stage("building session")

                val jsch = JSch()
                jsch.setHostKeyRepository(TofuHostKeyRepository(this))
                if (identity.isNotEmpty()) {
                    val added = try {
                        jsch.addIdentity(identity)
                        true
                    } catch (_: Exception) {
                        if (!password.isNullOrEmpty()) {
                            try {
                                jsch.addIdentity(identity, password)
                                true
                            } catch (_: Exception) {
                                false
                            }
                        } else {
                            false
                        }
                    }
                    if (!added) {
                        throw JSchException(SshIdentity.missingCredentialsMessage(identity))
                    }
                }
                val sess = jsch.getSession(p.user, p.sshHost, p.sshPort)
                if (!password.isNullOrEmpty()) {
                    sess.setPassword(password)
                }
                val methods = buildList {
                    if (identity.isNotEmpty()) add("publickey")
                    if (!password.isNullOrEmpty()) {
                        add("password")
                        add("keyboard-interactive")
                    }
                    if (isEmpty()) add("publickey")
                }.joinToString(",")
                sess.setConfig(Properties().apply {
                    // The TOFU repository decides: new key -> remember + OK,
                    // matching key -> OK, changed key -> CHANGED -> rejected.
                    put("StrictHostKeyChecking", "yes")
                    put("PreferredAuthentications", methods)
                    put("NumberOfPasswordPrompts", "1")
                })
                // Always own the TCP socket so we can enable SO_KEEPALIVE.
                // Bare host names are resolved ourselves (NetBIOS / MagicDNS)
                // instead of relying on the OS resolver.
                var viaLabel = ""
                sess.setSocketFactory(ResolvingSocketFactory { r ->
                    viaLabel = when (r.source) {
                        "tailscale" -> " · Tailscale ${r.ip}"
                        "netbios" -> " · NetBIOS ${r.ip}"
                        "dns" -> " · DNS ${r.ip}"
                        else -> ""
                    }
                })
                s = sess
                synchronized(sessionPolicyLock) {
                    session = sess
                    sess.setServerAliveInterval(SERVER_ALIVE_INTERVAL_MS)
                    sess.setServerAliveCountMax(SERVER_ALIVE_COUNT_MAX)
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
                // connect (cheap) so the first navigation carries it; a saved
                // per-profile URL/token is the only source in manual mode and
                // the fallback in NSSM mode. Otherwise the bare URL is opened,
                // whose 401 page opens the manual paste dialog.
                var authUrl: String? = null
                if (p.authMode == TunnelProfile.AUTH_MODE_NSSM) {
                    stage("auth-url fetch")
                    authUrl = NssmAuthUrl.fetch(sess, p.remotePort)
                    stage("auth-url " + (authUrl?.let { "ok" } ?: "miss"))
                }
                if (authUrl == null) {
                    stage("auth-url manual")
                    authUrl = WebAuthInput.normalize(
                        SecureStore.getAuthInput(this, p.id), p.remoteHost, p.remotePort
                    )
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
                // Refresh the timed wake lock so a background tunnel can outlive
                // a single hour-long acquire.
                while (g == generation && sess.isConnected) {
                    holdRuntimeLocks()
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
                val authFail = !resolveFail && SshIdentity.looksLikeAuthenticationFailure(msg)
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

    private fun applyKeepalivePolicy(visible: Boolean) {
        // Visibility no longer changes the keepalive budget — a background
        // tunnel must detect death just as quickly as a foreground one so
        // it can reconnect instead of sitting on a half-open TCP socket.
        synchronized(sessionPolicyLock) {
            session?.setServerAliveCountMax(SERVER_ALIVE_COUNT_MAX)
        }
        if (!visible && profile != null) {
            holdRuntimeLocks()
        }
    }

    private fun holdRuntimeLocks() {
        val wake = tunnelWakeLock
        if (wake != null) {
            try {
                if (!wake.isHeld) {
                    wake.acquire(WAKE_LOCK_CHUNK_MS)
                    Log.d(TAG, "wake lock acquired for ${WAKE_LOCK_CHUNK_MS}ms")
                }
            } catch (e: RuntimeException) {
                Log.w(TAG, "wake lock: unable to acquire", e)
            }
        }
        val wifi = wifiLock
        if (wifi != null) {
            try {
                if (!wifi.isHeld) {
                    wifi.acquire()
                    Log.d(TAG, "wifi lock acquired")
                }
            } catch (e: RuntimeException) {
                Log.w(TAG, "wifi lock: unable to acquire", e)
            }
        }
    }

    private fun releaseRuntimeLocks() {
        val wake = tunnelWakeLock
        if (wake != null && wake.isHeld) {
            try {
                wake.release()
            } catch (_: RuntimeException) {
                // A timed wake lock may have expired between isHeld and release.
            }
        }
        val wifi = wifiLock
        if (wifi != null && wifi.isHeld) {
            try {
                wifi.release()
            } catch (_: RuntimeException) {
            }
        }
    }

    private fun enterForeground(text: String) {
        val n = buildNotification(text)
        try {
            when {
                Build.VERSION.SDK_INT >= 34 -> startForeground(
                    NOTIFICATION_ID,
                    n,
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE
                )
                Build.VERSION.SDK_INT >= 29 -> startForeground(
                    NOTIFICATION_ID,
                    n,
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
                )
                else -> startForeground(NOTIFICATION_ID, n)
            }
        } catch (e: Exception) {
            Log.w(TAG, "startForeground typed failed: ${e.message}; falling back")
            try {
                if (Build.VERSION.SDK_INT >= 29) {
                    startForeground(
                        NOTIFICATION_ID,
                        n,
                        ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
                    )
                } else {
                    startForeground(NOTIFICATION_ID, n)
                }
            } catch (e2: Exception) {
                Log.w(TAG, "startForeground fallback failed: ${e2.message}")
                startForeground(NOTIFICATION_ID, n)
            }
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
