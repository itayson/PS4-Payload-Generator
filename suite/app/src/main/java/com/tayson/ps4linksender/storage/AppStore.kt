package com.tayson.ps4linksender.storage

import android.content.Context
import com.tayson.ps4linksender.model.*
import org.json.JSONArray
import org.json.JSONObject

class AppStore(context: Context) {
    private val prefs = context.getSharedPreferences("app_store_v3", Context.MODE_PRIVATE)
    private val legacyPrefs = context.getSharedPreferences("app_store_v2", Context.MODE_PRIVATE)

    fun loadQueue(): MutableList<QueueItem> {
        val current = parseArray(prefs.getString("queue", null)) { queueFromJson(it) }
        if (current.isNotEmpty()) return current.toMutableList()
        val legacy = parseArray(legacyPrefs.getString("queue", null)) { queueFromJson(it) }
        if (legacy.isNotEmpty()) saveQueue(legacy)
        return legacy.toMutableList()
    }

    fun saveQueue(items: List<QueueItem>) {
        val arr = JSONArray()
        items.forEach { arr.put(queueToJson(it)) }
        prefs.edit().putString("queue", arr.toString()).apply()
    }

    fun loadHistory(): MutableList<HistoryEntry> {
        val current = parseArray(prefs.getString("history", null)) { historyFromJson(it) }
        if (current.isNotEmpty()) return current.toMutableList()
        val legacy = parseArray(legacyPrefs.getString("history", null)) { historyFromJson(it) }
        if (legacy.isNotEmpty()) saveHistory(legacy)
        return legacy.toMutableList()
    }

    fun addHistory(entry: HistoryEntry) {
        val items = loadHistory()
        items.removeAll { it.originalUrl == entry.originalUrl }
        items.add(0, entry)
        while (items.size > 50) items.removeAt(items.lastIndex)
        saveHistory(items)
    }

    private fun saveHistory(items: List<HistoryEntry>) {
        val arr = JSONArray()
        items.forEach { arr.put(historyToJson(it)) }
        prefs.edit().putString("history", arr.toString()).apply()
    }

    fun clearHistory() = prefs.edit().remove("history").apply()

    fun loadReceiverJobs(): MutableList<ReceiverJobMeta> =
        parseArray(prefs.getString("receiver_jobs", null)) { receiverJobFromJson(it) }.toMutableList()

    fun addReceiverJob(meta: ReceiverJobMeta) {
        val items = loadReceiverJobs()
        items.removeAll { it.receiverJobId == meta.receiverJobId || it.contentId == meta.contentId }
        items.add(0, meta)
        while (items.size > 32) items.removeAt(items.lastIndex)
        val arr = JSONArray()
        items.forEach { arr.put(receiverJobToJson(it)) }
        prefs.edit().putString("receiver_jobs", arr.toString()).apply()
    }

    private fun receiverJobToJson(j: ReceiverJobMeta) = JSONObject().apply {
        put("receiverJobId", j.receiverJobId)
        j.bgftTaskId?.let { put("bgftTaskId", it) }
        put("contentId", j.contentId)
        putOpt("title", j.title)
        put("fileName", j.fileName)
        put("size", j.size)
        put("sentAt", j.sentAt)
    }

    private fun receiverJobFromJson(o: JSONObject): ReceiverJobMeta? = try {
        ReceiverJobMeta(
            receiverJobId = o.getInt("receiverJobId"),
            bgftTaskId = if (o.has("bgftTaskId")) o.optInt("bgftTaskId") else null,
            contentId = o.getString("contentId"),
            title = o.optString("title").takeIf { it.isNotBlank() },
            fileName = o.getString("fileName"),
            size = o.getLong("size"),
            sentAt = o.optLong("sentAt", 0L)
        )
    } catch (_: Exception) { null }

    private fun <T> parseArray(raw: String?, mapper: (JSONObject) -> T?): List<T> {
        if (raw.isNullOrBlank()) return emptyList()
        return try {
            val arr = JSONArray(raw)
            buildList {
                for (i in 0 until arr.length()) mapper(arr.getJSONObject(i))?.let { add(it) }
            }
        } catch (_: Exception) { emptyList() }
    }

    private fun queueToJson(item: QueueItem) = JSONObject().apply {
        put("id", item.id)
        put("addedAt", item.addedAt)
        put("originalUrl", item.originalUrl)
        put("provider", item.provider.name)
        put("pkg", pkgToJson(item.pkg))
    }

    private fun queueFromJson(o: JSONObject): QueueItem? = try {
        val pkg = pkgFromJson(o.getJSONObject("pkg"))
        QueueItem(
            id = o.getLong("id"),
            addedAt = o.getLong("addedAt"),
            pkg = pkg,
            originalUrl = o.optString("originalUrl").takeIf { it.isNotBlank() } ?: pkg.sourceUrl,
            provider = runCatching { Provider.valueOf(o.optString("provider", Provider.UNKNOWN.name)) }.getOrDefault(Provider.UNKNOWN)
        )
    } catch (_: Exception) { null }

