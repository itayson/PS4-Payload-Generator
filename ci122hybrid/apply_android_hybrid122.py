from pathlib import Path

root = Path('.')
main = root / 'app/src/main/java/com/tayson/ps4linksender/MainActivity.kt'
gradle = root / 'app/build.gradle.kts'
loader = root / 'app/src/main/java/com/tayson/ps4linksender/ps4/PayloadLoaderClient.kt'

# Version bump from 1.2.1.
g = gradle.read_text()
g = g.replace('versionCode = 18', 'versionCode = 19')
g = g.replace('versionName = "1.2.1"', 'versionName = "1.2.2"')
gradle.write_text(g)

s = main.read_text()
s = s.replace('1.2.1 HYBRID • BGFT Receiver + Payload Installer 0.1.1',
              '1.2.2 HYBRID • BGFT Receiver + Payload Installer 0.1.2')
s = s.replace('Payload 0.1.1 não testado.', 'Payload 0.1.2 não testado.')
s = s.replace('O Payload 0.1.1 retoma automaticamente transferências interrompidas usando Range.',
              'O Payload 0.1.2 retoma automaticamente transferências interrompidas usando Range e pode reaproveitar um current.pkg parcial existente.')

# Add explicit resume-existing-cache button; it deliberately does not RESET current.pkg.
old = '        root.addView(payloadSendButton)\n        root.addView(button("ATUALIZAR STATUS DO PAYLOAD") { refreshPayloadStatus() })\n'
new = ('        root.addView(payloadSendButton)\n'
       '        root.addView(button("RETOMAR CACHE EXISTENTE (SEM APAGAR)") { resumeCurrentPayload() })\n'
       '        root.addView(button("ATUALIZAR STATUS DO PAYLOAD") { refreshPayloadStatus() })\n')
if old not in s:
    raise SystemExit('payload button anchor not found')
s = s.replace(old, new, 1)

# Before loading a new payload through GoldHEN, gracefully stop an old Tayson payload
# if it is sitting in idle/error state. EXIT does not delete current.pkg.
old = '                val sent = PayloadLoaderClient.sendEmbedded(this, host)\n'
new = ('                runCatching { PayloadClient.exit(host) }\n'
       '                Thread.sleep(350)\n'
       '                val sent = PayloadLoaderClient.sendEmbedded(this, host)\n')
if old not in s:
    raise SystemExit('loadEmbeddedPayload anchor not found')
s = s.replace(old, new, 1)

anchor = '    private fun refreshPayloadStatus() {\n'
if anchor not in s:
    raise SystemExit('refreshPayloadStatus anchor not found')
resume_method = r'''    private fun resumeCurrentPayload() {
        val current = pkgInfo ?: run { toast("Analise novamente o mesmo PKG antes de retomar"); return }
        val host = ipInput.text.toString().trim()
        if (host.isBlank()) { toast("Informe ou localize o PS4"); return }
        savePrefs()
        setBusy(true, "Preparando Relay para retomar o cache existente…")
        executor.execute {
            try {
                val probe = PayloadClient.probe(host)
                if (probe.version != "0.1.2") {
                    throw IllegalStateException("Para preservar o cache parcial, carregue primeiro o Payload 0.1.2. Detectado: ${probe.version}")
                }
                val fresh = PkgScanner.scan(current.finalUrl, current.requestContext)
                pkgInfo = fresh
                val prepared = RelayManager.prepare(this, fresh)
                val result = PayloadClient.resume(host, prepared.relayUrl, fresh)
                runOnUiThread {
                    setBusy(false)
                    renderPkg(fresh)
                    payloadStatus.text = buildString {
                        append("✓ Cache existente preservado e retomada aceita.\n")
                        append("O Payload continuará a partir do tamanho atual de /data/tayson/payload-cache/current.pkg.\n")
                        append(result.raw)
                    }
                    status.text = "✓ Retomando transferência Payload sem apagar o cache parcial."
                }
            } catch (e: Exception) {
                runOnUiThread {
                    setBusy(false)
                    payloadStatus.text = "Falha ao retomar cache: ${e.message}"
                }
            }
        }
    }

'''
s = s.replace(anchor, resume_method + anchor, 1)
main.write_text(s)

# Embed/load Payload Installer 0.1.2.
l = loader.read_text()
l = l.replace('tayson_payload_installer_0_1_1.bin', 'tayson_payload_installer_0_1_2.bin')
loader.write_text(l)

assert 'versionCode = 19' in gradle.read_text()
assert 'versionName = "1.2.2"' in gradle.read_text()
assert 'RETOMAR CACHE EXISTENTE' in main.read_text()
assert 'PayloadClient.resume' in main.read_text()
assert 'PayloadClient.exit' in main.read_text()
assert 'tayson_payload_installer_0_1_2.bin' in loader.read_text()
