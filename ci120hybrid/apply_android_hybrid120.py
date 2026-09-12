from pathlib import Path

root = Path('.')
main = root / 'app/src/main/java/com/tayson/ps4linksender/MainActivity.kt'
gradle = root / 'app/build.gradle.kts'
manifest = root / 'app/src/main/AndroidManifest.xml'
relay = root / 'app/src/main/java/com/tayson/ps4linksender/relay/RelayHttpServer.kt'

s = main.read_text()

s = s.replace(
    'import com.tayson.ps4linksender.ps4.ReceiverClient\nimport com.tayson.ps4linksender.ps4.ReceiverDiscovery\n',
    'import com.tayson.ps4linksender.ps4.ReceiverClient\n'
    'import com.tayson.ps4linksender.ps4.ReceiverDiscovery\n'
    'import com.tayson.ps4linksender.ps4.PayloadClient\n'
    'import com.tayson.ps4linksender.ps4.PayloadLoaderClient\n'
    'import com.tayson.ps4linksender.relay.RelayManager\n'
)

s = s.replace(
    'private lateinit var sendButton: Button\n',
    'private lateinit var sendButton: Button\n'
    '    private lateinit var payloadSendButton: Button\n'
    '    private lateinit var payloadStatus: TextView\n'
)

s = s.replace(
    'root.addView(text("1.1.0 • PS4 Receiver 1.1.0+ • Rootz • VikingFile • AkiraBox • Mocha • direto", 13f, false).apply {',
    'root.addView(text("1.2.0 HYBRID • BGFT Receiver + Payload Installer • Rootz • VikingFile • AkiraBox • Mocha • direto", 13f, false).apply {'
)

s = s.replace(
    'status.text = "✓ PKG válido. Pronto para enviar ao PS4 Receiver."',
    'status.text = "✓ PKG válido. Escolha BGFT ou Payload Local."'
)

s = s.replace(
    'append("Transporte: PS4 Receiver (Internet → PS4)\\n")',
    'append("Transporte: BGFT Receiver ou Payload Local\\n")'
)

old = '''        sendButton = button("ENVIAR PKG AO PS4 RECEIVER") { sendCurrent() }.apply { isEnabled = false }
        root.addView(sendButton)

        root.addView(label("Fila de downloads"))
'''
new = '''        sendButton = button("ENVIAR VIA BGFT RECEIVER") { sendCurrent() }.apply { isEnabled = false }
        root.addView(sendButton)

        root.addView(label("Payload Installer experimental — :${PayloadClient.PORT}"))
        payloadStatus = text(
            "Payload não testado. Ative o BinLoader Server no GoldHEN para carregá-lo pelo celular.",
            13f, false
        )
        root.addView(card(payloadStatus))
        root.addView(button("CARREGAR PAYLOAD NO PS4 :${PayloadLoaderClient.PORT}") { loadEmbeddedPayload() })
        root.addView(button("TESTAR PAYLOAD :${PayloadClient.PORT}") { testPayload() })
        payloadSendButton = button("ENVIAR VIA PAYLOAD LOCAL (EXPERIMENTAL)") { sendCurrentPayload() }.apply { isEnabled = false }
        root.addView(payloadSendButton)
        root.addView(button("ATUALIZAR STATUS DO PAYLOAD") { refreshPayloadStatus() })
        root.addView(text(
            "No modo Payload, o Android mantém um Relay local: CDN → celular → PS4. O PKG é salvo temporariamente em /data/tayson/payload-cache/current.pkg e depois entregue ao instalador local do PS4. O caminho BGFT continua disponível acima.",
            12f, false
        ).apply { setPadding(0, dp(4), 0, dp(8)) })

        root.addView(label("Fila de downloads"))
'''
if old not in s:
    raise SystemExit('send button anchor not found')
s = s.replace(old, new)

anchor = '    private fun addCurrentToQueue() {\n'
if anchor not in s:
    raise SystemExit('addCurrentToQueue anchor not found')

