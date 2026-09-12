package com.tayson.ps4linksender.ps4

import android.content.Context
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.MessageDigest
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import org.json.JSONObject

object PayloadLoader {
    const val BIN_PORT = 9090
    const val ELF_PORT = 9021

    fun start(context: Context, host: String, binPort: Int, elfPort: Int, onStage: (String) -> Unit): PayloadProbeClient.Identity {
        PayloadProbeClient.requireHost(host)
        require(binPort in 1..65535 && elfPort in 1..65535 && binPort != elfPort &&
            binPort !in listOf(9095, 9096) && elfPort !in listOf(9095, 9096)) { "Portas do carregador inválidas" }
        val active = runCatching { PayloadProbeClient.ping(host) }.getOrNull()
        if (active != null) {
            require(active.version == "1.5.0") { "Há outro payload Tayson ativo (${active.version}). Reinicie o console para carregar esta versão." }
            return active
        }
        // Both assets are fixed at build time and checked against the bundled manifest.
        val elf = verifiedAsset(context, "tayson_probe.elf")
        validateElf(elf)
        val bootstrap = verifiedAsset(context, "elfldr.bin")
        require(!bootstrap.take(4).toByteArray().contentEquals(byteArrayOf(0x7f, 69, 76, 70))) { "Bootstrap BIN contém ELF; artefato incorreto" }
        if (!isOpen(host, elfPort)) {
            onStage("Enviando carregador ao BinLoader :$binPort…")
            send(host, binPort, bootstrap)
            await(15_000) { isOpen(host, elfPort) }
                ?: error("O carregador não abriu a porta $elfPort. Confira o BinLoader e as notificações do PS4.")
        }
        onStage("Enviando Tayson Payload ao carregador ELF :$elfPort…")
        send(host, elfPort, elf)
        onStage("Aguardando confirmação do payload :${PayloadProbeClient.PORT}…")
        return await(20_000) { runCatching { PayloadProbeClient.ping(host) }.getOrNull() }
            ?: error("O ELF foi transmitido, mas não houve confirmação na porta ${PayloadProbeClient.PORT}. Isso ainda não comprova execução no PS4.")
    }

    private fun verifiedAsset(context: Context, name: String): ByteArray {
        val manifest = JSONObject(context.assets.open("payload/manifest.json").bufferedReader().use { it.readText() })
        val expected = manifest.getJSONObject(name)
        val bytes = context.assets.open("payload/$name").use { it.readBytes() }
        require(bytes.size in 64..4 * 1024 * 1024 && bytes.size == expected.getInt("size")) { "Tamanho inválido: $name" }
        val actual = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it.toInt() and 255) }
        require(actual == expected.getString("sha256")) { "Integridade do payload inválida: $name" }
        return bytes
    }

    internal fun validateElf(bytes: ByteArray) {
        require(bytes.size >= 64 && bytes[0] == 0x7f.toByte() && bytes[1] == 69.toByte() && bytes[2] == 76.toByte() && bytes[3] == 70.toByte()) { "Arquivo ELF inválido" }
        require(bytes[4] == 2.toByte() && bytes[5] == 1.toByte()) { "O payload precisa ser ELF64 little endian" }
        val b = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        require(b.getShort(18).toInt() == 62) { "Arquitetura ELF incompatível com PS4" }
    }

    private fun send(host: String, port: Int, bytes: ByteArray) {
        val timeoutExecutor = Executors.newSingleThreadScheduledExecutor()
        Socket().use { s ->
            val guard = timeoutExecutor.schedule({ runCatching { s.close() } }, 15, TimeUnit.SECONDS)
            try {
                s.connect(InetSocketAddress(host, port), 3000)
                s.tcpNoDelay = true
                s.getOutputStream().write(bytes); s.getOutputStream().flush()
                s.shutdownOutput()
            } finally {
                guard.cancel(false)
                timeoutExecutor.shutdownNow()
            }
        }
    }

    private fun isOpen(host: String, port: Int): Boolean = try {
        Socket().use { it.connect(InetSocketAddress(host, port), 700) }; true
    } catch (_: Exception) { false }

    private fun <T> await(timeout: Long, check: () -> T?): T? {
        val deadline = System.nanoTime() + timeout * 1_000_000
        while (System.nanoTime() < deadline) {
            val result = check()
            if (result != null && result != false) return result
            Thread.sleep(400)
        }
        return null
    }
}
