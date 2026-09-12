package com.tayson.ps4linksender.ps4

import android.content.Context
import android.net.Uri
import com.tayson.ps4linksender.model.PkgInfo
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.charset.StandardCharsets

/**
 * Generates a one-shot BGFT payload from the embedded template and sends it
 * directly to GoldHEN BinLoader. After the payload registers the task, Android
 * is no longer in the data path: BGFT downloads the PKG directly on the PS4.
 */
object BgftPayloadClient {
    const val PORT = 9090
    private const val ASSET_NAME = "tayson_bgft_template.bin"
    private const val CONNECT_TIMEOUT_MS = 4000
    private const val IO_TIMEOUT_MS = 8000
    private const val MAX_PAYLOAD_BYTES = 2 * 1024 * 1024

    private data class Slot(val marker: String, val capacity: Int)

    private val contentIdSlot = Slot("TAYSON_CONTENT_ID_SLOT_V1", 64)
    private val urlSlot = Slot("TAYSON_URL_SLOT_V1", 4096)
    private val nameSlot = Slot("TAYSON_NAME_SLOT_V1", 256)
    private val typeSlot = Slot("TAYSON_TYPE_V1", 16)
    private val sizeSlot = Slot("TAYSON_SIZE_SLOT_V1", 32)
    private val categorySlot = Slot("TAYSON_CAT_V1", 16)

    data class SendResult(
        val host: String,
        val bytesSent: Int,
        val packageName: String,
        val contentId: String,
        val bgftUrl: String,
        val compatibilityAliasApplied: Boolean
    )

    fun send(context: Context, host: String, pkg: PkgInfo): SendResult {
        requireHost(host)
        val normalized = makeBgftCompatibleUrl(pkg.finalUrl)
        val payload = generate(context, pkg, normalized.first)
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, PORT), CONNECT_TIMEOUT_MS)
            socket.soTimeout = IO_TIMEOUT_MS
            socket.tcpNoDelay = true
            socket.getOutputStream().use { out ->
                out.write(payload)
                out.flush()
            }
        }
        return SendResult(
            host = host,
            bytesSent = payload.size,
            packageName = pkg.fileName,
            contentId = pkg.contentId,
            bgftUrl = normalized.first,
            compatibilityAliasApplied = normalized.second
        )
    }

    fun generate(context: Context, pkg: PkgInfo): ByteArray =
        generate(context, pkg, makeBgftCompatibleUrl(pkg.finalUrl).first)

    private fun generate(context: Context, pkg: PkgInfo, bgftUrl: String): ByteArray {
        val bytes = context.assets.open(ASSET_NAME).use { it.readBytes() }
        require(bytes.isNotEmpty()) { "Template BGFT embutido está vazio" }
        require(bytes.size <= MAX_PAYLOAD_BYTES) { "Template BGFT excede o limite esperado" }

        patchAscii(bytes, contentIdSlot, pkg.contentId)
        patchAscii(bytes, urlSlot, bgftUrl)
        patchAscii(bytes, nameSlot, pkg.fileName)
        patchAscii(bytes, typeSlot, pkg.packageType)
        patchAscii(bytes, sizeSlot, pkg.size.toString())
        patchAscii(bytes, categorySlot, pkg.sfo?.category.orEmpty())
        return bytes
    }

    /**
     * PS4 BGFT rejects remote package URLs whose URL extension is not .pkg
     * (0x80990033 / CE-32942-0). Several supported providers expose signed CDN
     * URLs with opaque paths while the real filename only exists in response
     * headers/query metadata. A URI fragment is not transmitted in the HTTP
     * request, so adding a .pkg fragment gives BGFT a package-looking URL while
     * preserving the provider's signed path and query string byte-for-byte.
     */
    private fun makeBgftCompatibleUrl(rawUrl: String): Pair<String, Boolean> {
        val clean = rawUrl.substringBefore('#')
        val path = runCatching { Uri.parse(clean).path.orEmpty() }.getOrDefault("")
        if (path.lowercase().endsWith(".pkg")) return clean to false

        val candidate = "$clean#tayson.pkg"
        require(candidate.toByteArray(StandardCharsets.UTF_8).size < urlSlot.capacity) {
            "URL final é grande demais para o payload BGFT"
        }
        return candidate to true
    }

    private fun patchAscii(bytes: ByteArray, slot: Slot, value: String) {
        val marker = slot.marker.toByteArray(StandardCharsets.US_ASCII)
        val offset = findUnique(bytes, marker)
        val encoded = value.toByteArray(StandardCharsets.UTF_8)
        require(encoded.size < slot.capacity) {
            "Campo ${slot.marker} é grande demais (${encoded.size}/${slot.capacity - 1})"
        }
        require(offset + slot.capacity <= bytes.size) { "Slot ${slot.marker} truncado no template" }
        java.util.Arrays.fill(bytes, offset, offset + slot.capacity, 0.toByte())
        System.arraycopy(encoded, 0, bytes, offset, encoded.size)
    }

    private fun findUnique(haystack: ByteArray, needle: ByteArray): Int {
        var found = -1
        var count = 0
        var i = 0
        while (i <= haystack.size - needle.size) {
            var ok = true
            var j = 0
            while (j < needle.size) {
                if (haystack[i + j] != needle[j]) { ok = false; break }
                j++
            }
            if (ok) {
                found = i
                count++
                if (count > 1) break
            }
            i++
        }
        require(count == 1 && found >= 0) { "Template incompatível: ${String(needle)} encontrado $count vez(es)" }
        return found
    }

    private fun requireHost(host: String) {
        require(host.isNotBlank()) { "Informe o IP do PS4" }
        require(Regex("^(?:\\d{1,3}\\.){3}\\d{1,3}$").matches(host)) { "Use somente o IPv4 do PS4" }
        require(host.split('.').all { (it.toIntOrNull() ?: 999) in 0..255 }) { "IPv4 inválido" }
    }
}
