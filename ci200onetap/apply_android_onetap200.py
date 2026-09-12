from pathlib import Path

root = Path('app/src/main/java/com/tayson/ps4linksender')
main = root / 'MainActivity.kt'
gradle = Path('app/build.gradle.kts')

s = main.read_text()

s = s.replace('import com.tayson.ps4linksender.ps4.AutoPilotEngine\n',
              'import com.tayson.ps4linksender.ps4.AutoPilotEngine\nimport com.tayson.ps4linksender.ps4.BgftPayloadClient\n')
s = s.replace('private var autoPilotInFlight = false',
              'private var autoPilotInFlight = false\n    private var oneTapPending = false')
s = s.replace('private lateinit var autoPilotSwitch: Switch',
              'private lateinit var autoPilotSwitch: Switch\n    private lateinit var oneTapButton: Button')

s = s.replace('root.addView(text("TAYSON PS4 LINK SENDER", 24f, true).apply { gravity = Gravity.CENTER_HORIZONTAL })',
              'root.addView(text("TAYSON PS4 DOWNLOADER", 24f, true).apply { gravity = Gravity.CENTER_HORIZONTAL })')
s = s.replace('1.3.0 AUTOPILOT • Payload Direct 0.2.0 + fallback BGFT • Rootz • VikingFile • AkiraBox • Mocha • direto',
              '2.0.0 ONE-TAP • Payload BGFT direto • VikingFile • AkiraBox • Rootz • Mocha • link direto')

subtitle_anchor = '''        root.addView(text("2.0.0 ONE-TAP • Payload BGFT direto • VikingFile • AkiraBox • Rootz • Mocha • link direto", 13f, false).apply {
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(0, dp(4), 0, dp(18))
        })

        root.addView(label("Link do arquivo / PKG"))
'''
subtitle_repl = '''        root.addView(text("2.0.0 ONE-TAP • Payload BGFT direto • VikingFile • AkiraBox • Rootz • Mocha • link direto", 13f, false).apply {
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(0, dp(4), 0, dp(18))
        })

        root.addView(label("IP do PS4 (GoldHEN BinLoader :9090)"))
        ipInput = edit("192.168.0.10").apply { inputType = InputType.TYPE_CLASS_PHONE }
        root.addView(ipInput)

        root.addView(label("Link do arquivo / PKG"))
'''
if subtitle_anchor not in s:
    raise SystemExit('subtitle anchor not found')
s = s.replace(subtitle_anchor, subtitle_repl, 1)

link_anchor = '''        linkInput = edit("https://…").apply { inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI }
        root.addView(linkInput)
        root.addView(button("ANALISAR LINK") { analyzeInput() })
'''
link_repl = '''        linkInput = edit("https://…").apply { inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI }
        root.addView(linkInput)
        oneTapButton = button("ENVIAR AO PS4") { sendOneTap() }
        root.addView(oneTapButton)
        root.addView(button("ANALISAR / VER DETALHES") { analyzeInput() })
'''
if link_anchor not in s:
    raise SystemExit('link anchor not found')
s = s.replace(link_anchor, link_repl, 1)

s = s.replace('''        root.addView(autoPilotSwitch)
        root.addView(button("INICIAR AUTOPILOT AGORA") { runAutopilot() })
''', '')
s = s.replace('isChecked = true\n            setPadding', 'isChecked = false\n            setPadding', 1)
s = s.replace('status = text("Abra o Tayson PS4 Receiver no console e analise um link.", 15f, true)',
              'status = text("Ative o BinLoader Server no GoldHEN, informe o IP do PS4, cole o link e toque em ENVIAR AO PS4.", 15f, true)')

s = s.replace('''        ipInput = edit("192.168.0.10").apply { inputType = InputType.TYPE_CLASS_PHONE }
        root.addView(ipInput)
        root.addView(button("LOCALIZAR RECEIVER AUTOMATICAMENTE") { discoverReceiver() })
''', '''        root.addView(button("LOCALIZAR RECEIVER AUTOMATICAMENTE") { discoverReceiver() })
''', 1)

s = s.replace('root.addView(label("Payload Installer experimental — :${PayloadClient.PORT}"))',
              'root.addView(label("Fallback / diagnóstico: Payload Installer — :${PayloadClient.PORT}"))')
