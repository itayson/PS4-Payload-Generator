package com.tayson.ps4linksender.pkg

import com.tayson.ps4linksender.model.PkgInfo
import com.tayson.ps4linksender.model.RequestContext
import com.tayson.ps4linksender.model.SfoInfo
import com.tayson.ps4linksender.net.Http
import java.net.URI
import java.net.URLDecoder
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.charset.StandardCharsets

object PkgScanner {
    private const val PKG_PARAM_SFO_ID = 0x00001000
    private const val PKG_ENTRY_SIZE = 0x20
    private const val MAX_ENTRY_COUNT = 4096
    private const val MAX_SFO_SIZE = 1024 * 1024
    private val titleRegex = Regex("[A-Z]{4}[0-9]{5}")

    fun scan(url: String, requestContext: RequestContext? = null): PkgInfo {
        val userAgent = requestContext?.userAgent?.takeIf { it.isNotBlank() } ?: Http.DIRECT_UA
        val extraHeaders = buildMap<String, String> {
            requestContext?.extraHeaders?.forEach { (k, v) ->
                if (k.isNotBlank() && v.isNotBlank()) put(k, v)
            }
            requestContext?.referer?.takeIf { it.isNotBlank() }?.let { put("Referer", it) }
            requestContext?.cookie?.takeIf { it.isNotBlank() }?.let { put("Cookie", it) }
        }
        val response = Http.getSmall(url, maxBytes = 4096, range = "bytes=0-4095", userAgent = userAgent, extraHeaders = extraHeaders)
        if (response.code !in listOf(200, 206)) {
            throw IllegalStateException("Servidor respondeu HTTP ${response.code}")
        }
        val mime = response.header("Content-Type")
        if (looksLikeHtml(response.bytes, mime)) {
            throw IllegalStateException("A URL ainda devolve HTML, não um PKG direto")
        }
        if (response.bytes.size < 0x80) throw IllegalStateException("Resposta curta demais para um cabeçalho PKG")
        val magic = response.bytes.copyOfRange(0, 4)
        if (!(magic[0] == 0x7f.toByte() && magic[1] == 'C'.code.toByte() && magic[2] == 'N'.code.toByte() && magic[3] == 'T'.code.toByte())) {
            throw IllegalStateException("Magic inválido: a URL não parece apontar para um PKG de PS4")
        }

        val contentId = asciiZ(response.bytes, 0x40, 0x30)
        if (contentId.isBlank()) throw IllegalStateException("Content ID não encontrado no cabeçalho")
        val headerTitleId = titleRegex.find(contentId)?.value
        val contentType = beU32(response.bytes, 0x74)
        val contentFlags = beU32(response.bytes, 0x78).toLong() and 0xffffffffL
        val packageType = when (contentType) {
            0x1A -> "PS4GD"
            0x1B -> "PS4AC"
            0x1C -> "PS4AL"
            0x1E -> "PS4DP"
            0x1F -> "PS4MD"
            else -> throw IllegalStateException("Content type PS4 não suportado: 0x${contentType.toString(16)}")
        }
        val totalSize = parseTotalSize(response)
        if (totalSize <= 0) throw IllegalStateException("O servidor não informou o tamanho total do arquivo")
        val rangeSupported = response.code == 206 || response.header("Accept-Ranges")?.contains("bytes", true) == true
        val rawName = parseFileName(response.header("Content-Disposition"), response.finalUrl)

        val sfo = if (rangeSupported) {
            tryReadParamSfo(response.finalUrl, response.bytes, userAgent, extraHeaders)
        } else null

        val finalTitleId = sfo?.titleId ?: headerTitleId
        val name = sanitizeFileName(rawName, finalTitleId)
        return PkgInfo(
            sourceUrl = url,
            finalUrl = response.finalUrl,
            fileName = name,
            size = totalSize,
            contentId = contentId,
            titleId = finalTitleId,
            packageType = packageType,
            contentTypeValue = contentType,
            contentFlags = contentFlags,
            rangeSupported = rangeSupported,
            httpCode = response.code,
            mimeType = mime,
            sfo = sfo,
            requestContext = requestContext
        )
    }

    private fun tryReadParamSfo(url: String, firstChunk: ByteArray, userAgent: String, extraHeaders: Map<String, String>): SfoInfo? {
        if (firstChunk.size < 0x20) return null
        val entryCount = beU32(firstChunk, 0x10)
        val tableOffset = beU32(firstChunk, 0x18).toLong() and 0xffffffffL
        if (entryCount <= 0 || entryCount > MAX_ENTRY_COUNT || tableOffset <= 0) return null

        val tableSizeLong = entryCount.toLong() * PKG_ENTRY_SIZE
        if (tableSizeLong > 1024 * 1024) return null
        val tableSize = tableSizeLong.toInt()

        val table = if (tableOffset < firstChunk.size.toLong() && tableOffset + tableSize.toLong() <= firstChunk.size.toLong()) {
            firstChunk.copyOfRange(tableOffset.toInt(), tableOffset.toInt() + tableSize)
        } else {
            Http.getRange(url, tableOffset, tableSize, userAgent, extraHeaders).bytes
        }
        if (table.size < tableSize) return null

        var sfoOffset = -1L
        var sfoSize = -1
        for (i in 0 until entryCount) {
            val off = i * PKG_ENTRY_SIZE
            val id = beU32(table, off)
            if (id == PKG_PARAM_SFO_ID) {
                val flags1 = beU32(table, off + 0x08).toLong() and 0xffffffffL
                if ((flags1 and 0x80000000L) != 0L) return null
                sfoOffset = beU32(table, off + 0x10).toLong() and 0xffffffffL
                sfoSize = beU32(table, off + 0x14)
                break
            }
        }
        if (sfoOffset < 0 || sfoSize <= 0 || sfoSize > MAX_SFO_SIZE) return null
        val sfoBytes = Http.getRange(url, sfoOffset, sfoSize, userAgent, extraHeaders).bytes
        return parseSfo(sfoBytes)
    }

