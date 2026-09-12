package com.tayson.ps4linksender.resolver

import com.tayson.ps4linksender.model.Provider
import com.tayson.ps4linksender.model.ResolutionResult
import com.tayson.ps4linksender.net.Http
import org.json.JSONObject
import java.net.URI
import java.net.URLEncoder

class AkiraBoxResolver : LinkResolver {
    override fun matches(url: String): Boolean {
        return try {
            val h = URI(url).host?.lowercase() ?: return false
            h == "akirabox.com" || h.endsWith(".akirabox.com")
        } catch (_: Exception) { false }
    }

    override fun resolve(url: String): ResolutionResult {
        val path = runCatching { URI(url).path ?: "" }.getOrDefault("")
        if (path.lowercase().endsWith(".pkg")) {
            return ResolutionResult(url, Provider.AKIRABOX, candidateUrl = url,
                message = "URL AkiraBox com aparência de PKG direto. Validando sem cookies.")
        }
        return try {
            val endpoint = "https://akirabox.com/api/files?url=" + URLEncoder.encode(url, "UTF-8")
            val json = JSONObject(Http.getText(endpoint, 64 * 1024))
            val status = json.optInt("status", 0)
            val size = when (val raw = json.opt("size")) {
                is Number -> raw.toLong().takeIf { it > 0 }
                is String -> raw.toLongOrNull()?.takeIf { it > 0 } ?: ResolverSupport.parseHumanSize(raw)
                else -> null
            }
            ResolutionResult(
                originalUrl = url,
                provider = Provider.AKIRABOX,
                name = json.optString("name").takeIf { it.isNotBlank() },
                declaredSize = size,
                needsOfficialFlow = true,
                message = if (status == 200)
                    "Arquivo confirmado pela API. Use o fluxo oficial para obter uma URL de download que o PS4 consiga acessar sem cookies."
                else json.optString("message", "Arquivo não confirmado pela API.")
            )
        } catch (e: Exception) {
            ResolutionResult(url, Provider.AKIRABOX, needsOfficialFlow = true,
                message = "Falha ao consultar a API: ${e.message}. Você ainda pode abrir o fluxo oficial.")
        }
    }
}
