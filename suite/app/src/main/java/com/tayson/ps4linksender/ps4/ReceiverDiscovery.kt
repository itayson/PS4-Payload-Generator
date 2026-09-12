package com.tayson.ps4linksender.ps4

import android.content.Context
import android.net.ConnectivityManager
import java.net.Inet4Address
import java.util.concurrent.Callable
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

object ReceiverDiscovery {
    data class Match(val ip: String, val version: String)
    data class Result(val localIp: String, val tested: Int, val matches: List<Match>)

    fun scan(context: Context): Result {
        val cm = context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
        val network = cm.activeNetwork ?: throw IllegalStateException("Sem rede ativa")
        val props = cm.getLinkProperties(network) ?: throw IllegalStateException("Não foi possível ler a rede local")
        val link = props.linkAddresses.firstOrNull {
            val a = it.address
            a is Inet4Address && !a.isLoopbackAddress && a.isSiteLocalAddress
        } ?: throw IllegalStateException("Nenhum IPv4 privado encontrado. Conecte o celular à mesma rede do PS4.")

        val local = link.address as Inet4Address
        val bytes = local.address.map { it.toInt() and 0xff }
        val prefix = link.prefixLength.coerceAtLeast(24).coerceAtMost(30)
        val ipInt = bytes.fold(0) { acc, b -> (acc shl 8) or b }
        val mask = -1 shl (32 - prefix)
        val networkInt = ipInt and mask
        val hostCount = 1 shl (32 - prefix)
        val self = local.hostAddress ?: throw IllegalStateException("IPv4 local inválido")
        val targets = (1 until hostCount - 1)
            .map { intToIp(networkInt or it) }
            .filter { it != self }

        val pool = Executors.newFixedThreadPool(minOf(40, targets.size.coerceAtLeast(1)))
        return try {
            val futures = pool.invokeAll(targets.map { ip ->
                Callable {
                    try {
                        val p = ReceiverClient.probe(ip, 450)
                        Match(ip, p.version)
                    } catch (_: Exception) { null }
                }
            }, 8, TimeUnit.SECONDS)
            val found = futures.mapNotNull { f ->
                try { if (!f.isCancelled) f.get() else null } catch (_: Exception) { null }
            }.sortedBy { ipToInt(it.ip) }
            Result(self, targets.size, found)
        } finally {
            pool.shutdownNow()
        }
    }

    private fun intToIp(value: Int): String = listOf(24, 16, 8, 0).joinToString(".") { ((value ushr it) and 0xff).toString() }
    private fun ipToInt(ip: String): Long = ip.split('.').fold(0L) { acc, s -> (acc shl 8) or (s.toLongOrNull() ?: 255L) }
}
