package com.tayson.ps4linksender.ps4

import android.content.Context
import java.net.InetSocketAddress
import java.net.Socket

object PayloadLoaderClient {
    const val PORT = 9090
    private const val CONNECT_TIMEOUT_MS = 3000
    private const val IO_TIMEOUT_MS = 5000
    private const val ASSET_NAME = "tayson_payload_installer_0_1_0.bin"
    private const val MAX_PAYLOAD_BYTES = 4 * 1024 * 1024

    data class SendResult(val bytesSent: Int)

    fun sendEmbedded(context: Context, host: String): SendResult {
        requireHost(host)
        val bytes = context.assets.open(ASSET_NAME).use { input ->
            val data = input.readBytes()
            require(data.isNotEmpty()) { "Payload embutido está vazio" }
            require(data.size <= MAX_PAYLOAD_BYTES) { "Payload embutido excede o limite de segurança" }
            data
        }
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, PORT), CONNECT_TIMEOUT_MS)
            socket.soTimeout = IO_TIMEOUT_MS
            socket.tcpNoDelay = true
            socket.getOutputStream().use { out ->
                out.write(bytes)
                out.flush()
            }
            return SendResult(bytes.size)
        }
    }

    private fun requireHost(host: String) {
        require(host.isNotBlank()) { "Informe o IP do PS4" }
        require(!host.contains('/') && !host.contains(':')) { "Use somente o IPv4 do PS4" }
    }
}
