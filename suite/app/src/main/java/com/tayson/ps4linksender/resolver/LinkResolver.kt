package com.tayson.ps4linksender.resolver

import com.tayson.ps4linksender.model.ResolutionResult

interface LinkResolver {
    fun matches(url: String): Boolean
    fun resolve(url: String): ResolutionResult
}
