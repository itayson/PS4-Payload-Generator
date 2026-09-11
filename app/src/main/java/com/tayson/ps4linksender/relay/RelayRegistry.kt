package com.tayson.ps4linksender.relay

import java.net.URLEncoder
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

object RelayRegistry {
    data class Entry(
        val token: String,
        val upstreamUrl: String,
        val fileName: String,
        val size: Long,
        val createdAt: Long = System.currentTimeMillis()
    )

    private val entries = ConcurrentHashMap<String, Entry>()

    fun register(upstreamUrl: String, fileName: String, size: Long): Entry {
        require(upstreamUrl.startsWith("http://") || upstreamUrl.startsWith("https://")) { "Relay aceita somente HTTP/HTTPS" }
        require(size > 0) { "Tamanho do PKG inválido para Relay" }
        prune()
        val token = UUID.randomUUID().toString().replace("-", "")
        val safeName = sanitizeName(fileName)
        return Entry(token, upstreamUrl.substringBefore('#'), safeName, size).also { entries[token] = it }
    }

    fun get(token: String): Entry? = entries[token]

    fun count(): Int = entries.size

    fun clear() = entries.clear()

    fun buildPath(entry: Entry): String = "/pkg/${entry.token}/${encodePathSegment(entry.fileName)}"

    private fun encodePathSegment(value: String): String =
        URLEncoder.encode(value, "UTF-8").replace("+", "%20")

    private fun sanitizeName(raw: String): String {
        var n = raw.replace(Regex("[\\\\/:*?\"<>|\\u0000-\\u001F]"), "_").trim()
        if (n.isBlank()) n = "package.pkg"
        if (!n.lowercase().endsWith(".pkg")) n += ".pkg"
        if (n.length > 160) n = n.take(156) + ".pkg"
        return n
    }

    private fun prune(maxAgeMs: Long = 24L * 60L * 60L * 1000L) {
        val cutoff = System.currentTimeMillis() - maxAgeMs
        entries.entries.removeIf { it.value.createdAt < cutoff }
    }
}
