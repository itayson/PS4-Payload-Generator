from pathlib import Path

root = Path('.')
main = root / 'app/src/main/java/com/tayson/ps4linksender/MainActivity.kt'
gradle = root / 'app/build.gradle.kts'

# Version bump.
g = gradle.read_text()
g = g.replace('versionCode = 19', 'versionCode = 20')
g = g.replace('versionName = "1.2.2"', 'versionName = "1.3.0"')
gradle.write_text(g)

s = main.read_text()
s = s.replace(
    'import com.tayson.ps4linksender.ps4.PayloadLoaderClient\n',
    'import com.tayson.ps4linksender.ps4.PayloadLoaderClient\n'
    'import com.tayson.ps4linksender.ps4.AutoPilotEngine\n'
)
s = s.replace(
    '    private var pkgInfo: PkgInfo? = null\n',
    '    private var pkgInfo: PkgInfo? = null\n'
    '    private var autoPilotInFlight = false\n'
)
s = s.replace(
    '    private lateinit var monitorButton: Button\n',
    '    private lateinit var monitorButton: Button\n'
    '    private lateinit var autoPilotSwitch: Switch\n'
)
s = s.replace(
    '1.2.2 HYBRID • BGFT Receiver + Payload Installer 0.1.2 • Rootz • VikingFile • AkiraBox • Mocha • direto',
    '1.3.0 AUTOPILOT • Payload Direct 0.2.0 + fallback BGFT • Rootz • VikingFile • AkiraBox • Mocha • direto'
)

# Autonomous mode is enabled by default. The first explicit Analyze/Capture action
# is the user's authorization; from that point backend choice, resume and fallback
# are automatic.
old = '        root.addView(button("HISTÓRICO DE LINKS") { showHistory() })\n\n        progress = ProgressBar(this).apply { visibility = View.GONE; isIndeterminate = true }\n'
new = '''        root.addView(button("HISTÓRICO DE LINKS") { showHistory() })
        autoPilotSwitch = Switch(this).apply {
            text = "AUTOPILOT: iniciar download/instalação automaticamente após validar o PKG"
            setTextColor(Color.rgb(235, 239, 247))
            isChecked = true
            setPadding(0, dp(8), 0, dp(8))
        }
        root.addView(autoPilotSwitch)
        root.addView(button("INICIAR AUTOPILOT AGORA") { runAutopilot() })

        progress = ProgressBar(this).apply { visibility = View.GONE; isIndeterminate = true }
'''
if old not in s:
    raise SystemExit('history/progress anchor not found')
s = s.replace(old, new, 1)

s = s.replace(
    '                status.text = "✓ PKG válido. Escolha BGFT ou Payload Local."\n                renderPkg(info)\n',
    '                status.text = "✓ PKG válido. Autopilot pronto."\n'
    '                renderPkg(info)\n'
    '                if (autoPilotSwitch.isChecked) runAutopilot()\n'
)

# Make the old manual backends clearly secondary rather than removing the proven
# paths. This preserves recovery/debug access without requiring it in normal use.
s = s.replace(
    '        root.addView(label("PS4 — Tayson Receiver :${ReceiverClient.PORT}"))',
    '        root.addView(label("Controles avançados / fallback"))\n'
    '        root.addView(text("Normalmente você não precisa usar os botões abaixo: o Autopilot detecta, carrega e escolhe o backend sozinho.", 12f, false))\n'
    '        root.addView(label("PS4 — Tayson Receiver :${ReceiverClient.PORT}"))'
)

anchor = '    private fun openOfficialFlow() {\n'
if anchor not in s:
    raise SystemExit('openOfficialFlow anchor not found')
