package com.tayson.ps4linksender.ps4

import com.tayson.ps4linksender.model.PkgInfo
import java.io.ByteArrayOutputStream
import java.io.DataOutputStream
import java.nio.charset.StandardCharsets

object JobPacketCodec {
    private const val MAGIC = 0x54594A31
    private const val VERSION = 1
    fun encode(pkg: PkgInfo): ByteArray {
        require(pkg.size > 0) { "Tamanho do PKG inválido" }
        val ctx = pkg.requestContext
        val extras = buildString {
            ctx?.extraHeaders?.forEach { (rawName, rawValue) ->
                val name = rawName.trim()
                val value = rawValue.trim()
                if (!isSafeHeader(name, value)) return@forEach
                if (name.equals("Host", true) || name.equals("Content-Length", true) ||
                    name.equals("Range", true) || name.equals("Connection", true) ||
                    name.equals("User-Agent", true) || name.equals("Cookie", true) ||
                    name.equals("Referer", true) || name.equals("Accept-Encoding", true)) return@forEach
                append(name).append(": ").append(value).append('\n')
            }
        }

        val output = ByteArrayOutputStream()
        DataOutputStream(output).use { data ->
            data.writeInt(MAGIC)
            data.writeInt(VERSION)
            data.writeLong(pkg.size)
            writeString(data, pkg.finalUrl, 8191, "URL")
            writeString(data, ctx?.userAgent ?: "TaysonPS4LinkSender/1.5.0", 1023, "User-Agent")
            writeString(data, ctx?.referer.orEmpty(), 4095, "Referer")
            writeString(data, ctx?.cookie.orEmpty(), 8191, "Cookie")
            writeString(data, extras, 8191, "headers")
            writeString(data, pkg.contentId, 127, "Content ID")
            writeString(data, pkg.fileName, 511, "nome do arquivo")
            writeString(data, pkg.packageType, 31, "tipo do pacote")
        }
        return output.toByteArray()
    }

    private fun writeString(out: DataOutputStream, value: String, maxBytes: Int, label: String) {
        val bytes = value.toByteArray(StandardCharsets.UTF_8)
        require(bytes.size <= maxBytes) { "$label excede o limite do Receiver ($maxBytes bytes)" }
        out.writeInt(bytes.size)
        out.write(bytes)
    }

    private fun isSafeHeader(name: String, value: String): Boolean {
        if (name.isBlank() || value.isBlank()) return false
        if (name.any { it == '\r' || it == '\n' || it == ':' }) return false
        if (value.any { it == '\r' || it == '\n' }) return false
        return name.all { it.isLetterOrDigit() || it == '-' || it == '_' }
    }

}
