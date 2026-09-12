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
        require(relayUrl.startsWith("http://")) { "O Payload requer URL HTTP do Relay local" }

        // START is intentionally clean: remove stale bytes from a different package.
        val resetLine = exchange(host, "TAYSONP/1 RESET\r\n\r\n")
        if (!resetLine.startsWith("TAYSONP/1 OK ")) {
            val reason = resetLine.removePrefix("TAYSONP/1 ERR ").ifBlank { resetLine }
            throw IllegalStateException("Payload não pode iniciar outro trabalho: $reason")
        }
        return sendJobCommand(host, "START", relayUrl, pkg)
    }

    fun resume(host: String, relayUrl: String, pkg: PkgInfo): StartResult {
        require(relayUrl.startsWith("http://")) { "O Payload requer URL HTTP do Relay local" }
        // RESUME never sends RESET: current.pkg is deliberately preserved.
        return sendJobCommand(host, "RESUME", relayUrl, pkg)
    }

    private fun sendJobCommand(host: String, verb: String, relayUrl: String, pkg: PkgInfo): StartResult {
        val title = pkg.titleId.orEmpty()
        val command = buildString {
            append("TAYSONP/1 $verb\r\n")
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

    fun direct(host: String, pkg: PkgInfo, resume: Boolean = false): StartResult {
        val verb = if (resume) "DIRECTRESUME" else "DIRECT"
        val ctx = pkg.requestContext
        val command = buildString {
            append("TAYSONP/1 $verb\r\n")
            appendHeader("Url", pkg.finalUrl)
            appendHeader("Size", pkg.size.toString())
            appendHeader("Name", pkg.fileName)
            appendHeader("Title", pkg.titleId.orEmpty())
            ctx?.userAgent?.takeIf { it.isNotBlank() }?.let { appendHeader("User-Agent", it) }
            ctx?.referer?.takeIf { it.isNotBlank() }?.let { appendHeader("Referer", it) }
            ctx?.cookie?.takeIf { it.isNotBlank() }?.let { appendHeader("Cookie", it) }
            ctx?.extraHeaders?.forEach { (name, value) ->
                val safe = name.trim()
                if (safe.matches(Regex("[A-Za-z0-9_-]{1,64}")) && value.isNotBlank()) {
                    appendHeader("Upstream-$safe", value)
                }
            }
            append("\r\n")
        }
        val line = exchange(host, command, IO_TIMEOUT_MS)
        if (!line.startsWith("TAYSONP/1 ACCEPTED")) {
            val reason = line.removePrefix("TAYSONP/1 ERR ").ifBlank { line }
            throw IllegalStateException("Payload recusou o modo direto: $reason")
        }
        return StartResult(line)
    }

    fun supportsDirect(version: String): Boolean {
        val parts = version.split('.').map { it.toIntOrNull() ?: 0 }
        val major = parts.getOrElse(0) { 0 }
        val minor = parts.getOrElse(1) { 0 }
        return major > 0 || minor >= 2
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
    fun exit(host: String): String = exchange(host, "TAYSONP/1 EXIT\r\n\r\n")

    private fun exchange(host: String, payload: String, ioTimeoutMs: Int = IO_TIMEOUT_MS): String {
        requireHost(host)
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, PORT), CONNECT_TIMEOUT_MS)
            socket.soTimeout = ioTimeoutMs
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
