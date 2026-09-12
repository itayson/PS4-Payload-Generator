package com.tayson.ps4linksender.ps4

import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.HttpURLConnection
import java.net.InetSocketAddress
import java.net.Socket
import java.net.URL

/**
 * Client for the Remote Package Installer (RPI) HTTP API described in the
 * project reference PDF. The client probes the common RPI ports and keeps the
 * phone out of the package data path: only URLs and control commands are sent.
 */
object RpiClient {
    val PORTS = intArrayOf(12800, 11200)
    private const val CONNECT_TIMEOUT_MS = 1800
    private const val READ_TIMEOUT_MS = 5000

    data class ProbeResult(val host: String, val port: Int)

    data class InstallResult(
        val host: String,
        val port: Int,
        val httpCode: Int,
        val taskId: Int?,
        val body: String
    )

    data class ProgressResult(
        val taskId: Int,
        val status: String?,
        val downloadedBytes: Long,
        val totalBytes: Long,
        val raw: String
    )

    fun probe(host: String): ProbeResult {
        requireHost(host)
        var last: Exception? = null
        for (port in PORTS) {
            try {
                Socket().use { socket ->
                    socket.connect(InetSocketAddress(host, port), CONNECT_TIMEOUT_MS)
                    return ProbeResult(host, port)
                }
            } catch (e: Exception) {
                last = e
            }
        }
        throw IllegalStateException("Remote Package Installer não respondeu nas portas 12800/11200", last)
    }

    fun install(host: String, packageUrls: List<String>): InstallResult {
        requireHost(host)
        require(packageUrls.isNotEmpty()) { "Nenhum PKG para enviar" }
        packageUrls.forEach { require(it.startsWith("http://") || it.startsWith("https://")) { "URL de PKG inválida" } }
        val probe = probe(host)
        val payload = JSONObject().apply {
            put("type", "direct")
            put("packages", JSONArray().apply { packageUrls.forEach { put(it) } })
        }
        val response = post(probe.host, probe.port, "/api/install", payload)
        if (response.code !in 200..299) {
            throw IllegalStateException("RPI recusou /api/install: HTTP ${response.code}${response.body.takeIf { it.isNotBlank() }?.let { " • ${it.take(240)}" } ?: ""}")
        }
        return InstallResult(
            host = probe.host,
            port = probe.port,
            httpCode = response.code,
            taskId = extractTaskId(response.body),
            body = response.body
        )
    }

    fun progress(host: String, port: Int, taskId: Int): ProgressResult {
        val response = post(host, port, "/api/get_task_progress", JSONObject().put("task_id", taskId))
        if (response.code !in 200..299) throw IllegalStateException("RPI progresso HTTP ${response.code}")
        val json = runCatching { JSONObject(response.body) }.getOrNull()
        return ProgressResult(
            taskId = json?.optInt("task_id", taskId) ?: taskId,
            status = json?.optString("status")?.takeIf { it.isNotBlank() },
            downloadedBytes = json?.optLong("downloaded_bytes", 0L) ?: 0L,
            totalBytes = json?.optLong("total_bytes", 0L) ?: 0L,
            raw = response.body
        )
    }

    fun pause(host: String, port: Int, taskId: Int) = control(host, port, "/api/pause_task", taskId)
    fun resume(host: String, port: Int, taskId: Int) = control(host, port, "/api/resume_task", taskId)
    fun cancel(host: String, port: Int, taskId: Int) = control(host, port, "/api/unregister_task", taskId)

    private fun control(host: String, port: Int, route: String, taskId: Int) {
        val response = post(host, port, route, JSONObject().put("task_id", taskId))
        if (response.code !in 200..299) throw IllegalStateException("RPI ${route.substringAfterLast('/')} HTTP ${response.code}")
    }

    private data class HttpResponse(val code: Int, val body: String)

    private fun post(host: String, port: Int, path: String, json: JSONObject): HttpResponse {
        requireHost(host)
        require(port in 1..65535) { "Porta inválida" }
        val conn = (URL("http://$host:$port$path").openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = CONNECT_TIMEOUT_MS
            readTimeout = READ_TIMEOUT_MS
            doOutput = true
            useCaches = false
            setRequestProperty("Content-Type", "application/json; charset=utf-8")
            setRequestProperty("Accept", "application/json, text/plain, */*")
        }
        try {
            conn.outputStream.use { it.write(json.toString().toByteArray(Charsets.UTF_8)) }
            val code = conn.responseCode
            val stream = if (code in 200..399) conn.inputStream else conn.errorStream
            val body = if (stream != null) BufferedReader(InputStreamReader(stream, Charsets.UTF_8)).use { it.readText() } else ""
            return HttpResponse(code, body)
        } finally {
            conn.disconnect()
        }
    }

    private fun extractTaskId(body: String): Int? {
        val json = runCatching { JSONObject(body) }.getOrNull()
        if (json != null) {
            for (key in arrayOf("task_id", "taskId", "id")) {
                if (json.has(key)) {
                    val value = json.optInt(key, Int.MIN_VALUE)
                    if (value != Int.MIN_VALUE) return value
                }
            }
            val task = json.optJSONObject("task")
            if (task != null) {
                for (key in arrayOf("task_id", "taskId", "id")) {
                    if (task.has(key)) {
                        val value = task.optInt(key, Int.MIN_VALUE)
                        if (value != Int.MIN_VALUE) return value
                    }
                }
            }
        }
        return Regex("(?i)(?:task[_ -]?id|task)\\D{0,12}(\\d+)").find(body)?.groupValues?.getOrNull(1)?.toIntOrNull()
    }

    private fun requireHost(host: String) {
        require(host.isNotBlank()) { "Informe o IP do PS4" }
        require(Regex("^(?:\\d{1,3}\\.){3}\\d{1,3}$").matches(host)) { "Use somente o IPv4 do PS4" }
        require(host.split('.').all { (it.toIntOrNull() ?: 999) in 0..255 }) { "IPv4 inválido" }
    }
}
