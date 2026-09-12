package com.tayson.ps4linksender.ps4

import com.tayson.ps4linksender.model.PkgInfo
import java.io.ByteArrayOutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.charset.StandardCharsets

object ReceiverClient {
    const val PORT = 9095
    private const val CONNECT_TIMEOUT_MS = 1800
    private const val IO_TIMEOUT_MS = 45_000

    data class ProbeResult(val version: String, val raw: String)
    data class SendResult(val jobId: Int?, val taskId: Int?, val upstreamHttp: Int?, val raw: String)
    data class JobStatus(
        val jobId: Int,
        val taskId: Int?,
        val size: Long,
        val frontier: Long,
        val served: Long,
        val activeConnections: Int,
        val upstreamHttp: Int?,
        val lastError: Int,
        val state: String,
        val contentId: String
    )
    data class StatusResult(val version: String, val jobs: List<JobStatus>)

    fun probe(host: String, timeoutMs: Int = CONNECT_TIMEOUT_MS): ProbeResult {
        requireHost(host)
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, PORT), timeoutMs)
            socket.soTimeout = maxOf(timeoutMs, 900)
            socket.getOutputStream().write("TAYSON/1 PING\n".toByteArray(StandardCharsets.US_ASCII))
            socket.getOutputStream().flush()
            val line = readAsciiLine(socket)
            require(line.startsWith("TAYSON/1 OK ")) { "Resposta não pertence ao Tayson Receiver" }
            val version = Regex("receiver=([^\\s]+)").find(line)?.groupValues?.getOrNull(1) ?: "desconhecida"
            return ProbeResult(version, line)
        }
    }

    fun status(host: String): StatusResult {
        requireHost(host)
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, PORT), CONNECT_TIMEOUT_MS)
            socket.soTimeout = 8_000
            val out = socket.getOutputStream()
            out.write("TAYSON/1 STATUS\n".toByteArray(StandardCharsets.US_ASCII))
            out.flush()
            val first = readAsciiLine(socket)
            if (!first.startsWith("TAYSON/1 STATUS ")) {
                val reason = first.removePrefix("TAYSON/1 ERR ").ifBlank { first }
                if (reason == "bad-command") {
                    throw IllegalStateException("O monitoramento exige o Tayson PS4 Receiver 1.1.0 ou superior.")
                }
                throw IllegalStateException(ReceiverErrorMapper.toUserMessage(reason))
            }
            val version = field(first, "receiver") ?: "desconhecida"
            val count = field(first, "count")?.toIntOrNull() ?: 0
            require(count in 0..32) { "Resposta de status inválida" }
            val jobs = ArrayList<JobStatus>(count)
            repeat(count) {
                val line = readAsciiLine(socket)
                require(line.startsWith("TAYSON/1 JOB ")) { "Resposta de job inválida" }
                val id = field(line, "id")?.toIntOrNull() ?: error("Status sem job id")
                val task = field(line, "task")?.toIntOrNull()?.takeIf { it >= 0 }
                val size = field(line, "size")?.toLongOrNull() ?: 0L
                val frontier = field(line, "frontier")?.toLongOrNull() ?: 0L
                val served = field(line, "served")?.toLongOrNull() ?: 0L
                val active = field(line, "active")?.toIntOrNull() ?: 0
                val upstream = field(line, "upstream")?.toIntOrNull()?.takeIf { it > 0 }
                val error = field(line, "error")?.toLongOrNull()?.toInt() ?: 0
                val state = field(line, "state") ?: "unknown"
                val content = field(line, "content") ?: ""
                jobs += JobStatus(id, task, size, frontier, served, active, upstream, error, state, content)
            }
            val end = readAsciiLine(socket)
            require(end == "TAYSON/1 END") { "Resposta de status incompleta" }
            return StatusResult(version, jobs)
        }
    }

    fun sendJob(host: String, pkg: PkgInfo): SendResult {
        requireHost(host)
        val packet = JobPacketCodec.encode(pkg)
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, PORT), CONNECT_TIMEOUT_MS)
            socket.soTimeout = IO_TIMEOUT_MS
            val out = socket.getOutputStream()
            out.write("TAYSON/1 JOB ${packet.size}\n".toByteArray(StandardCharsets.US_ASCII))
            out.write(packet)
            out.flush()
            val line = readAsciiLine(socket)
            if (!line.startsWith("TAYSON/1 OK ")) {
                val reason = line.removePrefix("TAYSON/1 ERR ").ifBlank { line }
                throw IllegalStateException(ReceiverErrorMapper.toUserMessage(reason))
            }
            return SendResult(
                jobId = Regex("job=(\\d+)").find(line)?.groupValues?.getOrNull(1)?.toIntOrNull(),
                taskId = Regex("task=(-?\\d+)").find(line)?.groupValues?.getOrNull(1)?.toIntOrNull(),
                upstreamHttp = Regex("upstream=(\\d+)").find(line)?.groupValues?.getOrNull(1)?.toIntOrNull(),
                raw = line
            )
        }
    }

    private fun field(line: String, name: String): String? =
        Regex("(?:^|\\s)${Regex.escape(name)}=([^\\s]+)").find(line)?.groupValues?.getOrNull(1)

    private fun readAsciiLine(socket: Socket): String {
        val input = socket.getInputStream()
        val out = ByteArrayOutputStream()
        while (out.size() < 2048) {
            val b = input.read()
            if (b < 0) break
            if (b == '\n'.code) break
            if (b != '\r'.code) out.write(b)
        }
        val line = out.toString(StandardCharsets.UTF_8.name()).trim()
        require(line.isNotBlank()) { "Receiver fechou a conexão sem resposta" }
        return line
    }

    private fun requireHost(host: String) {
        require(host.isNotBlank()) { "Informe o IP do PS4" }
        require(!host.contains('/') && !host.contains(':')) { "Use somente o IPv4 do PS4" }
    }
}
