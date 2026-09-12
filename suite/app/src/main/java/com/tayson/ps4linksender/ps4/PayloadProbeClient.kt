package com.tayson.ps4linksender.ps4

import com.tayson.ps4linksender.model.PkgInfo
import java.io.ByteArrayOutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketTimeoutException
import java.nio.charset.StandardCharsets

object PayloadProbeClient {
    const val PORT = 9096
    data class Identity(val version: String, val pid: Int, val busy: Boolean, val stage: String)
    data class Result(val http: Int, val bytes: Int, val pkg: Boolean, val scheme: String)

    fun requireHost(host: String) {
        val parts = host.split('.')
        require(parts.size == 4 && parts.all { it.isNotEmpty() && it.all(Char::isDigit) && (it.toIntOrNull() ?: -1) in 0..255 }) {
            "Informe somente o IPv4 do PS4, por exemplo 192.168.0.10"
        }
        val p = parts.map(String::toInt)
        require(p[0] == 10 || p[0] == 192 && p[1] == 168 || p[0] == 172 && p[1] in 16..31 || p[0] == 127) {
            "O PS4 precisa estar na rede local"
        }
    }

    fun ping(host: String, port: Int = PORT, timeout: Int = 1200): Identity {
        requireHost(host)
        Socket().use { s ->
            s.connect(InetSocketAddress(host, port), timeout)
            s.soTimeout = timeout
            s.getOutputStream().write("TAYSON/1 PING\n".toByteArray(StandardCharsets.US_ASCII))
            val line = readLine(s)
            require(line.startsWith("TAYSON/1 OK ") && field(line, "caps") == "probe") { "Essa porta não respondeu como Tayson Payload" }
            return Identity(
                field(line, "payload") ?: error("Resposta sem versão do payload"),
                field(line, "pid")?.toIntOrNull() ?: error("Resposta sem processo"),
                field(line, "busy") == "1",
                field(line, "stage") ?: "unknown"
            )
        }
    }

    fun probe(host: String, pkg: PkgInfo, port: Int = PORT, onStage: (String) -> Unit = {}): Result {
        requireHost(host)
        val identity = ping(host, port)
        require(!identity.busy) { "Já existe um teste em andamento: ${identity.stage}. Consulte a resposta do payload antes de tentar novamente." }
        val packet = JobPacketCodec.encode(pkg)
        var stage = "conexão"
        try {
            Socket().use { s ->
                s.connect(InetSocketAddress(host, port), 3000)
                s.soTimeout = 20_000
                val deadline = System.nanoTime() + 90_000_000_000L
                val out = s.getOutputStream()
                out.write("TAYSON/1 PROBE ${packet.size}\n".toByteArray(StandardCharsets.US_ASCII))
                out.write(packet); out.flush()
                repeat(40) {
                    if (System.nanoTime() >= deadline) throw SocketTimeoutException()
                    val line = readLine(s)
                    when {
                        line.startsWith("TAYSON/1 STEP ") -> {
                            stage = field(line, "stage") ?: error("Etapa inválida")
                            onStage(stage)
                        }
                        line.startsWith("TAYSON/1 PROBE ") -> {
                            val http = field(line, "http")?.toIntOrNull() ?: error("Resposta sem HTTP")
                            val bytes = field(line, "bytes")?.toIntOrNull() ?: error("Resposta sem bytes")
                            val scheme = field(line, "scheme") ?: error("Resposta sem protocolo")
                            require(http in listOf(200, 206) && bytes == 16 && scheme in listOf("http", "https")) { "Resultado incompleto do payload" }
                            require(field(line, "pkg") in listOf("0", "1")) { "Resultado PKG inválido" }
                            return Result(http, bytes, field(line, "pkg") == "1", scheme)
                        }
                        line.startsWith("TAYSON/1 ERR ") -> error("Payload: ${line.removePrefix("TAYSON/1 ERR ")}")
                        else -> error("Resposta inesperada do payload")
                    }
                }
                error("Limite de respostas do payload excedido")
            }
        } catch (_: SocketTimeoutException) {
            error("Sem resposta na etapa $stage. Toque em TESTAR PAYLOAD para consultar se o processo continua ativo.")
        }
    }

    private fun field(line: String, key: String) =
        Regex("(?:^|\\s)${Regex.escape(key)}=([^\\s]+)").find(line)?.groupValues?.get(1)

    internal fun readLine(s: Socket): String {
        val out = ByteArrayOutputStream()
        val input = s.getInputStream()
        while (out.size() < 1024) {
            val b = input.read()
            require(b >= 0) { "Payload fechou a conexão antes de concluir a resposta" }
            if (b == 10) return out.toString(StandardCharsets.US_ASCII.name())
            require(b in 32..126) { "Resposta inválida do payload" }
            out.write(b)
        }
        error("Resposta do payload excede o limite")
    }
}
