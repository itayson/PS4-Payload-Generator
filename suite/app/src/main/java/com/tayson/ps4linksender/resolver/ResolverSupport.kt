package com.tayson.ps4linksender.resolver

import com.tayson.ps4linksender.net.Http
import java.util.Locale

object ResolverSupport {
    data class PageMeta(val title: String?, val size: Long?)

    fun inspectPublicPage(url: String): PageMeta {
        val html = Http.getText(url, 256 * 1024)
        val title = sequenceOf(
            Regex("<meta[^>]+property=[\"']og:title[\"'][^>]+content=[\"']([^\"']+)", RegexOption.IGNORE_CASE),
            Regex("<meta[^>]+content=[\"']([^\"']+)[\"'][^>]+property=[\"']og:title[\"']", RegexOption.IGNORE_CASE),
            Regex("<title[^>]*>(.*?)</title>", setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL))
        ).mapNotNull { it.find(html)?.groupValues?.getOrNull(1) }
            .map { decodeHtml(it).replace(Regex("\\s+"), " ").trim() }
            .firstOrNull { it.isNotBlank() }
            ?.removeSuffix(" | Rootz")
            ?.removeSuffix(" | Mocha")

        val sizeText = Regex("(?i)(\\d+(?:[.,]\\d+)?)\\s*(B|KB|MB|GB|TB)")
            .findAll(stripTags(html))
            .map { it.value }
            .firstOrNull()
        return PageMeta(title?.takeIf { it.length <= 240 }, sizeText?.let { parseHumanSize(it) })
    }

    fun parseHumanSize(text: String): Long? {
        val m = Regex("(?i)(\\d+(?:[.,]\\d+)?)\\s*(B|KB|MB|GB|TB)").find(text) ?: return null
        val n = m.groupValues[1].replace(',', '.').toDoubleOrNull() ?: return null
        val factor = when (m.groupValues[2].uppercase(Locale.US)) {
            "KB" -> 1024.0
            "MB" -> 1024.0 * 1024.0
            "GB" -> 1024.0 * 1024.0 * 1024.0
            "TB" -> 1024.0 * 1024.0 * 1024.0 * 1024.0
            else -> 1.0
        }
        return (n * factor).toLong().takeIf { it > 0 }
    }

    private fun stripTags(s: String) = s.replace(Regex("<script.*?</script>", setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL)), " ")
        .replace(Regex("<style.*?</style>", setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL)), " ")
        .replace(Regex("<[^>]+>"), " ")
        .replace(Regex("\\s+"), " ")

    private fun decodeHtml(s: String): String = s
        .replace("&amp;", "&", true)
        .replace("&quot;", "\"", true)
        .replace("&#39;", "'", true)
        .replace("&lt;", "<", true)
        .replace("&gt;", ">", true)
        .replace("&nbsp;", " ", true)
}
