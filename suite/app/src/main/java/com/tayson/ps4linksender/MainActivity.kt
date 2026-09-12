package com.tayson.ps4linksender

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.graphics.Color
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.*
import com.tayson.ps4linksender.model.*
import com.tayson.ps4linksender.pkg.PkgScanner
import com.tayson.ps4linksender.ps4.PayloadLoader
import com.tayson.ps4linksender.ps4.PayloadProbeClient
import com.tayson.ps4linksender.ps4.ReceiverClient
import com.tayson.ps4linksender.ps4.ReceiverDiscovery
import com.tayson.ps4linksender.resolver.ResolverRegistry
import com.tayson.ps4linksender.storage.AppStore
import com.tayson.ps4linksender.util.Format
import com.tayson.ps4linksender.web.LinkCaptureActivity
import java.text.DateFormat
import java.util.Date
import java.util.concurrent.Executors
import org.json.JSONObject

class MainActivity : Activity() {
    companion object {
        private const val REQ_CAPTURE = 1001
        private const val MONITOR_INTERVAL_MS = 2000L
    }

    private val executor = Executors.newSingleThreadExecutor()
    private val monitorHandler = Handler(Looper.getMainLooper())
    private var monitoring = false
    private var monitorInFlight = false
    private var activityResumed = false
    private data class SpeedSample(val served: Long, val atMs: Long)
    private val speedSamples = mutableMapOf<Int, SpeedSample>()
    private val resolvers = ResolverRegistry()
    private lateinit var store: AppStore
    private var queue = mutableListOf<QueueItem>()
    private var currentProvider = Provider.UNKNOWN
    private var currentOriginalUrl = ""
    private var pkgInfo: PkgInfo? = null

