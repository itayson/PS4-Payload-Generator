package com.tayson.ps4linksender.web

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.graphics.Color
import android.net.Uri
import android.os.Bundle
import android.view.ViewGroup
import android.webkit.CookieManager
import android.webkit.SafeBrowsingResponse
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.LinearLayout
import android.widget.TextView
import org.json.JSONObject
import java.util.LinkedHashMap

class LinkCaptureActivity : Activity() {
    companion object {
        const val EXTRA_URL = "url"
        const val EXTRA_CAPTURED_URL = "captured_url"
        const val EXTRA_USER_AGENT = "captured_user_agent"
        const val EXTRA_REFERER = "captured_referer"
        const val EXTRA_COOKIE = "captured_cookie"
        const val EXTRA_HEADERS_JSON = "captured_headers_json"
    }

    private lateinit var webView: WebView
    private lateinit var status: TextView
    private val observedHeaders = object : LinkedHashMap<String, Map<String, String>>(80, 0.75f, true) {
        override fun removeEldestEntry(eldest: MutableMap.MutableEntry<String, Map<String, String>>?): Boolean = size > 64
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val url = intent.getStringExtra(EXTRA_URL)?.trim() ?: run { finish(); return }
        if (!isHttp(url)) { finish(); return }

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12), dp(12), dp(12), dp(12))
            setBackgroundColor(Color.rgb(16, 19, 26))
        }
        status = TextView(this).apply {
            text = "Conclua somente o fluxo oficial. Quando o site iniciar o arquivo real, o app captura a URL e, se necessário, o contexto HTTP autorizado (User-Agent/Referer/Cookie) para o Native Bridge do PS4."
            textSize = 14f
            setTextColor(Color.WHITE)
            setPadding(0, 0, 0, dp(8))
        }
        webView = WebView(this)
        root.addView(status, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        root.addView(webView, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        setContentView(root)

        CookieManager.getInstance().setAcceptCookie(true)
        CookieManager.getInstance().setAcceptThirdPartyCookies(webView, false)
        webView.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            javaScriptCanOpenWindowsAutomatically = false
            setSupportMultipleWindows(false)
            allowFileAccess = false
            allowContentAccess = false
            mixedContentMode = android.webkit.WebSettings.MIXED_CONTENT_NEVER_ALLOW
        }
        if (android.os.Build.VERSION.SDK_INT >= 26) WebView.startSafeBrowsing(this) { }

        webView.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                val target = request.url?.toString() ?: return true
                if (isHttp(target)) return false
                status.text = "Navegação não HTTP/HTTPS bloqueada."
                return true
            }

            override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? {
                val u = request.url?.toString().orEmpty()
                if (isHttp(u)) synchronized(observedHeaders) { observedHeaders[u] = sanitizeHeaders(request.requestHeaders) }
                return super.shouldInterceptRequest(view, request)
            }

            override fun onSafeBrowsingHit(view: WebView, request: WebResourceRequest, threatType: Int, callback: SafeBrowsingResponse) {
                callback.backToSafety(true)
                AlertDialog.Builder(this@LinkCaptureActivity)
                    .setTitle("Navegação bloqueada")
                    .setMessage("O Android Safe Browsing marcou esse destino como arriscado. O app não prosseguiu.")
                    .setPositiveButton("OK", null)
                    .show()
            }
        }
        webView.setDownloadListener { downloadUrl, _, contentDisposition, mimeType, _ ->
            if (downloadUrl.isNullOrBlank()) return@setDownloadListener
            if (!isHttp(downloadUrl)) {
                status.text = "O site iniciou um download interno (por exemplo blob:), que o PS4 não consegue acessar diretamente."
                return@setDownloadListener
            }
            status.text = "Download capturado. Retornando para validar o PKG…"
            val cookies = CookieManager.getInstance().getCookie(downloadUrl).orEmpty()
            val referer = webView.url.orEmpty()
            val userAgent = webView.settings.userAgentString.orEmpty()
            val headers = synchronized(observedHeaders) {
                observedHeaders[downloadUrl] ?: observedHeaders.entries.lastOrNull { runCatching { Uri.parse(it.key).host == Uri.parse(downloadUrl).host }.getOrDefault(false) }?.value ?: emptyMap()
            }
            val headersJson = JSONObject().apply { headers.forEach { (k, v) -> put(k, v) } }.toString()
            setResult(RESULT_OK, Intent().apply {
                putExtra(EXTRA_CAPTURED_URL, downloadUrl)
                putExtra(EXTRA_USER_AGENT, userAgent)
                putExtra(EXTRA_REFERER, referer)
                putExtra(EXTRA_COOKIE, cookies)
                putExtra(EXTRA_HEADERS_JSON, headersJson)
                putExtra("content_disposition", contentDisposition)
                putExtra("mime_type", mimeType)
            })
            finish()
        }
        webView.loadUrl(url)
    }

    @Deprecated("Deprecated in Android API; kept for minSdk compatibility")
    override fun onBackPressed() {
        if (::webView.isInitialized && webView.canGoBack()) webView.goBack() else super.onBackPressed()
    }

    override fun onDestroy() {
        if (::webView.isInitialized) {
            webView.stopLoading()
            webView.loadUrl("about:blank")
            webView.clearHistory()
            webView.removeAllViews()
            webView.destroy()
        }
        super.onDestroy()
    }


    private fun sanitizeHeaders(headers: Map<String, String>): Map<String, String> {
        val allowedExact = setOf("accept", "accept-language", "origin", "dnt", "cache-control", "pragma")
        return headers.entries.mapNotNull { (name, value) ->
            val lower = name.lowercase()
            val allowed = lower in allowedExact || lower.startsWith("sec-fetch-") || lower.startsWith("sec-ch-ua")
            if (!allowed || value.isBlank() || name.contains('\n') || name.contains('\r') || value.contains('\n') || value.contains('\r')) null
            else name to value
        }.toMap()
    }

    private fun isHttp(value: String): Boolean = try {
        val u = Uri.parse(value)
        (u.scheme.equals("http", true) || u.scheme.equals("https", true)) && !u.host.isNullOrBlank()
    } catch (_: Exception) { false }

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()
}
