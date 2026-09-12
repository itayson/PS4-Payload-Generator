package com.tayson.ps4linksender.relay

import android.content.Context
import com.tayson.ps4linksender.model.PkgInfo

object RelayManager {
    data class Prepared(val relayUrl: String, val upstreamUrl: String, val token: String)

    fun prepare(context: Context, info: PkgInfo): Prepared {
        RelayService.ensureRunning(context.applicationContext)
        val ip = RelayService.localIpv4(context)
        val entry = RelayRegistry.register(info.finalUrl, info.fileName, info.size, info.requestContext)
        val url = "http://$ip:${RelayService.PORT}${RelayRegistry.buildPath(entry)}"
        return Prepared(url, entry.upstreamUrl, entry.token)
    }

    fun stop(context: Context) = RelayService.stop(context.applicationContext)
    fun activeSessions(): Int = RelayRegistry.count()
}
