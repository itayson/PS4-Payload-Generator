package com.tayson.ps4linksender.relay

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.ConnectivityManager
import android.net.wifi.WifiManager
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import com.tayson.ps4linksender.MainActivity
import java.net.Inet4Address

class RelayService : Service() {
    companion object {
        const val PORT = 8787
        private const val CHANNEL_ID = "ps4_relay"
        private const val NOTIFICATION_ID = 8787
        private const val ACTION_STOP = "com.tayson.ps4linksender.relay.STOP"

        @Volatile var ready: Boolean = false
            private set
        @Volatile var lastError: String? = null
            private set

        fun ensureRunning(context: Context) {
            val i = Intent(context, RelayService::class.java)
            if (Build.VERSION.SDK_INT >= 26) context.startForegroundService(i) else context.startService(i)
            val deadline = System.currentTimeMillis() + 4000
            while (!ready && lastError == null && System.currentTimeMillis() < deadline) Thread.sleep(40)
            lastError?.let { throw IllegalStateException("Relay não iniciou: $it") }
            if (!ready) throw IllegalStateException("Relay não respondeu a tempo")
        }

        fun stop(context: Context) {
            context.startService(Intent(context, RelayService::class.java).setAction(ACTION_STOP))
        }

        fun localIpv4(context: Context): String {
            val cm = context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
            val network = cm.activeNetwork ?: throw IllegalStateException("Sem rede ativa")
            val props = cm.getLinkProperties(network) ?: throw IllegalStateException("Não foi possível ler a rede local")
            val address = props.linkAddresses.map { it.address }.firstOrNull {
                it is Inet4Address && !it.isLoopbackAddress && it.isSiteLocalAddress
            } ?: throw IllegalStateException("Conecte o celular ao mesmo Wi‑Fi/LAN do PS4")
            return address.hostAddress ?: throw IllegalStateException("IPv4 local inválido")
        }
    }

    private var server: RelayHttpServer? = null
    private var wakeLock: PowerManager.WakeLock? = null
    private var wifiLock: WifiManager.WifiLock? = null
    private var servedSinceNotification = 0L

    override fun onCreate() {
        super.onCreate()
        lastError = null
        createChannel()
        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(NOTIFICATION_ID, notification("Relay iniciando…"), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        } else {
            startForeground(NOTIFICATION_ID, notification("Relay iniciando…"))
        }
        try {
            val pm = getSystemService(Context.POWER_SERVICE) as PowerManager
            wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "TaysonPS4:Relay").apply { setReferenceCounted(false); acquire() }
            val wm = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
            @Suppress("DEPRECATION")
            wifiLock = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "TaysonPS4:RelayWifi").apply { setReferenceCounted(false); acquire() }
            server = RelayHttpServer(PORT, RelayRegistry) { n ->
                servedSinceNotification += n
                if (servedSinceNotification >= 64L * 1024L * 1024L) {
                    servedSinceNotification = 0
                    updateNotification()
                }
            }.also { it.start() }
            ready = true
            updateNotification()
        } catch (e: Exception) {
            lastError = e.message ?: e.javaClass.simpleName
            ready = false
            stopSelf()
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            RelayRegistry.clear()
            stopSelf()
            return START_NOT_STICKY
        }
        updateNotification()
        return START_STICKY
    }

    override fun onDestroy() {
        ready = false
        runCatching { server?.close() }
        runCatching { if (wifiLock?.isHeld == true) wifiLock?.release() }
        runCatching { if (wakeLock?.isHeld == true) wakeLock?.release() }
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createChannel() {
        if (Build.VERSION.SDK_INT >= 26) {
            val nm = getSystemService(NotificationManager::class.java)
            nm.createNotificationChannel(NotificationChannel(CHANNEL_ID, "PS4 Relay", NotificationManager.IMPORTANCE_LOW).apply {
                description = "Mantém o relay HTTP local ativo enquanto o PS4 baixa o PKG"
                setShowBadge(false)
            })
        }
    }

    private fun updateNotification() {
        val bytes = server?.totalBytesServed() ?: 0L
        val gb = bytes.toDouble() / (1024.0 * 1024.0 * 1024.0)
        val txt = "${RelayRegistry.count()} PKG(s) • %.2f GiB enviados".format(gb)
        val nm = getSystemService(NotificationManager::class.java)
        nm.notify(NOTIFICATION_ID, notification(txt))
    }

    private fun notification(text: String): Notification {
        val open = PendingIntent.getActivity(
            this, 1, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val stop = PendingIntent.getService(
            this, 2, Intent(this, RelayService::class.java).setAction(ACTION_STOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        return Notification.Builder(this, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.stat_sys_download)
            .setContentTitle("Tayson PS4 Relay ativo")
            .setContentText(text)
            .setContentIntent(open)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .addAction(Notification.Action.Builder(null, "PARAR RELAY", stop).build())
            .build()
    }
}
