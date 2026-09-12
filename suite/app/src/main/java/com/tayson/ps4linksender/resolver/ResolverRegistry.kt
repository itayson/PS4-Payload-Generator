package com.tayson.ps4linksender.resolver

import com.tayson.ps4linksender.model.Provider
import com.tayson.ps4linksender.model.ResolutionResult
import com.tayson.ps4linksender.net.Http

class ResolverRegistry {
    private val direct = DirectResolver()
    private val providerResolvers: List<LinkResolver> = listOf(
        RootzResolver(), VikingFileResolver(), AkiraBoxResolver(), MochaResolver()
    )

    fun resolve(url: String): ResolutionResult {
        val clean = try { Http.requireHttpUrl(url) } catch (e: Exception) {
            return ResolutionResult(url, Provider.UNKNOWN, message = e.message ?: "URL inválida")
        }
        // Preserve provider identity even when a provider URL ends in .pkg.
        providerResolvers.firstOrNull { it.matches(clean) }?.let { return it.resolve(clean) }
        if (direct.matches(clean)) return direct.resolve(clean)
        return ResolutionResult(
            originalUrl = clean,
            provider = Provider.UNKNOWN,
            candidateUrl = clean,
            message = "Host sem resolver específico. Tentando validar como download direto."
        )
    }
}
