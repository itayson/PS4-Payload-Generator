package com.tayson.ps4linksender.resolver

import com.tayson.ps4linksender.model.Provider
import com.tayson.ps4linksender.model.ResolutionResult
import com.tayson.ps4linksender.net.Http
import org.json.JSONObject
import java.net.URI

class VikingFileResolver : LinkResolver {
    private val regex = Regex("/f/([A-Za-z0-9_-]+)")

    override fun matches(url: String): Boolean = try {
        URI(url).host?.lowercase()?.let { it == "vikingfile.com" || it.endsWith(".vikingfile.com") } == true
    } catch (_: Exception) { false }

    override fun resolve(url: String): ResolutionResult {
        val path = runCatching { URI(url).path ?: "" }.getOrDefault("")
        if (path.lowercase().endsWith(".pkg")) {
            return ResolutionResult(url, Provider.VIKINGFILE, candidateUrl = url,
                message = "URL VikingFile com aparência de PKG direto. Validando sem cookies.")
        }
        val hash = regex.find(path)?.groupValues?.getOrNull(1)
        if (hash == null) return ResolutionResult(url, Provider.VIKINGFILE, needsOfficialFlow = true,
            message = "Link VikingFile reconhecido, mas o hash /f/... não foi encontrado.")
        return try {
            val json = JSONObject(Http.postForm("https://vikingfile.com/api/check-file", mapOf("hash" to hash)))
            val exists = json.optBoolean("exist", false)
            ResolutionResult(
                originalUrl = url,
                provider = Provider.VIKINGFILE,
                name = json.optString("name").takeIf { it.isNotBlank() },
                declaredSize = json.optLong("size").takeIf { it > 0 },
                needsOfficialFlow = true,
                message = if (exists)
                    "Arquivo confirmado pela API. A API pública informa metadados, mas não fornece hotlink gratuito; abra o fluxo oficial para obter uma URL autorizada."
                else "A API não confirmou a disponibilidade desse arquivo."
            )
        } catch (e: Exception) {
            ResolutionResult(url, Provider.VIKINGFILE, needsOfficialFlow = true,
                message = "Não foi possível consultar os metadados: ${e.message}. Você ainda pode abrir o fluxo oficial.")
        }
    }
}