s = s.replace('Payload 0.1.2 não testado. Ative o BinLoader Server no GoldHEN para carregá-lo pelo celular.',
              'O modo principal acima gera um payload BGFT por download e não precisa deste serviço persistente.')

analyze_anchor = '    private fun analyzeInput() {\n'
one_tap = '''    private fun sendOneTap() {
        val host = ipInput.text.toString().trim()
        val url = linkInput.text.toString().trim()
        if (host.isBlank()) { toast("Informe o IP do PS4"); return }
        if (url.isBlank()) { toast("Cole um link primeiro"); return }
        oneTapPending = true
        analyzeInput()
    }

    private fun sendBgftPayload(info: PkgInfo) {
        val host = ipInput.text.toString().trim()
        if (host.isBlank()) { oneTapPending = false; toast("Informe o IP do PS4"); return }
        savePrefs()
        setBusy(true, "Gerando payload BGFT e enviando ao GoldHEN BinLoader :${BgftPayloadClient.PORT}…")
        executor.execute {
            try {
                val result = BgftPayloadClient.send(this, host, info)
                runOnUiThread {
                    setBusy(false)
                    status.text = buildString {
                        append("✓ Payload enviado ao PS4 (${result.bytesSent} bytes).\\n")
                        append("O payload registrará o PKG no BGFT e encerrará. Depois disso o PS4 baixa diretamente da Internet; o celular pode sair do aplicativo.")
                    }
                    details.text = details.text.toString() + "\\nModo: Payload BGFT direto → PS4 Downloads"
                }
            } catch (e: Exception) {
                runOnUiThread {
                    setBusy(false)
                    status.text = buildString {
                        append("Falha ao enviar payload direto: ${e.message}\\n")
                        append("Confirme que o GoldHEN > BinLoader Server está ativado e que o IP do PS4 está correto. O Receiver/BGFT continua disponível nos controles avançados.")
                    }
                }
            }
        }
    }

'''
if analyze_anchor not in s:
    raise SystemExit('analyze anchor not found')
s = s.replace(analyze_anchor, one_tap + analyze_anchor, 1)

old = '''                if (r.candidateUrl != null) scanCandidate(r.candidateUrl)
                else runOnUiThread { setBusy(false) }
'''
new = '''                if (r.candidateUrl != null) scanCandidate(r.candidateUrl)
                else runOnUiThread {
                    setBusy(false)
                    if (oneTapPending && r.needsOfficialFlow) {
                        status.text = "O provedor exige uma autorização no fluxo oficial. Conclua-a uma vez; o envio ao PS4 continuará automaticamente."
                        openOfficialFlow()
                    } else if (oneTapPending) {
                        oneTapPending = false
                        status.text = "Não foi possível obter uma URL de PKG utilizável para envio direto."
                    }
                }
'''
if old not in s:
    raise SystemExit('resolver candidate anchor not found')
s = s.replace(old, new, 1)

old = '''                status.text = "✓ PKG válido. Autopilot pronto."
                renderPkg(info)
                if (autoPilotSwitch.isChecked) runAutopilot()
'''
new = '''                status.text = "✓ PKG válido. Pronto para enviar ao PS4."
                renderPkg(info)
                if (oneTapPending) {
                    oneTapPending = false
                    sendBgftPayload(info)
                } else if (autoPilotSwitch.isChecked) runAutopilot()
'''
if old not in s:
    raise SystemExit('scan success anchor not found')
s = s.replace(old, new, 1)

s = s.replace('status.text = "URL ainda não está pronta para o Receiver: ${e.message}"',
              'status.text = "URL ainda não está pronta para envio: ${e.message}"')
s = s.replace('append("Transporte: BGFT Receiver ou Payload Local\\n")',
              'append("Transporte padrão: Payload BGFT direto (Internet → PS4)\\n")')
s = s.replace('if (::autoPilotSwitch.isInitialized && autoPilotSwitch.isChecked) linkInput.post { analyzeInput() }',
              'linkInput.post { analyzeInput() }')

main.write_text(s)

g = gradle.read_text()
g = g.replace('versionCode = 20', 'versionCode = 30')
g = g.replace('versionName = "1.3.0"', 'versionName = "2.0.0"')
gradle.write_text(g)

assert 'TAYSON PS4 DOWNLOADER' in main.read_text()
assert 'ENVIAR AO PS4' in main.read_text()
assert 'BgftPayloadClient.send' in main.read_text()
assert 'versionName = "2.0.0"' in gradle.read_text()