    private fun parseSfo(bytes: ByteArray): SfoInfo? {
        if (bytes.size < 20) return null
        val le = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        val magic = le.getInt(0)
        if (magic != 0x46535000) return null // \0PSF
        val keyTableOffset = le.getInt(8)
        val dataTableOffset = le.getInt(12)
        val count = le.getInt(16)
        if (keyTableOffset !in 0 until bytes.size || dataTableOffset !in 0 until bytes.size || count !in 1..4096) return null
        if (20L + count.toLong() * 16L > bytes.size) return null

        val values = LinkedHashMap<String, Any>()
        for (i in 0 until count) {
            val e = 20 + i * 16
            val keyOffset = u16le(bytes, e)
            val format = u16le(bytes, e + 2)
            val dataLen = le.getInt(e + 4)
            val dataOffset = le.getInt(e + 12)
            val keyPos = keyTableOffset + keyOffset
            val valuePos = dataTableOffset + dataOffset
            if (keyPos !in bytes.indices || valuePos !in 0..bytes.size || dataLen < 0 || valuePos.toLong() + dataLen > bytes.size) continue
            val key = asciiZ(bytes, keyPos, minOf(256, bytes.size - keyPos))
            if (key.isBlank()) continue
            when (format) {
                0x0404 -> if (dataLen >= 4 && valuePos + 4 <= bytes.size) values[key] = le.getInt(valuePos).toLong() and 0xffffffffL
                0x0204, 0x0004 -> values[key] = utf8Z(bytes, valuePos, dataLen)
                else -> if (dataLen in 1..4096) {
                    val text = utf8Z(bytes, valuePos, dataLen)
                    if (text.isNotBlank()) values[key] = text
                }
            }
        }

        fun str(k: String): String? = (values[k] as? String)?.takeIf { it.isNotBlank() }
        fun num(k: String): Long? = values[k] as? Long
        return SfoInfo(
            title = str("TITLE") ?: str("TITLE_01") ?: str("TITLE_00"),
            titleId = str("TITLE_ID") ?: str("TITLEID"),
            appVersion = str("APP_VER"),
            version = str("VERSION"),
            category = str("CATEGORY"),
            contentId = str("CONTENT_ID"),
            systemVersion = num("SYSTEM_VER")
        )
    }

    private fun looksLikeHtml(bytes: ByteArray, mime: String?): Boolean {
        if (mime?.contains("text/html", true) == true) return true
        val prefix = bytes.copyOfRange(0, minOf(bytes.size, 256)).toString(StandardCharsets.UTF_8).trimStart().lowercase()
        return prefix.startsWith("<!doctype html") || prefix.startsWith("<html")
    }

    private fun asciiZ(bytes: ByteArray, off: Int, len: Int): String {
        if (off < 0 || off >= bytes.size || len <= 0) return ""
        val endLimit = minOf(bytes.size, off + len)
        val end = (off until endLimit).firstOrNull { bytes[it] == 0.toByte() } ?: endLimit
        return bytes.copyOfRange(off, end).toString(StandardCharsets.US_ASCII).trim()
    }

    private fun utf8Z(bytes: ByteArray, off: Int, len: Int): String {
        if (off < 0 || off >= bytes.size || len <= 0) return ""
        val endLimit = minOf(bytes.size, off + len)
        val end = (off until endLimit).firstOrNull { bytes[it] == 0.toByte() } ?: endLimit
        return bytes.copyOfRange(off, end).toString(StandardCharsets.UTF_8).trim()
    }

    private fun u16le(bytes: ByteArray, off: Int): Int =
        (bytes[off].toInt() and 0xff) or ((bytes[off + 1].toInt() and 0xff) shl 8)

    private fun beU32(bytes: ByteArray, off: Int): Int =
        ByteBuffer.wrap(bytes, off, 4).order(ByteOrder.BIG_ENDIAN).int

    private fun parseTotalSize(response: Http.SmallResponse): Long {
        val cr = response.header("Content-Range")
        if (cr != null) {
            Regex("/(\\d+)$").find(cr)?.groupValues?.getOrNull(1)?.toLongOrNull()?.let { return it }
        }
        return response.header("Content-Length")?.toLongOrNull() ?: -1L
    }

    private fun sanitizeFileName(raw: String, titleId: String?): String {
        var name = raw.substringAfterLast('/').replace(Regex("[\\/:*?\"<>|\u0000-\u001F]"), "_").trim()
        if (name.isBlank() || name.equals("download", true) || name.length > 180) {
            name = (titleId ?: "package") + ".pkg"
        }
        if (!name.lowercase().endsWith(".pkg")) name += ".pkg"
        return name
    }

    private fun parseFileName(contentDisposition: String?, url: String): String {
        if (contentDisposition != null) {
            Regex("filename\\*=UTF-8''([^;]+)", RegexOption.IGNORE_CASE).find(contentDisposition)?.groupValues?.getOrNull(1)?.let {
                return URLDecoder.decode(it, "UTF-8")
            }
            Regex("filename=\\\"?([^\\\";]+)", RegexOption.IGNORE_CASE).find(contentDisposition)?.groupValues?.getOrNull(1)?.let {
                return it.trim()
            }
        }
        return try {
            val p = URI(url).path.substringAfterLast('/').ifBlank { "package.pkg" }
            URLDecoder.decode(p, "UTF-8").ifBlank { "package.pkg" }
        } catch (_: Exception) { "package.pkg" }
    }
}