payload_methods = r'''    private fun loadEmbeddedPayload() {
        val host = ipInput.text.toString().trim()
        if (host.isBlank()) { toast("Informe ou localize o PS4"); return }
        savePrefs()
        setBusy(true, "Enviando Tayson Payload Installer ao BinLoader :${PayloadLoaderClient.PORT}…")
        executor.execute {
            try {
                val sent = PayloadLoaderClient.sendEmbedded(this, host)
                var probe: PayloadClient.ProbeResult? = null
                var last: Exception? = null
                repeat(12) {
                    if (probe == null) {
                        try {
                            Thread.sleep(500)
                            probe = PayloadClient.probe(host)
                        } catch (e: Exception) { last = e }
                    }
                }
                val result = probe ?: throw IllegalStateException(
                    "${sent.bytesSent} bytes enviados ao BinLoader, mas o serviço :${PayloadClient.PORT} não respondeu. Verifique se o BinLoader Server do GoldHEN está ativo. ${last?.message.orEmpty()}"
                )
                runOnUiThread {
                    setBusy(false)
                    payloadStatus.text = "✓ Payload ${result.version} carregado em $host:${PayloadClient.PORT}. Pronto para receber um PKG."
                    status.text = "✓ Backend Payload ativo. BGFT Receiver continua disponível normalmente."
                }
            } catch (e: Exception) {
                runOnUiThread {
                    setBusy(false)
                    payloadStatus.text = "Falha ao carregar/testar Payload: ${e.message}"
                }
            }
        }
    }

    private fun testPayload() {
        val host = ipInput.text.toString().trim()
        if (host.isBlank()) { toast("Informe o IP do PS4"); return }
        setBusy(true, "Testando Payload $host:${PayloadClient.PORT}…")
        executor.execute {
            try {
                val p = PayloadClient.probe(host)
                runOnUiThread {
                    setBusy(false)
                    payloadStatus.text = "✓ Tayson Payload Installer ${p.version} respondeu em $host:${PayloadClient.PORT}."
                }
            } catch (e: Exception) {
                runOnUiThread {
                    setBusy(false)
                    payloadStatus.text = "Payload não respondeu: ${e.message}"
                }
            }
        }
    }

    private fun sendCurrentPayload() {
        val current = pkgInfo ?: run { toast("Analise um PKG válido primeiro"); return }
        val host = ipInput.text.toString().trim()
        if (host.isBlank()) { toast("Informe ou localize o PS4"); return }
        savePrefs()
        setBusy(true, "Revalidando URL e preparando Relay local…")
        executor.execute {
            try {
                val fresh = PkgScanner.scan(current.finalUrl, current.requestContext)
                pkgInfo = fresh
                PayloadClient.probe(host)
                val prepared = RelayManager.prepare(this, fresh)
                runOnUiThread {
                    status.text = "Relay ativo. Enviando trabalho ao Payload Installer…"
                    renderPkg(fresh)
                }
                val result = PayloadClient.start(host, prepared.relayUrl, fresh)
                runOnUiThread {
                    setBusy(false)
                    payloadStatus.text = buildString {
                        append("✓ Payload aceitou o trabalho.\n")
                        append("Estágio: CDN → Relay Android → cache local do PS4 → AppInstUtil.\n")
                        append("Mantenha o celular na mesma rede e o serviço Relay ativo até o download terminar.\n")
                        append(result.raw)
                    }
                    status.text = "✓ Transferência Payload iniciada. Use ‘Atualizar status do Payload’ para acompanhar."
                }
            } catch (e: Exception) {
                runOnUiThread {
                    setBusy(false)
                    payloadStatus.text = "Falha no modo Payload: ${e.message}"
                }
            }
        }
    }

    private fun refreshPayloadStatus() {
        val host = ipInput.text.toString().trim()
        if (host.isBlank()) { toast("Informe o IP do PS4"); return }
        executor.execute {
            try {
                val st = PayloadClient.status(host)
                val rendered = buildString {
                    val label = when (st.state) {
                        "idle" -> "Ocioso"
                        "downloading" -> "Baixando para cache local"
                        "installing" -> "Enviando ao instalador local"
                        "submitted" -> "PKG aceito pelo instalador local"
                        "cancelled" -> "Cancelado"
                        "error" -> "Erro"
                        else -> st.state
                    }
                    append("Payload :${PayloadClient.PORT} • $label")
                    if (st.total > 0) append("\n${Format.percent(st.bytes.coerceAtMost(st.total), st.total)} • ${Format.bytes(st.bytes)} / ${Format.bytes(st.total)}")
                    else if (st.bytes > 0) append("\n${Format.bytes(st.bytes)} recebidos")
                    st.http?.let { append("\nRelay HTTP $it") }
                    st.titleId?.let { append(" • $it") }
                    if (st.error != 0L) append("\nErro 0x${st.error.toULong().toString(16).uppercase().padStart(8, '0')}")
                    if (st.install != 0L) append(" • Install 0x${st.install.toULong().toString(16).uppercase().padStart(8, '0')}")
                }
                runOnUiThread { payloadStatus.text = rendered }
            } catch (e: Exception) {
                runOnUiThread { payloadStatus.text = "Status Payload indisponível: ${e.message}" }
            }
        }
    }

'''
s = s.replace(anchor, payload_methods + anchor)