    private fun historyToJson(h: HistoryEntry) = JSONObject().apply {
        put("originalUrl", h.originalUrl)
        put("provider", h.provider.name)
        putOpt("fileName", h.fileName)
        putOpt("titleId", h.titleId)
        putOpt("title", h.title)
        put("lastUsedAt", h.lastUsedAt)
    }

    private fun historyFromJson(o: JSONObject): HistoryEntry? = try {
        HistoryEntry(
            originalUrl = o.getString("originalUrl"),
            provider = runCatching { Provider.valueOf(o.optString("provider", Provider.UNKNOWN.name)) }.getOrDefault(Provider.UNKNOWN),
            fileName = o.optString("fileName").takeIf { it.isNotBlank() },
            titleId = o.optString("titleId").takeIf { it.isNotBlank() },
            title = o.optString("title").takeIf { it.isNotBlank() },
            lastUsedAt = o.optLong("lastUsedAt", 0L)
        )
    } catch (_: Exception) { null }

    private fun pkgToJson(p: PkgInfo) = JSONObject().apply {
        put("sourceUrl", p.sourceUrl)
        put("finalUrl", p.finalUrl)
        put("fileName", p.fileName)
        put("size", p.size)
        put("contentId", p.contentId)
        putOpt("titleId", p.titleId)
        put("packageType", p.packageType)
        put("contentTypeValue", p.contentTypeValue)
        put("contentFlags", p.contentFlags)
        put("rangeSupported", p.rangeSupported)
        put("httpCode", p.httpCode)
        putOpt("mimeType", p.mimeType)
        p.sfo?.let { put("sfo", sfoToJson(it)) }
        p.requestContext?.let { put("requestContext", requestContextToJson(it)) }
    }

    private fun pkgFromJson(o: JSONObject): PkgInfo = PkgInfo(
        sourceUrl = o.getString("sourceUrl"),
        finalUrl = o.getString("finalUrl"),
        fileName = o.getString("fileName"),
        size = o.getLong("size"),
        contentId = o.getString("contentId"),
        titleId = o.optString("titleId").takeIf { it.isNotBlank() },
        packageType = o.getString("packageType"),
        contentTypeValue = o.getInt("contentTypeValue"),
        contentFlags = o.getLong("contentFlags"),
        rangeSupported = o.optBoolean("rangeSupported", false),
        httpCode = o.optInt("httpCode", 0),
        mimeType = o.optString("mimeType").takeIf { it.isNotBlank() },
        sfo = o.optJSONObject("sfo")?.let { sfoFromJson(it) },
        requestContext = o.optJSONObject("requestContext")?.let { requestContextFromJson(it) }
    )

    private fun requestContextToJson(r: RequestContext) = JSONObject().apply {
        putOpt("userAgent", r.userAgent)
        putOpt("referer", r.referer)
        putOpt("cookie", r.cookie)
        if (r.extraHeaders.isNotEmpty()) {
            put("extraHeaders", JSONObject().apply { r.extraHeaders.forEach { (k, v) -> put(k, v) } })
        }
    }

    private fun requestContextFromJson(o: JSONObject) = RequestContext(
        userAgent = o.optString("userAgent").takeIf { it.isNotBlank() },
        referer = o.optString("referer").takeIf { it.isNotBlank() },
        cookie = o.optString("cookie").takeIf { it.isNotBlank() },
        extraHeaders = o.optJSONObject("extraHeaders")?.let { h ->
            buildMap {
                val keys = h.keys()
                while (keys.hasNext()) {
                    val k = keys.next()
                    val v = h.optString(k)
                    if (k.isNotBlank() && v.isNotBlank()) put(k, v)
                }
            }
        } ?: emptyMap()
    )

    private fun sfoToJson(s: SfoInfo) = JSONObject().apply {
        putOpt("title", s.title)
        putOpt("titleId", s.titleId)
        putOpt("appVersion", s.appVersion)
        putOpt("version", s.version)
        putOpt("category", s.category)
        putOpt("contentId", s.contentId)
        s.systemVersion?.let { put("systemVersion", it) }
    }

    private fun sfoFromJson(o: JSONObject) = SfoInfo(
        title = o.optString("title").takeIf { it.isNotBlank() },
        titleId = o.optString("titleId").takeIf { it.isNotBlank() },
        appVersion = o.optString("appVersion").takeIf { it.isNotBlank() },
        version = o.optString("version").takeIf { it.isNotBlank() },
        category = o.optString("category").takeIf { it.isNotBlank() },
        contentId = o.optString("contentId").takeIf { it.isNotBlank() },
        systemVersion = if (o.has("systemVersion")) o.optLong("systemVersion") else null
    )
}