    private lateinit var linkInput: EditText
    private lateinit var ipInput: EditText
    private lateinit var status: TextView
    private lateinit var details: TextView
    private lateinit var queueSummary: TextView
    private lateinit var progress: ProgressBar
    private lateinit var officialButton: Button
    private lateinit var sendButton: Button
    private lateinit var addQueueButton: Button
    private lateinit var sendQueueButton: Button
    private lateinit var downloadsStatus: TextView
    private lateinit var monitorButton: Button
    private var payloadMode = false
    private var busy = false
    private lateinit var modeGroup: RadioGroup
    private lateinit var payloadPanel: LinearLayout
    private lateinit var receiverPanel: LinearLayout
    private lateinit var binPortInput: EditText
    private lateinit var elfPortInput: EditText
    private lateinit var payloadLog: TextView
    private val payloadActions = mutableListOf<Button>()
    private val receiverModeId = View.generateViewId()
    private val payloadModeId = View.generateViewId()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        store = AppStore(this)
        queue = store.loadQueue()
        setContentView(buildUi())
        restoreState()
        updateQueueSummary()
        acceptSharedText(intent)
    }

    override fun onNewIntent(intent: Intent?) {
        super.onNewIntent(intent)
        if (intent != null) {
            setIntent(intent)
            acceptSharedText(intent)
        }
    }

    private fun buildUi(): View {
        val scroll = ScrollView(this).apply { setBackgroundColor(Color.rgb(16, 19, 26)) }
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(18), dp(18), dp(32))
        }
        scroll.addView(root, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        root.addView(text("TAYSON PS4 PAYLOAD LAB", 24f, true).apply { gravity = Gravity.CENTER_HORIZONTAL })
        root.addView(text("1.5.0 experimental • Receiver + Payload • Rootz • VikingFile • AkiraBox • Mocha • direto", 13f, false).apply {
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(0, dp(4), 0, dp(18))
        })

        root.addView(label("Link do arquivo / PKG"))
        linkInput = edit("https://…").apply { inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI }
        root.addView(linkInput)
        root.addView(button("ANALISAR LINK") { analyzeInput() })
        officialButton = button("ABRIR FLUXO OFICIAL / CAPTURAR DOWNLOAD") { openOfficialFlow() }.apply { isEnabled = false }
        root.addView(officialButton)
        root.addView(button("HISTÓRICO DE LINKS") { showHistory() })

        progress = ProgressBar(this).apply { visibility = View.GONE; isIndeterminate = true }
        root.addView(progress, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(36)))

        status = text("Abra o Tayson PS4 Receiver no console e analise um link.", 15f, true)
        root.addView(card(status))
        details = text("", 14f, false)
        root.addView(card(details))

        root.addView(label("PS4 — endereço na rede local"))
        ipInput = edit("192.168.0.10").apply { inputType = InputType.TYPE_CLASS_PHONE }
        root.addView(ipInput)
        modeGroup = RadioGroup(this).apply {
            orientation = RadioGroup.VERTICAL
            addView(RadioButton(this@MainActivity).apply {
                id = receiverModeId; text = "Receiver — instalar PKGs"; setTextColor(Color.WHITE)
            })
            addView(RadioButton(this@MainActivity).apply {
                id = payloadModeId; text = "Payload experimental — testar conexão e HTTPS"; setTextColor(Color.WHITE)
            })
            check(receiverModeId)
        }
        root.addView(modeGroup)
        receiverPanel = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        root.addView(receiverPanel)
        receiverPanel.addView(button("LOCALIZAR RECEIVER AUTOMATICAMENTE") { discoverReceiver() })
        receiverPanel.addView(button("TESTAR RECEIVER :${ReceiverClient.PORT}") { testReceiver() })
        receiverPanel.addView(text("Abra o Receiver no PS4 para instalar e acompanhar PKGs. O download vai da Internet para o console.", 12f, false))
        payloadPanel = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; visibility = View.GONE }
        root.addView(payloadPanel)
        payloadPanel.addView(text("Primeira etapa: carregar o payload e testar 16 bytes do link no próprio PS4. Para instalar PKGs, selecione Receiver.", 13f, false))
        payloadPanel.addView(label("Porta do BinLoader (GoldHEN)"))
        binPortInput = edit("9090").apply { inputType = InputType.TYPE_CLASS_NUMBER; setText("9090") }
        payloadPanel.addView(binPortInput)
        payloadPanel.addView(label("Porta do carregador ELF"))
        elfPortInput = edit("9021").apply { inputType = InputType.TYPE_CLASS_NUMBER; setText("9021") }
        payloadPanel.addView(elfPortInput)
        for ((title, action) in listOf<Pair<String, () -> Unit>>(
            "1. CARREGAR PAYLOAD" to { loadPayload() },
            "2. TESTAR PAYLOAD" to { pingPayload() },
            "COPIAR DIAGNÓSTICO" to { copyPayloadLog() }
        )) {
            val b = button(title, action); payloadActions.add(b); payloadPanel.addView(b)
        }
        payloadLog = text("Ative o BinLoader no PS4 e toque em Carregar payload.", 13f, false)
        payloadPanel.addView(card(payloadLog))

        receiverPanel.addView(label("Downloads no PS4"))
        downloadsStatus = text("Monitoramento parado. Toque em Atualizar ou inicie o monitoramento automático.", 13f, false)
        receiverPanel.addView(card(downloadsStatus))
        val monitorActions = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        monitorActions.addView(button("ATUALIZAR") { refreshDownloads(true) }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        monitorButton = button("MONITORAR") { toggleMonitoring() }
        monitorActions.addView(monitorButton, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        receiverPanel.addView(monitorActions)

        sendButton = button("ENVIAR PKG AO PS4 RECEIVER") { if (payloadMode) testPayloadUrl() else sendCurrent() }.apply { isEnabled = false }
        root.addView(sendButton)

        root.addView(label("Fila de downloads"))
        queueSummary = text("Fila vazia.", 14f, false)
        root.addView(card(queueSummary))
        addQueueButton = button("ADICIONAR PKG ATUAL À FILA") { addCurrentToQueue() }.apply { isEnabled = false }
        root.addView(addQueueButton)
        root.addView(button("VER / EDITAR FILA") { showQueue() })
        sendQueueButton = button("ENVIAR FILA AO RECEIVER") { sendQueue() }.apply { isEnabled = false }
        root.addView(sendQueueButton)
        root.addView(button("LIMPAR FILA") { clearQueueWithConfirmation() })

        root.addView(text(
            "O aplicativo não remove anúncios, CAPTCHAs ou logins. Ele usa somente a URL e o contexto HTTP obtidos pelo fluxo autorizado do provedor. O PS4 Receiver mantém o BGFT integrado à tela normal de Downloads.",
            12f, false
        ).apply { setPadding(0, dp(18), 0, 0) })
        modeGroup.setOnCheckedChangeListener { _, checked ->
            payloadMode = checked == payloadModeId
            applyTransportMode()
            savePrefs()
        }
        return scroll
    }

    private fun applyTransportMode() {
        payloadPanel.visibility = if (payloadMode) View.VISIBLE else View.GONE
        receiverPanel.visibility = if (payloadMode) View.GONE else View.VISIBLE
        sendButton.text = if (payloadMode) "3. TESTAR LINK NO PAYLOAD (16 BYTES)" else "ENVIAR PKG AO PS4 RECEIVER"
        sendQueueButton.isEnabled = !busy && !payloadMode && queue.isNotEmpty()
        if (payloadMode) stopMonitoring()
        pkgInfo?.let { renderPkg(it) }
    }

    private fun appendPayloadLog(line: String) {
        payloadLog.text = (payloadLog.text.toString() + "\n" + line).takeLast(8000)
    }

    private fun copyPayloadLog() {
        val clipboard = getSystemService(CLIPBOARD_SERVICE) as android.content.ClipboardManager
        clipboard.setPrimaryClip(android.content.ClipData.newPlainText("Tayson Payload", payloadLog.text))
        toast("Diagnóstico copiado. Ele não inclui URL, cookies ou tokens.")
    }

    private fun loadPayload() {
        if (busy) return
        val host = ipInput.text.toString().trim()
        val binPort = binPortInput.text.toString().toIntOrNull() ?: 0
        val elfPort = elfPortInput.text.toString().toIntOrNull() ?: 0
        savePrefs()
        payloadLog.text = "Tayson Payload Lab 1.5.0 • iniciando"
        setBusy(true, "Carregando payload…")
        executor.execute {
            try {
                val result = PayloadLoader.start(this, host, binPort, elfPort) { stage -> runOnUiThread { appendPayloadLog(stage) } }
                runOnUiThread {
                    appendPayloadLog("Execução confirmada: payload ${result.version}; PID ${result.pid}; etapa ${result.stage}.")
                    setBusy(false, "✓ Payload respondeu no PS4. Analise um link e teste a leitura.")
                }
            } catch (e: Exception) {
                runOnUiThread { appendPayloadLog("Falha: ${e.message}"); setBusy(false, "Payload não confirmado. Confira o diagnóstico.") }
            }
        }
    }

    private fun pingPayload() {
        if (busy) return
        val host = ipInput.text.toString().trim()
        setBusy(true, "Consultando o payload…")
        executor.execute {
            try {
                val result = PayloadProbeClient.ping(host)
                runOnUiThread {
                    appendPayloadLog("Payload ${result.version}; PID ${result.pid}; ocupado=${result.busy}; etapa=${result.stage}.")
                    setBusy(false, "✓ Payload respondeu.")
                }
            } catch (e: Exception) {
                runOnUiThread { appendPayloadLog("Sem resposta: ${e.message}"); setBusy(false, "Carregue o payload e confira o IP do PS4.") }
            }
        }
    }

    private fun testPayloadUrl() {
        if (busy) return
        val info = pkgInfo ?: run { toast("Analise um PKG primeiro"); return }
        val host = ipInput.text.toString().trim()
        setBusy(true, "Testando a leitura no próprio PS4…")
        appendPayloadLog("Iniciando teste de 16 bytes; provedor=${currentProvider.label}.")
        executor.execute {
            try {
                val result = PayloadProbeClient.probe(host, info) { step -> runOnUiThread { appendPayloadLog("Etapa: $step") } }
                runOnUiThread {
                    appendPayloadLog("Resultado: ${result.scheme.uppercase()} ${result.http}; bytes=${result.bytes}; cabeçalho PKG=${result.pkg}.")
                    if (!result.pkg) setBusy(false, "O PS4 leu a resposta, mas ela não tem cabeçalho PKG. Reanalise o link.")
                    else if (result.http != 206) setBusy(false, "O PS4 leu o PKG, mas o servidor ignorou o Range. Compatibilidade parcial.")
                    else setBusy(false, "✓ O PS4 leu 16 bytes do PKG por ${result.scheme.uppercase()} (206). Este teste não instala o arquivo.")
                }
            } catch (e: Exception) {
                runOnUiThread { appendPayloadLog("Falha: ${e.message}"); setBusy(false, "O teste não concluiu. Copie o diagnóstico para análise.") }
            }
        }
    }

    private fun analyzeInput() {
        val url = linkInput.text.toString().trim()
        if (url.isBlank()) { toast("Cole um link primeiro"); return }
        currentOriginalUrl = url
        savePrefs()
        setBusy(true, "Analisando link…")
        clearPkg()
        executor.execute {
            try {
                val r = resolvers.resolve(url)
                currentProvider = r.provider
                runOnUiThread {
                    status.text = "${r.provider.label}: ${r.message}"
                    officialButton.isEnabled = r.needsOfficialFlow
                    details.text = buildString {
                        r.name?.let { append("Arquivo: $it\n") }
                        r.declaredSize?.let { append("Tamanho informado: ${Format.bytes(it)}\n") }
                    }.trim()
                }
                if (r.candidateUrl != null) scanCandidate(r.candidateUrl)
                else runOnUiThread { setBusy(false) }
            } catch (e: Exception) {
                runOnUiThread { setBusy(false); status.text = "Falha: ${e.message}" }
            }
        }
    }

    private fun scanCandidate(url: String, requestContext: RequestContext? = null) {
        try {
            runOnUiThread { status.text = "Validando URL e lendo metadados do PKG…" }
            val info = PkgScanner.scan(url, requestContext)
            pkgInfo = info
            store.addHistory(
                HistoryEntry(
                    originalUrl = currentOriginalUrl.ifBlank { info.sourceUrl },
                    provider = currentProvider,
                    fileName = info.fileName,
                    titleId = info.titleId,
                    title = info.sfo?.title,
                    lastUsedAt = System.currentTimeMillis()
                )
            )
            runOnUiThread {
                setBusy(false)
                officialButton.isEnabled = false
                setPkgButtons(true)
                status.text = if (payloadMode) "✓ PKG válido. Pronto para testar a leitura no payload." else "✓ PKG válido. Pronto para enviar ao PS4 Receiver."
                renderPkg(info)
            }
        } catch (e: Exception) {
            runOnUiThread {
                setBusy(false)
                status.text = "URL ainda não está pronta para o Receiver: ${e.message}"
                officialButton.isEnabled = true
            }
        }
    }

    private fun renderPkg(info: PkgInfo) {
        val sfo = info.sfo
        details.text = buildString {
            sfo?.title?.let { append("Título: $it\n") }
            append("Arquivo: ${info.fileName}\n")
            append("Provedor: ${currentProvider.label}\n")
            append(if (payloadMode) "Transporte: Payload experimental (teste Internet → PS4)\n" else "Transporte: PS4 Receiver (Internet → PS4)\n")
            append("Tamanho: ${Format.bytes(info.size)}\n")
            append("Content ID: ${info.contentId}\n")
            append("Title ID: ${info.titleId ?: "não identificado"}\n")
            append("Package Type: ${info.packageType} (0x${info.contentTypeValue.toString(16)})\n")
            sfo?.appVersion?.let { append("APP_VER: $it\n") }
            sfo?.version?.let { append("VERSION: $it\n") }
            sfo?.category?.let { append("CATEGORY: $it\n") }
            append("HTTP: ${info.httpCode}\n")
            append("Range: ${if (info.rangeSupported) "sim" else "não confirmado"}\n")
            info.requestContext?.let { ctx ->
                val parts = buildList {
                    if (!ctx.userAgent.isNullOrBlank()) add("UA")
                    if (!ctx.referer.isNullOrBlank()) add("Referer")
                    if (!ctx.cookie.isNullOrBlank()) add("Cookie")
                    if (ctx.extraHeaders.isNotEmpty()) add("${ctx.extraHeaders.size} header(s)")
                }
                if (parts.isNotEmpty()) append("Contexto HTTP: ${parts.joinToString(" + ")}\n")
            }
            append("URL final: ${Format.redactUrl(info.finalUrl)}")
        }
    }

    private fun openOfficialFlow() {
        val url = linkInput.text.toString().trim()
        if (url.isBlank()) return
        startActivityForResult(
            Intent(this, LinkCaptureActivity::class.java).putExtra(LinkCaptureActivity.EXTRA_URL, url),
            REQ_CAPTURE
        )
    }

    private fun discoverReceiver() {
        setBusy(true, "Procurando Tayson PS4 Receiver :${ReceiverClient.PORT} na rede local…")
        executor.execute {
            try {
                val result = ReceiverDiscovery.scan(this)
                runOnUiThread {
                    setBusy(false)
                    when (result.matches.size) {
                        0 -> status.text = "Nenhum Tayson Receiver encontrado no mesmo /24 (${result.localIp}). Abra o Receiver no PS4."
                        1 -> {
                            val m = result.matches.first()
                            ipInput.setText(m.ip)
                            savePrefs()
                            status.text = "✓ Tayson Receiver ${m.version} encontrado em ${m.ip}:${ReceiverClient.PORT}"
                        }
                        else -> {
                            val labels = result.matches.map { "${it.ip} • Receiver ${it.version}" }.toTypedArray()
                            AlertDialog.Builder(this)
                                .setTitle("Receivers encontrados")
                                .setItems(labels) { _, which ->
                                    ipInput.setText(result.matches[which].ip)
                                    savePrefs()
                                }
                                .setNegativeButton("Cancelar", null)
                                .show()
                        }
                    }
                }
            } catch (e: Exception) {
                runOnUiThread { setBusy(false); status.text = "Falha na descoberta: ${e.message}" }
            }
        }
    }

    private fun testReceiver() {
        val host = ipInput.text.toString().trim()
        if (host.isBlank()) { toast("Informe o IP do PS4"); return }
        setBusy(true, "Testando $host:${ReceiverClient.PORT}…")
        executor.execute {
            try {
                val p = ReceiverClient.probe(host)
                runOnUiThread { setBusy(false); status.text = "✓ Tayson PS4 Receiver ${p.version} respondeu em $host:${ReceiverClient.PORT}." }
            } catch (e: Exception) {
                runOnUiThread { setBusy(false); status.text = "Receiver não respondeu: ${e.message}" }
            }
        }
    }

    private fun sendCurrent() {
        val current = pkgInfo ?: run { toast("Analise um PKG válido primeiro"); return }
        val host = ipInput.text.toString().trim()
        if (host.isBlank()) { toast("Informe ou localize o PS4 Receiver"); return }
        savePrefs()
        setBusy(true, "Revalidando a URL antes de enviar ao Receiver…")
        executor.execute {
            try {
                val fresh = PkgScanner.scan(current.finalUrl, current.requestContext)
                pkgInfo = fresh
                runOnUiThread { status.text = "Enviando metadados ao PS4 Receiver…" }
                val result = ReceiverClient.sendJob(host, fresh)
                result.jobId?.let { jobId ->
                    store.addReceiverJob(
                        ReceiverJobMeta(
                            receiverJobId = jobId,
                            bgftTaskId = result.taskId,
                            contentId = fresh.contentId,
                            title = fresh.sfo?.title,
                            fileName = fresh.fileName,
                            size = fresh.size,
                            sentAt = System.currentTimeMillis()
                        )
                    )
                }
                runOnUiThread {
                    setBusy(false)
                    renderPkg(fresh)
                    status.text = buildString {
                        append("✓ Download registrado no PS4")
                        result.jobId?.let { append(" • job $it") }
                        result.taskId?.let { append(" • BGFT $it") }
                        result.upstreamHttp?.let { append(" • servidor HTTP $it") }
                        append(". Confira Downloads no PS4.\n")
                        append("Mantenha o Tayson PS4 Receiver aberto enquanto a transferência estiver ativa.")
                    }
                    startMonitoring()
                }
            } catch (e: Exception) {
                runOnUiThread { setBusy(false); status.text = "Falha ao enviar ao Receiver: ${e.message}" }
            }
        }
    }

    private fun addCurrentToQueue() {
        val info = pkgInfo ?: return
        if (queue.any { it.pkg.contentId == info.contentId && it.pkg.size == info.size }) {
            toast("Esse PKG já está na fila")
            return
        }
        queue.add(
            QueueItem(
                System.nanoTime(),
                System.currentTimeMillis(),
                info,
                currentOriginalUrl.ifBlank { info.sourceUrl },
                currentProvider
            )
        )
        store.saveQueue(queue)
        updateQueueSummary()
        toast("Adicionado à fila")
    }

    private fun showQueue() {
        if (queue.isEmpty()) { toast("A fila está vazia"); return }
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12), dp(8), dp(12), dp(8))
        }
        val scroll = ScrollView(this).apply { addView(content) }
        queue.toList().forEachIndexed { index, item ->
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(dp(8), dp(8), dp(8), dp(8))
                setBackgroundColor(Color.rgb(27, 31, 41))
            }
            row.addView(text("${index + 1}. ${item.pkg.sfo?.title ?: item.pkg.fileName}", 14f, true))
            row.addView(text("${item.provider.label} • ${item.pkg.titleId ?: "sem Title ID"} • ${Format.bytes(item.pkg.size)}", 12f, false))
            val actions = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            actions.addView(button("CARREGAR") {
                pkgInfo = item.pkg
                currentOriginalUrl = item.originalUrl
                currentProvider = item.provider
                linkInput.setText(item.originalUrl)
                setPkgButtons(true)
                renderPkg(item.pkg)
                status.text = "Item carregado da fila (${item.provider.label})."
            }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            actions.addView(button("REMOVER") {
                queue.removeAll { it.id == item.id }
                store.saveQueue(queue)
                updateQueueSummary()
                toast("Item removido. Reabra a fila para atualizar a lista.")
            }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            row.addView(actions)
            val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            lp.setMargins(0, 0, 0, dp(8))
            content.addView(row, lp)
        }
        AlertDialog.Builder(this).setTitle("Fila (${queue.size})").setView(scroll).setPositiveButton("Fechar", null).show()
    }

    private fun sendQueue() {
        if (payloadMode) { toast("Selecione Receiver para instalar a fila"); return }
        if (queue.isEmpty()) { toast("A fila está vazia"); return }
        val host = ipInput.text.toString().trim()
        if (host.isBlank()) { toast("Informe ou localize o PS4 Receiver"); return }
        val snapshot = queue.toList()
        savePrefs()
        setBusy(true, "Enviando fila: 0/${snapshot.size}…")
        executor.execute {
            var sent = 0
            try {
                ReceiverClient.probe(host)
                for ((index, item) in snapshot.withIndex()) {
                    runOnUiThread { status.text = "Revalidando ${index + 1}/${snapshot.size}: ${item.pkg.titleId ?: item.pkg.fileName}" }
                    val fresh = revalidateQueueItem(item)
                    runOnUiThread { status.text = "Registrando ${index + 1}/${snapshot.size} no Receiver…" }
                    val result = ReceiverClient.sendJob(host, fresh)
                    result.jobId?.let { jobId ->
                        store.addReceiverJob(
                            ReceiverJobMeta(
                                receiverJobId = jobId,
                                bgftTaskId = result.taskId,
                                contentId = fresh.contentId,
                                title = fresh.sfo?.title,
                                fileName = fresh.fileName,
                                size = fresh.size,
                                sentAt = System.currentTimeMillis()
                            )
                        )
                    }
                    sent++
                    Thread.sleep(300)
                }
                runOnUiThread {
                    setBusy(false)
                    status.text = "✓ Fila enviada: $sent/${snapshot.size} PKGs registrados no Receiver/BGFT."
                    startMonitoring()
                }
            } catch (e: Exception) {
                runOnUiThread { setBusy(false); status.text = "Fila interrompida após $sent/${snapshot.size}: ${e.message}" }
            }
        }
    }

    private fun revalidateQueueItem(item: QueueItem): PkgInfo {
        return try {
            PkgScanner.scan(item.pkg.finalUrl, item.pkg.requestContext)
        } catch (first: Exception) {
            if (item.originalUrl == item.pkg.finalUrl) throw first
            val resolved = resolvers.resolve(item.originalUrl)
            val candidate = resolved.candidateUrl
            if (candidate != null) return PkgScanner.scan(candidate)
            throw IllegalStateException(
                "A URL temporária de ${item.provider.label} expirou. Carregue o item e autorize o download novamente. (${first.message})"
            )
        }
    }

    private fun clearQueueWithConfirmation() {
        if (queue.isEmpty()) return
        AlertDialog.Builder(this)
            .setTitle("Limpar fila?")
            .setMessage("Os links do histórico serão mantidos.")
            .setPositiveButton("Limpar") { _, _ ->
                queue.clear(); store.saveQueue(queue); updateQueueSummary()
            }
            .setNegativeButton("Cancelar", null)
            .show()
    }

    private fun updateQueueSummary() {
        if (!::queueSummary.isInitialized) return
        if (queue.isEmpty()) {
            queueSummary.text = "Fila vazia."
            sendQueueButton.isEnabled = false
        } else {
            val total = queue.sumOf { it.pkg.size }
            queueSummary.text = "${queue.size} PKG(s) • ${Format.bytes(total)}\n" + queue.take(3).joinToString("\n") {
                "• ${it.pkg.sfo?.title ?: it.pkg.titleId ?: it.pkg.fileName}"
            } + if (queue.size > 3) "\n… +${queue.size - 3}" else ""
            sendQueueButton.isEnabled = !busy && !payloadMode
        }
    }

    private fun showHistory() {
        val history = store.loadHistory()
        if (history.isEmpty()) { toast("Nenhum link no histórico"); return }
        val labels = history.map {
            val date = DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT).format(Date(it.lastUsedAt))
            "${it.title ?: it.titleId ?: it.fileName ?: it.provider.label}\n${it.provider.label} • $date"
        }.toTypedArray()
        AlertDialog.Builder(this)
            .setTitle("Histórico")
            .setItems(labels) { _, which ->
                currentOriginalUrl = history[which].originalUrl
                currentProvider = history[which].provider
                linkInput.setText(history[which].originalUrl)
                status.text = "Link carregado do histórico. Toque em Analisar."
            }
            .setNeutralButton("Limpar histórico") { _, _ -> store.clearHistory(); toast("Histórico limpo") }
            .setNegativeButton("Fechar", null)
            .show()
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (resultCode != RESULT_OK || data == null) return
        if (requestCode == REQ_CAPTURE) {
            val captured = data.getStringExtra(LinkCaptureActivity.EXTRA_CAPTURED_URL) ?: return
            val extraHeaders = data.getStringExtra(LinkCaptureActivity.EXTRA_HEADERS_JSON)?.let { raw ->
                runCatching {
                    val o = JSONObject(raw)
                    buildMap<String, String> {
                        val keys = o.keys()
                        while (keys.hasNext()) {
                            val k = keys.next()
                            val v = o.optString(k)
                            if (k.isNotBlank() && v.isNotBlank()) put(k, v)
                        }
                    }
                }.getOrDefault(emptyMap())
            } ?: emptyMap()
            val ctx = RequestContext(
                userAgent = data.getStringExtra(LinkCaptureActivity.EXTRA_USER_AGENT)?.takeIf { it.isNotBlank() },
                referer = data.getStringExtra(LinkCaptureActivity.EXTRA_REFERER)?.takeIf { it.isNotBlank() },
                cookie = data.getStringExtra(LinkCaptureActivity.EXTRA_COOKIE)?.takeIf { it.isNotBlank() },
                extraHeaders = extraHeaders
            )
            setBusy(true, "Download autorizado capturado. Verificando compatibilidade…")
            executor.execute { scanCandidate(captured, ctx) }
        }
    }

    private fun toggleMonitoring() {
        if (monitoring) stopMonitoring() else startMonitoring()
    }

    private fun startMonitoring() {
        monitoring = true
        updateMonitorButton()
        if (activityResumed) {
            monitorHandler.removeCallbacks(monitorTick)
            monitorHandler.post(monitorTick)
        }
    }

    private fun stopMonitoring() {
        monitoring = false
        monitorHandler.removeCallbacks(monitorTick)
        updateMonitorButton()
    }

    private fun updateMonitorButton() {
        if (::monitorButton.isInitialized) monitorButton.text = if (monitoring) "PARAR" else "MONITORAR"
    }

    private val monitorTick = object : Runnable {
        override fun run() {
            if (!monitoring || !activityResumed) return
            refreshDownloads(false)
            monitorHandler.postDelayed(this, MONITOR_INTERVAL_MS)
        }
    }

    private fun refreshDownloads(manual: Boolean) {
        val host = ipInput.text.toString().trim()
        if (host.isBlank()) {
            if (manual) toast("Informe ou localize o PS4 Receiver")
            return
        }
        if (monitorInFlight) return
        monitorInFlight = true
        executor.execute {
            try {
                val result = ReceiverClient.status(host)
                val now = System.currentTimeMillis()
                val metas = store.loadReceiverJobs()
                val rendered = buildString {
                    append("Receiver ${result.version} • ${result.jobs.size} job(s)\n")
                    if (result.jobs.isEmpty()) {
                        append("Nenhum download registrado nesta sessão do Receiver.")
                    } else {
                        result.jobs.forEachIndexed { index, job ->
                            val meta = metas.firstOrNull { it.receiverJobId == job.jobId && it.contentId == job.contentId }
                            val old = speedSamples[job.jobId]
                            val deltaBytes = if (old != null) (job.served - old.served).coerceAtLeast(0L) else 0L
                            val deltaMs = if (old != null) (now - old.atMs).coerceAtLeast(1L) else 1L
                            val rate = if (old != null) deltaBytes * 1000L / deltaMs else 0L
                            speedSamples[job.jobId] = SpeedSample(job.served, now)
                            val effectiveSize = if (job.size > 0) job.size else (meta?.size ?: 0L)
                            val done = if (effectiveSize > 0) job.served.coerceAtMost(effectiveSize) else job.served
                            val stateLabel = when (job.state) {
                                "downloading" -> "Baixando"
                                "complete" -> "Transferido"
                                "error" -> "Erro / tentando novamente"
                                "queued" -> "Aguardando / pausado"
                                else -> "${job.state}"
                            }
                            if (index > 0) append("\n\n")
                            append("#${job.jobId}")
                            job.taskId?.let { append(" • BGFT $it") }
                            append(" • $stateLabel")
                            append("\n${meta?.title ?: meta?.fileName ?: job.contentId}")
                            if (effectiveSize > 0) {
                                append("\n${Format.percent(done, effectiveSize)} aprox. • ${Format.bytes(done)} / ${Format.bytes(effectiveSize)}")
                            } else {
                                append("\n${Format.bytes(done)} transferidos")
                            }
                            append(" • ${Format.rate(rate)}")
                            if (job.activeConnections > 0) append(" • ${job.activeConnections} conexão(ões)")
                            job.upstreamHttp?.let { append("\nHTTP $it") }
                            if (job.lastError != 0) append(" • erro 0x${job.lastError.toUInt().toString(16).uppercase().padStart(8, '0')}")
                        }
                    }
                }
                runOnUiThread {
                    monitorInFlight = false
                    downloadsStatus.text = rendered
                }
            } catch (e: Exception) {
                runOnUiThread {
                    monitorInFlight = false
                    downloadsStatus.text = "Monitoramento indisponível: ${e.message}"
                }
            }
        }
    }

    private fun clearPkg() {
        pkgInfo = null
        setPkgButtons(false)
    }

    private fun setPkgButtons(enabled: Boolean) {
        sendButton.isEnabled = enabled
        addQueueButton.isEnabled = enabled
    }

    private fun setBusy(busy: Boolean, message: String? = null) {
        this.busy = busy
        if (::modeGroup.isInitialized) for (i in 0 until modeGroup.childCount) modeGroup.getChildAt(i).isEnabled = !busy
        payloadActions.forEach { it.isEnabled = !busy }
        sendQueueButton.isEnabled = !busy && !payloadMode && queue.isNotEmpty()
        progress.visibility = if (busy) View.VISIBLE else View.GONE
        message?.let { status.text = it }
    }

    private fun restoreState() {
        val p = getSharedPreferences("state_v1", MODE_PRIVATE)
        ipInput.setText(p.getString("receiver_ip", ""))
        linkInput.setText(p.getString("last_link", ""))
        binPortInput.setText(p.getString("bin_port", "9090"))
        elfPortInput.setText(p.getString("elf_port", "9021"))
        modeGroup.check(if (p.getBoolean("payload_mode", false)) payloadModeId else receiverModeId)
        applyTransportMode()
    }

    private fun savePrefs() {
        getSharedPreferences("state_v1", MODE_PRIVATE).edit()
            .putString("receiver_ip", ipInput.text.toString().trim())
            .putString("last_link", linkInput.text.toString().trim())
            .putString("bin_port", binPortInput.text.toString())
            .putString("elf_port", elfPortInput.text.toString())
            .putBoolean("payload_mode", payloadMode)
            .apply()
    }

    private fun acceptSharedText(intent: Intent) {
        if (intent.action == Intent.ACTION_SEND && intent.type == "text/plain") {
            intent.getStringExtra(Intent.EXTRA_TEXT)?.let {
                val url = Regex("https?://\\S+").find(it)?.value ?: it
                currentOriginalUrl = url.trim()
                linkInput.setText(url.trim())
            }
        }
    }

    private fun label(s: String) = text(s, 13f, true).apply { setPadding(0, dp(16), 0, dp(6)) }
    private fun text(s: String, size: Float, bold: Boolean) = TextView(this).apply {
        text = s
        textSize = size
        setTextColor(Color.rgb(235, 239, 247))
        if (bold) setTypeface(typeface, android.graphics.Typeface.BOLD)
        setTextIsSelectable(true)
    }
    private fun edit(hintText: String) = EditText(this).apply {
        hint = hintText
        setHintTextColor(Color.rgb(120, 130, 145))
        setTextColor(Color.WHITE)
        setSingleLine(true)
        setPadding(dp(12), dp(10), dp(12), dp(10))
        setBackgroundColor(Color.rgb(34, 39, 50))
    }
    private fun button(label: String, click: () -> Unit) = Button(this).apply {
        text = label
        isAllCaps = false
        setOnClickListener { click() }
        setPadding(dp(10), dp(7), dp(10), dp(7))
    }
    private fun card(v: View) = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        setPadding(dp(12), dp(12), dp(12), dp(12))
        setBackgroundColor(Color.rgb(27, 31, 41))
        addView(v)
        val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
        lp.setMargins(0, dp(8), 0, dp(8))
        layoutParams = lp
    }
    private fun toast(s: String) = Toast.makeText(this, s, Toast.LENGTH_LONG).show()
    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()

    override fun onResume() {
        super.onResume()
        activityResumed = true
        if (monitoring) {
            monitorHandler.removeCallbacks(monitorTick)
            monitorHandler.post(monitorTick)
        }
    }

    override fun onPause() {
        activityResumed = false
        monitorHandler.removeCallbacks(monitorTick)
        savePrefs()
        super.onPause()
    }

    override fun onDestroy() {
        monitorHandler.removeCallbacksAndMessages(null)
        executor.shutdownNow()
        super.onDestroy()
    }
}
