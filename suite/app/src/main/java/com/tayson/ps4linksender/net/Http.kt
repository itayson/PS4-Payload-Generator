package com.tayson.ps4linksender.net

import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL
import java.net.URLEncoder
import java.nio.charset.StandardCharsets

object Http {
    private const val BROWSER_UA = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/124 Mobile Safari/537.36 TaysonPS4LinkSender/1.0.1"
    const val DIRECT_UA = "TaysonPS4LinkSender/1.0.1"
    private const val MAX_REDIRECTS = 8

    data class SmallResponse(
        val code: Int,
        val finalUrl: String,
        val headers: Map<String, List<String>>,
        val bytes: ByteArray
    ) {
        fun header(name: String): String? = headers.entries
            .firstOrNull { it.key.equals(name, ignoreCase = true) }
            ?.value?.firstOrNull()
    }

    fun requireHttpUrl(url: String): String {
        val trimmed = url.trim()
        val u = try { URI(trimmed) } catch (_: Exception) { throw IllegalArgumentException("URL inválida") }
        val scheme = u.scheme?.lowercase()
        require(scheme == "http" || scheme == "https") { "Use somente URLs http:// ou https://" }
        require(!u.host.isNullOrBlank()) { "URL sem host" }
        require(u.userInfo.isNullOrBlank()) { "URLs com usuário/senha embutidos não são aceitas" }
        return trimmed
    }

    fun getSmall(url: String, maxBytes: Int = 4096, range: String? = null, userAgent: String = DIRECT_UA, extraHeaders: Map<String, String> = emptyMap()): SmallResponse {
        require(maxBytes in 1..1024 * 1024) { "Leitura deve ter entre 1 byte e 1 MiB" }
        var current = requireHttpUrl(url)
        val originHost = try { URI(current).host.orEmpty() } catch (_: Exception) { "" }
        repeat(MAX_REDIRECTS + 1) { redirectCount ->
            val conn = (URL(current).openConnection() as HttpURLConnection).apply {
                instanceFollowRedirects = false
                connectTimeout = 15000
                readTimeout = 15000
                requestMethod = "GET"
                setRequestProperty("User-Agent", userAgent)
                setRequestProperty("Accept", "*/*")
                setRequestProperty("Accept-Encoding", "identity")
                if (range != null) setRequestProperty("Range", range)
                val currentHost = try { URI(current).host.orEmpty() } catch (_: Exception) { "" }
                extraHeaders.forEach { (name, value) ->
                    if (value.isBlank()) return@forEach
                    if (name.equals("Host", true) || name.equals("Content-Length", true) || name.equals("Range", true) || name.equals("User-Agent", true)) return@forEach
                    if (name.equals("Cookie", true) && originHost.isNotBlank() && !currentHost.equals(originHost, true)) return@forEach
                    setRequestProperty(name, value)
                }
            }
            val code = conn.responseCode
            if (code in 300..399) {
                val location = conn.getHeaderField("Location") ?: run {
                    conn.disconnect(); throw IllegalStateException("Redirecionamento sem Location")
                }
                if (redirectCount >= MAX_REDIRECTS) {
                    conn.disconnect(); throw IllegalStateException("Muitos redirecionamentos")
                }
                val next = URL(URL(current), location).toString()
                current = requireHttpUrl(next)
                conn.disconnect()
                return@repeat
            }
            val stream = try { conn.inputStream } catch (_: Exception) { conn.errorStream }
                ?: run { conn.disconnect(); throw IllegalStateException("HTTP $code sem corpo") }
            val out = ByteArrayOutputStream(minOf(maxBytes, 8192))
            val buf = ByteArray(4096)
            var total = 0
            stream.use {
                while (total < maxBytes) {
                    val n = it.read(buf, 0, minOf(buf.size, maxBytes - total))
                    if (n <= 0) break
                    out.write(buf, 0, n)
                    total += n
                }
            }
            val headers = conn.headerFields.entries
                .filter { it.key != null }
                .associate { it.key!! to (it.value ?: emptyList()) }
            conn.disconnect()
            return SmallResponse(code, current, headers, out.toByteArray())
        }
        throw IllegalStateException("Muitos redirecionamentos")
    }

    fun getRange(url: String, start: Long, length: Int, userAgent: String = DIRECT_UA, extraHeaders: Map<String, String> = emptyMap()): SmallResponse {
        require(start >= 0) { "Offset HTTP Range inválido" }
        require(length in 1..1024 * 1024) { "Range deve ter entre 1 byte e 1 MiB" }
        val end = start + length - 1L
        val r = getSmall(url, maxBytes = length, range = "bytes=$start-$end", userAgent = userAgent, extraHeaders = extraHeaders)
        if (start > 0) {
            if (r.code != 206) throw IllegalStateException("Servidor ignorou HTTP Range para offset $start")
            val cr = r.header("Content-Range") ?: throw IllegalStateException("Resposta parcial sem Content-Range")
            val actualStart = Regex("bytes\\s+(\\d+)-", RegexOption.IGNORE_CASE)
                .find(cr)?.groupValues?.getOrNull(1)?.toLongOrNull()
            if (actualStart != start) throw IllegalStateException("Servidor devolveu Range diferente do solicitado")
        }
        return r
    }

    fun getText(url: String, maxBytes: Int = 256 * 1024): String =
        getSmall(url, maxBytes, userAgent = BROWSER_UA).bytes.toString(StandardCharsets.UTF_8)

    fun postForm(url: String, fields: Map<String, String>, maxBytes: Int = 128 * 1024): String {
        val endpoint = requireHttpUrl(url)
        val body = fields.entries.joinToString("&") {
            URLEncoder.encode(it.key, "UTF-8") + "=" + URLEncoder.encode(it.value, "UTF-8")
        }.toByteArray(StandardCharsets.UTF_8)
        val conn = (URL(endpoint).openConnection() as HttpURLConnection).apply {
            instanceFollowRedirects = true
            connectTimeout = 15000
            readTimeout = 15000
            requestMethod = "POST"
            doOutput = true
            setRequestProperty("User-Agent", BROWSER_UA)
            setRequestProperty("Accept", "application/json")
            setRequestProperty("Content-Type", "application/x-www-form-urlencoded")
            setFixedLengthStreamingMode(body.size)
        }
        conn.outputStream.use { it.write(body) }
        val code = conn.responseCode
        val stream = try { conn.inputStream } catch (_: Exception) { conn.errorStream }
            ?: run { conn.disconnect(); throw IllegalStateException("HTTP $code sem resposta") }
        val out = ByteArrayOutputStream()
        val buf = ByteArray(4096)
        stream.use {
            while (out.size() < maxBytes) {
                val n = it.read(buf, 0, minOf(buf.size, maxBytes - out.size()))
                if (n <= 0) break
                out.write(buf, 0, n)
            }
        }
        conn.disconnect()
        return out.toString("UTF-8")
    }
}
