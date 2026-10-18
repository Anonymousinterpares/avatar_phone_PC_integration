package dev.bluey.phone

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.net.wifi.WifiManager

class Finder(
    context: Context,
    private val onFound: (name: String, host: String, port: Int) -> Unit,
) {
    private val nsd = context.getSystemService(Context.NSD_SERVICE) as NsdManager
    private val lock = (context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager)
        .createMulticastLock("bluey")
        .apply { setReferenceCounted(false) }
    private var started = false

    private val discovery = object : NsdManager.DiscoveryListener {
        override fun onDiscoveryStarted(serviceType: String) {}
        override fun onDiscoveryStopped(serviceType: String) {}
        override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {}
        override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {}
        override fun onServiceLost(serviceInfo: NsdServiceInfo) {}
        override fun onServiceFound(serviceInfo: NsdServiceInfo) {
            @Suppress("DEPRECATION")
            nsd.resolveService(serviceInfo, object : NsdManager.ResolveListener {
                override fun onResolveFailed(info: NsdServiceInfo, errorCode: Int) {}
                override fun onServiceResolved(info: NsdServiceInfo) {
                    val host = info.host?.hostAddress ?: return
                    if (info.port <= 0) return
                    onFound(info.serviceName, host, info.port)
                }
            })
        }
    }

    fun start() {
        if (started) return
        try {
            lock.acquire()
            nsd.discoverServices("_googly._tcp.", NsdManager.PROTOCOL_DNS_SD, discovery)
            started = true
        } catch (_: Exception) {
            // The address box still works when multicast is blocked.
        }
    }

    fun stop() {
        if (!started) return
        started = false
        try { nsd.stopServiceDiscovery(discovery) } catch (_: Exception) { }
        try { if (lock.isHeld) lock.release() } catch (_: Exception) { }
    }
}
