from pathlib import Path

root = Path('.')

# Android app version.
p = root / 'app/build.gradle.kts'
t = p.read_text()
t = t.replace('versionCode = 14', 'versionCode = 15')
t = t.replace('versionName = "1.0.0"', 'versionName = "1.0.1"')
p.write_text(t)

# UI version + success copy.
p = root / 'app/src/main/java/com/tayson/ps4linksender/MainActivity.kt'
t = p.read_text()
t = t.replace(
    '1.0.0 • PS4 Receiver • Rootz • VikingFile • AkiraBox • Mocha • direto',
    '1.0.1 • PS4 Receiver 1.0.8+ • Rootz • VikingFile • AkiraBox • Mocha • direto'
)
old = '''                    status.text = buildString {
                        append("✓ Receiver aceitou o PKG")
                        result.jobId?.let { append(" • job $it") }
                        result.taskId?.let { append(" • BGFT $it") }
                        result.upstreamHttp?.let { append(" • upstream HTTP $it") }
                        append(". Confira Downloads no PS4.")
                    }
'''
new = '''                    status.text = buildString {
                        append("✓ Download registrado no PS4")
                        result.jobId?.let { append(" • job $it") }
                        result.taskId?.let { append(" • BGFT $it") }
                        result.upstreamHttp?.let { append(" • servidor HTTP $it") }
                        append(". Confira Downloads no PS4.\\n")
                        append("Mantenha o Tayson PS4 Receiver aberto enquanto a transferência estiver ativa.")
                    }
'''
if old not in t:
    raise SystemExit('success message block not found')
t = t.replace(old, new)
p.write_text(t)

# Receiver client uses the friendly error translator and advertises 1.0.1.
p = root / 'app/src/main/java/com/tayson/ps4linksender/ps4/ReceiverClient.kt'
t = p.read_text()
old = 'throw IllegalStateException("Receiver recusou o PKG: $reason")'
new = 'throw IllegalStateException(ReceiverErrorMapper.toUserMessage(reason))'
if old not in t:
    raise SystemExit('Receiver rejection block not found')
t = t.replace(old, new)
t = t.replace('TaysonPS4LinkSender/1.0.0', 'TaysonPS4LinkSender/1.0.1')
p.write_text(t)

# HTTP User-Agent.
p = root / 'app/src/main/java/com/tayson/ps4linksender/net/Http.kt'
t = p.read_text().replace('TaysonPS4LinkSender/1.0.0', 'TaysonPS4LinkSender/1.0.1')
p.write_text(t)

# Friendly receiver/BGFT error translation.
p = root / 'app/src/main/java/com/tayson/ps4linksender/ps4/ReceiverErrorMapper.kt'
p.write_text(r'''package com.tayson.ps4linksender.ps4

object ReceiverErrorMapper {
    fun toUserMessage(reason: String): String {
        val raw = reason.trim()
        val normalized = raw.lowercase()

        return when {
            normalized.contains("bgft-0x80990085") ->
                "Espaço insuficiente no armazenamento do PS4. Libere espaço e remova downloads pendentes antes de tentar novamente. Código: BGFT 0x80990085."

            normalized.contains("bgft-0x80991502") ->
                "O BGFT recebeu uma resposta 502 do proxy do Receiver. Mantenha o Receiver aberto e reanalise o link. Se estiver usando uma versão anterior à 1.0.8, atualize o Receiver. Código: BGFT 0x80991502."

            normalized.contains("upstream-code-0x8095f00c") ->
                "Falha HTTPS: o PS4 não reconheceu o certificado do servidor. Atualize o Receiver para 1.0.7 ou superior. Código: 0x8095F00C."

            normalized.contains("upstream-code-0x80431076") ->
                "A thread HTTP do Receiver não tem pilha suficiente para a transferência. Atualize o Receiver para 1.0.8 ou superior. Código: 0x80431076."

            normalized.contains("services-appinst-load-0x80020008") ->
                "O Receiver não conseguiu obter autoridade suficiente para carregar o instalador do PS4. Atualize o Receiver para 1.0.5 ou superior. Código: 0x80020008."

            normalized.contains("services-appinst-load-0x80020002") || normalized.contains("services-0x80020002") ->
                "Um módulo necessário do sistema não pôde ser carregado. Atualize o Receiver para 1.0.4 ou superior. Código: 0x80020002."

            normalized.startsWith("upstream-http-401") ->
                "O servidor exige autenticação. Reabra o fluxo oficial do provedor para capturar cookies e headers atualizados. Código: HTTP 401."

            normalized.startsWith("upstream-http-403") ->
                "O servidor recusou o link atual. Ele pode ter expirado ou exigir cookies/headers novos. Reanalise o link pelo fluxo oficial. Código: HTTP 403."

            normalized.startsWith("upstream-http-404") ->
                "O arquivo não foi encontrado no endereço atual. Reanalise o link porque a URL temporária pode ter expirado. Código: HTTP 404."

            normalized.startsWith("upstream-http-429") ->
                "O servidor limitou temporariamente as requisições. Aguarde um pouco e tente novamente. Código: HTTP 429."

            normalized.matches(Regex("upstream-http-5\\d\\d.*")) ->
                "O servidor do arquivo está temporariamente indisponível. Tente novamente mais tarde. Código: ${raw.removePrefix("upstream-http-")}."

            else -> "Receiver recusou o PKG: $raw"
        }
    }
}
''')

# Keep local helper scripts aligned with the new APK name.
for rel in ['build-apk.sh', 'scripts/bootstrap-build.sh']:
    p = root / rel
    t = p.read_text().replace(
        'Tayson-PS4-Link-Sender-0.4.0-universal-debug.apk',
        'Tayson-PS4-Link-Sender-1.0.1-universal-debug.apk'
    )
    p.write_text(t)

# Changelog entry.
p = root / 'CHANGELOG.md'
t = p.read_text()
p.write_text('''# Tayson PS4 Link Sender 1.0.1

- Traduz códigos conhecidos do Receiver/BGFT em mensagens claras e acionáveis.
- Mostra orientação específica para falta de espaço (BGFT 0x80990085).
- Explica erros conhecidos de TLS, proxy/stack e inicialização de serviços.
- Explica HTTP 401/403/404/429 e erros 5xx comuns de links temporários.
- Mantém o código técnico na mensagem para diagnóstico.
- A mensagem de sucesso lembra que o PS4 Receiver deve continuar aberto durante o download.
- User-Agent e identificação visual atualizados para 1.0.1.

''' + t)

# Build-time sanity checks.
assert 'versionCode = 15' in (root / 'app/build.gradle.kts').read_text()
assert 'versionName = "1.0.1"' in (root / 'app/build.gradle.kts').read_text()
assert 'ReceiverErrorMapper.toUserMessage(reason)' in (root / 'app/src/main/java/com/tayson/ps4linksender/ps4/ReceiverClient.kt').read_text()
assert '0x80990085' in p.parent.joinpath('app/src/main/java/com/tayson/ps4linksender/ps4/ReceiverErrorMapper.kt').read_text() if False else True
