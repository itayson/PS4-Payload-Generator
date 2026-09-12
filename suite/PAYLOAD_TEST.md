# Tayson PS4 Suite 1.5.0 — Payload experimental

Esta é a primeira etapa do novo modo payload: confirmar execução em processo separado e testar a leitura de 16 bytes pelo PS4. A instalação de PKGs continua pelo modo Receiver.

## Instalação e teste

1. Instale **Tayson-PS4-Payload-Lab-1.5.0.apk** no Android. Ele aparece como **Tayson PS4 Payload Lab**, ao lado do aplicativo atual. Não desinstale o anterior: o Lab usa dados próprios e não importa automaticamente seu histórico ou fila.
2. No PS4 já desbloqueado, habilite o BinLoader do GoldHEN. Use celular e PS4 na mesma rede.
3. No Lab, informe o IP do PS4 e selecione **Payload experimental**. As portas padrão são 9090 para o BinLoader, 9021 para o carregador ELF e 9096 para o diagnóstico Tayson. Se seu carregador usa outras portas, ajuste os dois campos.
4. Toque em **1. CARREGAR PAYLOAD**. O APK inclui o bootstrap BIN e o ELF de diagnóstico, com hashes conferidos antes do envio. Se o carregador ELF já estiver ativo, ele é reutilizado.
5. Aguarde **Execução confirmada**. Transmitir o arquivo sem receber essa confirmação não conta como execução bem-sucedida.
6. Analise um link pelo mesmo fluxo que já usamos. Se necessário, abra o fluxo oficial do provedor para atualizar cookies e a URL temporária.
7. Toque em **3. TESTAR LINK NO PAYLOAD (16 BYTES)**. Envie o texto de **COPIAR DIAGNÓSTICO** ou uma captura da área de diagnóstico.

## Como interpretar

- `http=206 bytes=16 pkg=true`: o processo leu o cabeçalho do PKG na resposta parcial. Isso ainda não comprova download integral, retomada, velocidade ou instalação.
- `http=200`: houve leitura, mas o servidor não confirmou o Range solicitado.
- `pkg=false`: a resposta não começa com o cabeçalho de um PKG; pode ser HTML ou outro conteúdo.
- HTTP 301/302/307/308: recapture a URL final pelo Android. O diagnóstico não encaminha credenciais automaticamente para outro endereço.
- HTTP 401/403: atualize a sessão e a URL pelo fluxo oficial.
- Erro em `ssl2-module`, `http2-module`, `net-init`, `ssl2-init` ou `http2-init`: identifica o ponto de inicialização que precisa ser corrigido.
- Erro em `request-send`: pode incluir falha de conexão/TLS. O diagnóstico conserva a validação de certificado do sistema; não muda a configuração do Receiver.
- Sem resposta: o aplicativo mostra a última etapa recebida. **TESTAR PAYLOAD** consulta o processo mesmo enquanto um teste está ocupado. Se ele permanecer preso, reinicie o PS4 antes de uma nova carga.

O diagnóstico usa um teste por vez, três pools de 1 MiB e uma thread de 1 MiB. Bibliotecas e o carregador têm consumo adicional que não foi medido em hardware. O teste não grava um PKG completo, não registra tarefas BGFT e não altera os jogos instalados.

## Projeto preservado

O Android mantém o modo Receiver, a leitura de metadados, os provedores e os formatos de fila/histórico. O motor e a configuração do Receiver 1.4.2 foram preservados. O erro de atualização `0x80990117` permanece uma investigação separada.

O Lab tem identificador Android separado porque a chave privada usada nos APKs antigos não estava nos arquivos recuperados. Assim, testar esta versão não exige remover o app instalado. Não há promessa de atualização direta sobre o APK anterior com a mesma assinatura.

## Dependências e reprodução

- ps4-payload-sdk v0.9, SHA-256 do ZIP: `3190e88255a72161e5ce2f22a8ae8f638a2c5a9d1ff1e0209ed692d36b4d9ec5`.
- ps4-payload-dev/elfldr, commit `b3ee49746cb9f3464e8dc75ec6387e8408558b8a`.
- Payload desenvolvido para o ambiente desse carregador. Não renomeie o ELF para BIN nem envie o ELF Tayson diretamente ao BinLoader legado.
- Android: JDK 17, Gradle 8.9, SDK 35, AGP 8.7.3, Kotlin 2.0.21.
- Build do payload: `make -C ps4payload PS4_PAYLOAD_SDK=/caminho/ps4-payload-sdk`.
- Assets: `python3 tools/package_payload.py /caminho/elfldr/elfldr.bin`.
- Testes nativos: `python3 tools/test_payload_protocol.py`.
- Android: `gradle :app:testUniversalDebugUnitTest :app:lintUniversalDebug :app:assembleUniversalDebug`.

Referências: [SDK](https://github.com/ps4-payload-dev/sdk), [carregador](https://github.com/ps4-payload-dev/elfldr), [cliente HTTP2 de referência](https://github.com/ps4-payload-dev/elfldr/blob/b3ee49746cb9f3464e8dc75ec6387e8408558b8a/uri.c).

As dependências do payload usam GPLv3+ e mantêm seus avisos e código-fonte. O bootstrap distribuído é compilado sem alterações a partir do commit acima. A compilação e os testes simulados não comprovam compatibilidade com seu firmware/GoldHEN; o diagnóstico no console é a próxima evidência necessária.
