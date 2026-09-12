package com.tayson.ps4linksender.ps4

import com.tayson.ps4linksender.model.PkgInfo
import java.io.ByteArrayOutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.charset.StandardCharsets

object PayloadClient {
    const val PORT = 9096
    private const val CONNECT_TIMEOUT_MS = 2500
    private const val IO_TIMEOUT_MS = 5000

    data class ProbeResult(val version: String, val raw: String)
    data class StartResult(val raw: String)
    data class StatusResult(
        val state: String,
        val bytes: Long,
        val total: Long,
        val http: Int?,
        val error: Long,
        val install: Long,
        val titleId: String?,
        val fileName: String?,
        val raw: String
    )

    fun probe(host: String): ProbeResult {
        val line = exchange(host, "TAYSONP/1 PING\r\n\r\n")
        require(line.startsWith("TAYSONP/1 OK ")) { "Payload não respondeu corretamente: $line" }
        val version = field(line, "payload") ?: "?"
        return ProbeResult(version, line)
    }

    fun start(host: String, relayUrl: String, pkg: PkgInfo): StartResult {
        require(relayUrl.startsWith("http://")) { "O Payload 0.1.0 requer URL HTTP do Relay local" }

        // 0.1.0 has one fixed staging file. Reset it before a different package so
        // stale bytes can never be appended to a new PKG. A busy payload refuses RESET.
        val resetLine = exchange(host, "TAYSONP/1 RESET\r\n\r\n")
        if (!resetLine.startsWith("TAYSONP/1 OK ")) {
            val reason = resetLine.removePrefix("TAYSONP/1 ERR ").ifBlank { resetLine }
            throw IllegalStateException("Payload não pode iniciar outro trabalho: $reason")
        }

        val title = pkg.titleId.orEmpty()
        val command = buildString {
            append("TAYSONP/1 START\r\n")
            appendHeader("Url", relayUrl)
            appendHeader("Size", pkg.size.toString())
            appendHeader("Name", pkg.fileName)
            appendHeader("Title", title)
            append("\r\n")
        }
        val line = exchange(host, command)
        if (!line.startsWith("TAYSONP/1 ACCEPTED")) {
            val reason = line.removePrefix("TAYSONP/1 ERR ").ifBlank { line }
            throw IllegalStateException("Payload recusou o trabalho: $reason")
        }
        return StartResult(line)
    }

    fun status(host: String): StatusResult {
        val line = exchange(host, "TAYSONP/1 STATUS\r\n\r\n")
        require(line.startsWith("TAYSONP/1 STATUS ")) { "Status inválido do Payload: $line" }
        return StatusResult(
            state = field(line, "state") ?: "unknown",
            bytes = field(line, "bytes")?.toLongOrNull() ?: 0L,
            total = field(line, "total")?.toLongOrNull() ?: 0L,
            http = field(line, "http")?.toIntOrNull()?.takeIf { it > 0 },
            error = parseCode(field(line, "error")),
            install = parseCode(field(line, "install")),
            titleId = field(line, "title")?.takeIf { it != "-" },
            fileName = field(line, "file")?.takeIf { it != "-" },
            raw = line
        )
    }

    fun stop(host: String): String = exchange(host, "TAYSONP/1 STOP\r\n\r\n")
    fun reset(host: String): String = exchange(host, "TAYSONP/1 RESET\r\n\r\n")

    private fun exchange(host: String, payload: String): String {
        requireHost(host)
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, PORT), CONNECT_TIMEOUT_MS)
            socket.soTimeout = IO_TIMEOUT_MS
            socket.getOutputStream().apply {
                write(payload.toByteArray(StandardCharsets.UTF_8))
                flush()
            }
            return readAsciiLine(socket)
        }
    }

    private fun StringBuilder.appendHeader(name: String, raw: String) {
        val value = raw.replace("\r", " ").replace("\n", " ").trim()
        append(name).append(": ").append(value).append("\r\n")
    }

    private fun field(line: String, name: String): String? =
        Regex("(?:^|\\s)${Regex.escape(name)}=([^\\s]+)").find(line)?.groupValues?.getOrNull(1)

    private fun parseCode(value: String?): Long {
        if (value.isNullOrBlank()) return 0L
        return when {
            value.startsWith("0x", true) -> value.substring(2).toLongOrNull(16) ?: 0L
            else -> value.toLongOrNull() ?: 0L
        }
    }

    private fun readAsciiLine(socket: Socket): String {
        val input = socket.getInputStream()
        val out = ByteArrayOutputStream()
        while (out.size() < 4096) {
            val b = input.read()
            if (b < 0 || b == '\n'.code) break
            if (b != '\r'.code) out.write(b)
        }
        val line = out.toString(StandardCharsets.UTF_8.name()).trim()
        require(line.isNotBlank()) { "Payload fechou a conexão sem resposta" }
        return line
    }

    private fun requireHost(host: String) {
        require(host.isNotBlank()) { "Informe o IP do PS4" }
        require(!host.contains('/') && !host.contains(':')) { "Use somente o IPv4 do PS4" }
    }
}
