package com.tayson.ps4linksender.resolver

import com.tayson.ps4linksender.model.Provider
import com.tayson.ps4linksender.model.ResolutionResult
import java.net.URI

class DirectResolver : LinkResolver {
    override fun matches(url: String): Boolean {
        return try {
            val u = URI(url)
            val path = (u.path ?: "").lowercase()
            path.endsWith(".pkg") || path.endsWith(".elf") || path.endsWith(".bin")
        } catch (_: Exception) { false }
    }

    override fun resolve(url: String) = ResolutionResult(
        originalUrl = url,
        provider = Provider.DIRECT,
        candidateUrl = url,
        message = "URL tratada como download direto."
    )
}
