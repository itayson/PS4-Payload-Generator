package com.tayson.ps4linksender.resolver

import com.tayson.ps4linksender.model.Provider
import com.tayson.ps4linksender.model.ResolutionResult
import java.net.URI

class MochaResolver : LinkResolver {
    override fun matches(url: String): Boolean {
        return try {
            val h = URI(url).host?.lowercase() ?: return false
        h == "mocha.my" || h.endsWith(".mocha.my")
        } catch (_: Exception) { false }
    }

    override fun resolve(url: String): ResolutionResult {
        val path = try { URI(url).path ?: "" } catch (_: Exception) { "" }
        if (!path.startsWith("/share/", ignoreCase = true) && path.lowercase().endsWith(".pkg")) {
            return ResolutionResult(url, Provider.MOCHA, candidateUrl = url,
                message = "URL Mocha com aparência de arquivo direto. Validando sem cookies.")
        }
        return try {
            val meta = ResolverSupport.inspectPublicPage(url)
            ResolutionResult(
                originalUrl = url,
                provider = Provider.MOCHA,
                name = meta.title?.removeSuffix(" - File Download")?.trim(),
                declaredSize = meta.size,
                needsOfficialFlow = true,
                message = "Share Mocha reconhecido. A página pública foi inspecionada; o app ainda precisa de uma URL temporária/hotlink que funcione sem a sessão do WebView."
            )
        } catch (e: Exception) {
            ResolutionResult(url, Provider.MOCHA, needsOfficialFlow = true,
                message = "Mocha reconhecido. A resolução pública falhou (${e.message}); abra o fluxo oficial para obter uma URL de download autorizada.")
        }
    }
}
