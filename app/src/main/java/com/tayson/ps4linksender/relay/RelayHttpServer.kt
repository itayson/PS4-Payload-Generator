package com.tayson.ps4linksender.relay

import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.io.IOException
import java.net.HttpURLConnection
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URI
import java.net.URL
import java.nio.charset.StandardCharsets
import java.util.Locale
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicLong

class RelayHttpServer(
    private val port: Int = 8787,
    private val registry: RelayRegistry = RelayRegistry,
    private val onTraffic: (Long) -> Unit = {}
) : Closeable {
    companion object {
        private const val MAX_HEADER_BYTES = 16 * 1024
        private const val MAX_REDIRECTS = 8
        private const val UA = "TaysonPS4LinkSender/0.4.0 Relay"
    }

    private val pool = Executors.newFixedThreadPool(8)
    private val bytesServed = AtomicLong(0)
    @Volatile private var running = false
    @Volatile private var server: ServerSocket? = null

    fun start() {
        if (running) return
        val ss = ServerSocket()
        ss.reuseAddress = true
        ss.bind(InetSocketAddress("0.0.0.0", port))
        server = ss
        running = true
        pool.execute {
            while (running) {
                try {
                    val socket = ss.accept()
                    pool.execute { handle(socket) }
                } catch (e: IOException) {
                    if (running) Thread.sleep(80)
                }
            }
        }
    }

    fun isRunning(): Boolean = running
    fun totalBytesServed(): Long = bytesServed.get()

    private fun handle(socket: Socket) {
        socket.use { s ->
            s.soTimeout = 30_000
            if (!isPrivateClient(s.inetAddress)) {
                writeSimple(s, 403, "Forbidden")
                return
            }
            val request = try { readRequest(s) } catch (_: Exception) {
                writeSimple(s, 400, "Bad Request"); return
            }
            if (request.method != "GET" && request.method != "HEAD") {
                writeSimple(s, 405, "Method Not Allowed", mapOf("Allow" to "GET, HEAD")); return
            }
            val token = extractToken(request.target)
            val entry = token?.let { registry.get(it) }
            if (entry == null) {
                writeSimple(s, 404, "Not Found"); return
            }
            if (request.method == "HEAD") {
                writeHeaders(
                    BufferedOutputStream(s.getOutputStream()), 200, "OK",
                    linkedMapOf(
                        "Content-Length" to entry.size.toString(),
                        "Content-Type" to "application/octet-stream",
                        "Accept-Ranges" to "bytes",
                        "Cache-Control" to "no-store",
                        "Connection" to "close"
                    )
                )
                return
            }

            val range = normalizeRange(request.headers["range"])
            if (request.headers.containsKey("range") && range == null) {
                writeSimple(s, 416, "Range Not Satisfiable", mapOf("Content-Range" to "bytes */${entry.size}")); return
            }
            proxyGet(s, entry, range)
        }
    }

    private fun proxyGet(socket: Socket, entry: RelayRegistry.Entry, range: String?) {
        var current = entry.upstreamUrl
        var conn: HttpURLConnection? = null
        var headersSent = false
        try {
            for (redirect in 0..MAX_REDIRECTS) {
                conn = (URL(current).openConnection() as HttpURLConnection).apply {
                    instanceFollowRedirects = false
                    connectTimeout = 20_000
                    readTimeout = 45_000
                    requestMethod = "GET"
                    setRequestProperty("User-Agent", UA)
                    setRequestProperty("Accept", "*/*")
                    setRequestProperty("Accept-Encoding", "identity")
                    setRequestProperty("Connection", "close")
                    if (range != null) setRequestProperty("Range", range)
                }
                val code = conn.responseCode
                if (code in 300..399) {
                    val location = conn.getHeaderField("Location") ?: throw IOException("Redirect sem Location")
                    if (redirect == MAX_REDIRECTS) throw IOException("Muitos redirects")
                    current = validateRedirect(current, location)
                    conn.disconnect(); conn = null
                    continue
                }
                if (code !in listOf(200, 206)) {
                    val reason = conn.responseMessage ?: "Upstream Error"
                    writeSimple(socket, code.coerceIn(400, 599), reason)
                    return
                }
                if (range != null && rangeStart(range) > 0L && code != 206) {
                    writeSimple(socket, 502, "Upstream ignored Range")
                    return
                }

                val upstreamLength = conn.getHeaderFieldLong("Content-Length", -1L)
                val contentRange = conn.getHeaderField("Content-Range")
                val responseLength = when {
                    upstreamLength > 0 -> upstreamLength
                    code == 200 -> entry.size
                    else -> parseRangeLength(contentRange) ?: -1L
                }
                if (responseLength <= 0) throw IOException("Upstream sem Content-Length")

                val headers = linkedMapOf(
                    "Content-Length" to responseLength.toString(),
                    "Content-Type" to (conn.contentType?.substringBefore(';') ?: "application/octet-stream"),
                    "Accept-Ranges" to "bytes",
                    "Cache-Control" to "no-store",
                    "Connection" to "close"
                )
                if (code == 206 && !contentRange.isNullOrBlank()) headers["Content-Range"] = contentRange

                val out = BufferedOutputStream(socket.getOutputStream(), 128 * 1024)
                writeHeaders(out, code, if (code == 206) "Partial Content" else "OK", headers)
                headersSent = true
                val input = BufferedInputStream(conn.inputStream, 128 * 1024)
                input.use {
                    val buf = ByteArray(128 * 1024)
                    var sinceFlush = 0
                    while (true) {
                        val n = it.read(buf)
                        if (n < 0) break
                        out.write(buf, 0, n)
                        bytesServed.addAndGet(n.toLong())
                        onTraffic(n.toLong())
                        sinceFlush += n
                        if (sinceFlush >= 1024 * 1024) { out.flush(); sinceFlush = 0 }
                    }
                }
                out.flush()
                return
            }
        } catch (_: Exception) {
            if (!headersSent) runCatching { writeSimple(socket, 502, "Relay Upstream Error") }
        } finally {
            conn?.disconnect()
        }
    }

    private data class Request(val method: String, val target: String, val headers: Map<String, String>)

    private fun readRequest(socket: Socket): Request {
        val input = socket.getInputStream()
        val out = ByteArrayOutputStream()
        var state = 0
        while (out.size() < MAX_HEADER_BYTES) {
            val b = input.read()
            if (b < 0) break
            out.write(b)
            state = when {
                state == 0 && b == '\r'.code -> 1
                state == 1 && b == '\n'.code -> 2
                state == 2 && b == '\r'.code -> 3
                state == 3 && b == '\n'.code -> 4
                else -> if (b == '\r'.code) 1 else 0
            }
            if (state == 4) break
        }
        if (state != 4) throw IOException("Cabeçalho HTTP incompleto")
        val text = out.toString(StandardCharsets.ISO_8859_1.name())
        val lines = text.split("\r\n")
        val first = lines.firstOrNull()?.split(' ') ?: emptyList()
        if (first.size < 3) throw IOException("Request line inválida")
        val headers = linkedMapOf<String, String>()
        lines.drop(1).forEach { line ->
            val p = line.indexOf(':')
            if (p > 0) headers[line.substring(0, p).trim().lowercase(Locale.US)] = line.substring(p + 1).trim()
        }
        return Request(first[0].uppercase(Locale.US), first[1], headers)
    }

    private fun extractToken(target: String): String? {
        return try {
            val path = URI("http://relay$target").path ?: return null
            val parts = path.split('/').filter { it.isNotBlank() }
            if (parts.size >= 3 && parts[0] == "pkg") parts[1] else null
        } catch (_: Exception) { null }
    }

    private fun normalizeRange(value: String?): String? {
        if (value == null) return null
        val v = value.trim()
        return if (Regex("bytes=\\d+-\\d*", RegexOption.IGNORE_CASE).matches(v)) v else null
    }

    private fun rangeStart(value: String): Long = value.substringAfter('=').substringBefore('-').toLongOrNull() ?: 0L

    private fun parseRangeLength(contentRange: String?): Long? {
        val m = Regex("bytes\\s+(\\d+)-(\\d+)/(?:\\d+|\\*)", RegexOption.IGNORE_CASE).find(contentRange ?: "") ?: return null
        val a = m.groupValues[1].toLongOrNull() ?: return null
        val b = m.groupValues[2].toLongOrNull() ?: return null
        return if (b >= a) b - a + 1 else null
    }

    private fun validateRedirect(base: String, location: String): String {
        val next = URL(URL(base), location).toString()
        val uri = URI(next)
        require(uri.scheme.equals("http", true) || uri.scheme.equals("https", true)) { "Redirect não HTTP" }
        require(!uri.host.isNullOrBlank()) { "Redirect sem host" }
        require(uri.userInfo.isNullOrBlank()) { "Redirect com credenciais embutidas" }
        return next
    }

    private fun isPrivateClient(address: InetAddress): Boolean =
        address.isLoopbackAddress || address.isSiteLocalAddress || (address is Inet4Address && address.isLinkLocalAddress)

    private fun writeSimple(socket: Socket, code: Int, reason: String, extra: Map<String, String> = emptyMap()) {
        val body = "$code $reason\n".toByteArray(StandardCharsets.UTF_8)
        val headers = linkedMapOf(
            "Content-Type" to "text/plain; charset=utf-8",
            "Content-Length" to body.size.toString(),
            "Connection" to "close"
        )
        headers.putAll(extra)
        val out = BufferedOutputStream(socket.getOutputStream())
        writeHeaders(out, code, reason, headers)
        out.write(body)
        out.flush()
    }

    private fun writeHeaders(out: BufferedOutputStream, code: Int, reason: String, headers: Map<String, String>) {
        val head = buildString {
            append("HTTP/1.1 $code $reason\r\n")
            headers.forEach { (k, v) -> append(k).append(": ").append(v).append("\r\n") }
            append("\r\n")
        }.toByteArray(StandardCharsets.ISO_8859_1)
        out.write(head)
        out.flush()
    }

    override fun close() {
        running = false
        runCatching { server?.close() }
        pool.shutdownNow()
    }
}
