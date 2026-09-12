package com.tayson.ps4linksender.resolver

import com.tayson.ps4linksender.model.Provider
import com.tayson.ps4linksender.model.ResolutionResult
import java.net.URI

class RootzResolver : LinkResolver {
    override fun matches(url: String): Boolean {
        return try {
            val h = URI(url).host?.lowercase() ?: return false
        h == "rootz.so" || h.endsWith(".rootz.so")
        } catch (_: Exception) { false }
    }

    override fun resolve(url: String): ResolutionResult {
        val path = try { URI(url).path ?: "" } catch (_: Exception) { "" }
        if (path.startsWith("/download/", ignoreCase = true) || path.lowercase().endsWith(".pkg")) {
            return ResolutionResult(
                originalUrl = url,
                provider = Provider.ROOTZ,
                candidateUrl = url,
                message = "URL Rootz com aparência de download direto. Validando sem cookies."
            )
        }
        return try {
            val meta = ResolverSupport.inspectPublicPage(url)
            ResolutionResult(
                originalUrl = url,
                provider = Provider.ROOTZ,
                name = meta.title,
                declaredSize = meta.size,
                needsOfficialFlow = true,
                message = "Página Rootz reconhecida e metadados públicos lidos. O link compartilhado não é tratado como hotlink; conclua o fluxo oficial para capturar a URL liberada pelo serviço."
            )
        } catch (e: Exception) {
            ResolutionResult(
                originalUrl = url,
                provider = Provider.ROOTZ,
                needsOfficialFlow = true,
                message = "Rootz reconhecido. Não foi possível ler a página pública (${e.message}); use o fluxo oficial para capturar a URL autorizada."
            )
        }
    }
}