methods = r'''    private fun runAutopilot() {
        if (autoPilotInFlight) return
        val current = pkgInfo ?: run { toast("Analise ou capture um PKG válido primeiro"); return }
        autoPilotInFlight = true
        savePrefs()
        setBusy(true, "Autopilot: revalidando o PKG e preparando o PS4…")
        executor.execute {
            try {
                val fresh = revalidateForAutopilot(current)
                pkgInfo = fresh
                val result = AutoPilotEngine.run(this, ipInput.text.toString().trim(), fresh) { message ->
                    runOnUiThread { status.text = "AUTOPILOT • $message" }
                }
                runOnUiThread {
                    autoPilotInFlight = false
                    setBusy(false)
                    ipInput.setText(result.host)
                    savePrefs()
                    renderPkg(fresh)
                    when (result.backend) {
                        AutoPilotEngine.Backend.PAYLOAD_DIRECT -> {
                            status.text = "✓ AUTOPILOT ativo: download direto no PS4; o celular não é relay."
                            payloadStatus.text = buildString {
                                append("✓ Payload ${result.payloadVersion ?: "0.2.x"} em modo direto.\n")
                                append(result.message)
                                append("\nO Payload fará retry, Range/resume, cache local e instalação automaticamente.")
                            }
                            refreshPayloadStatus()
                        }
                        AutoPilotEngine.Backend.PAYLOAD_ALREADY_RUNNING -> {
                            status.text = "✓ AUTOPILOT detectou o mesmo trabalho já em execução; não duplicou o download."
                            payloadStatus.text = result.message
                            refreshPayloadStatus()
                        }
                        AutoPilotEngine.Backend.BGFT_RECEIVER -> {
                            status.text = buildString {
                                append("✓ AUTOPILOT usou fallback BGFT")
                                result.receiverJobId?.let { append(" • job $it") }
                                result.bgftTaskId?.let { append(" • BGFT $it") }
                                append(". O PS4 continuará sozinho.")
                            }
                            startMonitoring()
                        }
                    }
                }
            } catch (e: Exception) {
                runOnUiThread {
                    autoPilotInFlight = false
                    setBusy(false)
                    status.text = "Autopilot interrompido: ${e.message}"
                }
            }
        }
    }

    private fun revalidateForAutopilot(current: PkgInfo): PkgInfo {
        return try {
            PkgScanner.scan(current.finalUrl, current.requestContext)
        } catch (first: Exception) {
            val original = currentOriginalUrl
            if (original.isNotBlank() && original != current.finalUrl) {
                val resolved = resolvers.resolve(original)
                resolved.candidateUrl?.let { candidate ->
                    currentProvider = resolved.provider
                    return PkgScanner.scan(candidate)
                }
            }
            throw IllegalStateException(
                "A URL temporária expirou ou exige nova autorização. Abra o fluxo oficial novamente. (${first.message})"
            )
        }
    }

'''
s = s.replace(anchor, methods + anchor, 1)

# Persist the autonomy toggle; default ON.
s = s.replace(
    '        linkInput.setText(p.getString("last_link", ""))\n',
    '        linkInput.setText(p.getString("last_link", ""))\n'
    '        if (::autoPilotSwitch.isInitialized) autoPilotSwitch.isChecked = p.getBoolean("autopilot_enabled", true)\n'
)
s = s.replace(
    '            .putString("last_link", linkInput.text.toString().trim())\n            .apply()\n',
    '            .putString("last_link", linkInput.text.toString().trim())\n'
    '            .putBoolean("autopilot_enabled", !::autoPilotSwitch.isInitialized || autoPilotSwitch.isChecked)\n'
    '            .apply()\n'
)

# A shared link becomes zero-touch: sharing to the app is the explicit initial
# action, so when Autopilot is enabled we may start analysis automatically.
s = s.replace(
    '                currentOriginalUrl = url.trim()\n                linkInput.setText(url.trim())\n',
    '                currentOriginalUrl = url.trim()\n'
    '                linkInput.setText(url.trim())\n'
    '                if (::autoPilotSwitch.isInitialized && autoPilotSwitch.isChecked) linkInput.post { analyzeInput() }\n'
)

main.write_text(s)

assert 'versionCode = 20' in gradle.read_text()
assert 'versionName = "1.3.0"' in gradle.read_text()
assert 'AutoPilotEngine.run' in main.read_text()
assert 'AUTOPILOT: iniciar download/instalação automaticamente' in main.read_text()
assert 'revalidateForAutopilot' in main.read_text()
