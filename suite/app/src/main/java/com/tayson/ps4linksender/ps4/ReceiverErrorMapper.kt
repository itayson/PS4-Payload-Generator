package com.tayson.ps4linksender.ps4

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