s = s.replace(
    '    private fun setPkgButtons(enabled: Boolean) {\n        sendButton.isEnabled = enabled\n        addQueueButton.isEnabled = enabled\n    }',
    '    private fun setPkgButtons(enabled: Boolean) {\n'
    '        sendButton.isEnabled = enabled\n'
    '        if (::payloadSendButton.isInitialized) payloadSendButton.isEnabled = enabled\n'
    '        addQueueButton.isEnabled = enabled\n'
    '    }'
)

main.write_text(s)

g = gradle.read_text()
g = g.replace('versionCode = 16', 'versionCode = 17')
g = g.replace('versionName = "1.1.0"', 'versionName = "1.2.0"')
gradle.write_text(g)

m = manifest.read_text()
m = m.replace(
    '    <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />\n',
    '    <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />\n'
    '    <uses-permission android:name="android.permission.ACCESS_WIFI_STATE" />\n'
    '    <uses-permission android:name="android.permission.CHANGE_NETWORK_STATE" />\n'
    '    <uses-permission android:name="android.permission.WAKE_LOCK" />\n'
    '    <uses-permission android:name="android.permission.FOREGROUND_SERVICE" />\n'
    '    <uses-permission android:name="android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE" />\n'
)
service = '''\n        <service\n            android:name=".relay.RelayService"\n            android:exported="false"\n            android:foregroundServiceType="connectedDevice" />\n'''
m = m.replace('        <activity\n            android:name=".web.LinkCaptureActivity"', service + '\n        <activity\n            android:name=".web.LinkCaptureActivity"')
manifest.write_text(m)

r = relay.read_text()
old = '''                    setRequestProperty("User-Agent", UA)
                    setRequestProperty("Accept", "*/*")
                    setRequestProperty("Accept-Encoding", "identity")
                    setRequestProperty("Connection", "close")
                    if (range != null) setRequestProperty("Range", range)
'''
new = '''                    val ctx = entry.requestContext
                    setRequestProperty("User-Agent", ctx?.userAgent?.takeIf { it.isNotBlank() } ?: UA)
                    setRequestProperty("Accept", "*/*")
                    setRequestProperty("Accept-Encoding", "identity")
                    setRequestProperty("Connection", "close")
                    ctx?.referer?.takeIf { it.isNotBlank() }?.let { setRequestProperty("Referer", it) }
                    ctx?.cookie?.takeIf { it.isNotBlank() }?.let { setRequestProperty("Cookie", it) }
                    ctx?.extraHeaders?.forEach { (name, value) ->
                        if (isSafeUpstreamHeader(name, value)) setRequestProperty(name.trim(), value.trim())
                    }
                    if (range != null) setRequestProperty("Range", range)
'''
if old not in r:
    raise SystemExit('relay request-property anchor not found')
r = r.replace(old, new)
anchor2 = '''    private fun validateRedirect(base: String, location: String): String {
'''
helper = '''    private fun isSafeUpstreamHeader(rawName: String, rawValue: String): Boolean {
        val name = rawName.trim()
        val value = rawValue.trim()
        if (name.isBlank() || value.isBlank()) return false
        if (name.any { it == '\\r' || it == '\\n' || it == ':' } || value.any { it == '\\r' || it == '\\n' }) return false
        val blocked = setOf("host", "content-length", "range", "connection", "accept-encoding", "user-agent", "cookie", "referer")
        if (name.lowercase(Locale.US) in blocked) return false
        return name.all { it.isLetterOrDigit() || it == '-' || it == '_' }
    }

'''
if anchor2 not in r:
    raise SystemExit('relay helper anchor not found')
r = r.replace(anchor2, helper + anchor2)
relay.write_text(r)

# Sanity checks.
text = main.read_text()
for needle in [
    'versionName = "1.2.0"',
]:
    pass
assert 'ENVIAR VIA PAYLOAD LOCAL' in text
assert 'PayloadLoaderClient.sendEmbedded' in text
assert 'RelayManager.prepare' in text
assert 'PayloadClient.status' in text
assert 'foregroundServiceType="connectedDevice"' in manifest.read_text()
assert 'requestContext' in relay.read_text()
