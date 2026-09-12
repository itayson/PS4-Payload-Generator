package com.tayson.ps4linksender.model

enum class Provider(val label: String) {
    DIRECT("Link direto"),
    ROOTZ("Rootz"),
    VIKINGFILE("VikingFile"),
    AKIRABOX("AkiraBox"),
    MOCHA("Mocha"),
    UNKNOWN("Desconhecido")
}

data class ResolutionResult(
    val originalUrl: String,
    val provider: Provider,
    val candidateUrl: String? = null,
    val name: String? = null,
    val declaredSize: Long? = null,
    val needsOfficialFlow: Boolean = false,
    val message: String = ""
)


data class RequestContext(
    val userAgent: String? = null,
    val referer: String? = null,
    val cookie: String? = null,
    val extraHeaders: Map<String, String> = emptyMap()
)

data class SfoInfo(
    val title: String? = null,
    val titleId: String? = null,
    val appVersion: String? = null,
    val version: String? = null,
    val category: String? = null,
    val contentId: String? = null,
    val systemVersion: Long? = null
)

data class PkgInfo(
    val sourceUrl: String,
    val finalUrl: String,
    val fileName: String,
    val size: Long,
    val contentId: String,
    val titleId: String?,
    val packageType: String,
    val contentTypeValue: Int,
    val contentFlags: Long,
    val rangeSupported: Boolean,
    val httpCode: Int,
    val mimeType: String?,
    val sfo: SfoInfo? = null,
    val requestContext: RequestContext? = null
)

data class QueueItem(
    val id: Long,
    val addedAt: Long,
    val pkg: PkgInfo,
    val originalUrl: String = pkg.sourceUrl,
    val provider: Provider = Provider.UNKNOWN
)

data class HistoryEntry(
    val originalUrl: String,
    val provider: Provider,
    val fileName: String?,
    val titleId: String?,
    val title: String?,
    val lastUsedAt: Long
)


data class ReceiverJobMeta(
    val receiverJobId: Int,
    val bgftTaskId: Int?,
    val contentId: String,
    val title: String?,
    val fileName: String,
    val size: Long,
    val sentAt: Long
)
