# TaySon Proxy Lab v0.1 — APK experimental (DEBUG)

Aplicativo independente do PS4 Payload Generator. Não altera o projeto original.

- Arquivo: `TaySon-Proxy-Lab-v0.1-debug.apk`
- Package: `com.tayson.proxylab`
- Version: `0.1-experimental` (`versionCode=1`)
- Android minSdk: 26
- Assinatura: debug v2 (validada com apksigner)
- SHA-256: `cc1ac526bda904be168ba9b3073d9c1ebbe7894754143ef5f73a57194de9acc5`
- Build: Gradle 8.9 + Android Gradle Plugin 8.7.3 + Android SDK 35, concluído com sucesso.

O app resolve URLs HTTP(S) públicas, consulta assinatura inicial PKG e HTTP Range, e disponibiliza proxy local na porta TCP 8765. O aplicativo precisa permanecer aberto com Wi-Fi e console na mesma rede.

**NÃO TESTADO EM HARDWARE:** transporte Android real, compatibilidade do provedor específico, BGFT, download e instalação no PS4. O APK não envia payload/ELF e não cria tarefas BGFT. Requer ferramenta de envio ao PS4 já funcional para teste.

Este APK é assinado com chave de depuração gerada no ambiente experimental. Para instalar atualizações futuras com assinatura diferente poderá ser necessário remover esta versão de laboratório. O PS4 Payload Generator principal utiliza outro identificador de pacote e não é afetado.

Nota: a primeira execução do GitHub Actions no repositório falhou antes de executar seus passos (runner_id 0). Este arquivo foi compilado em ambiente remoto com Gradle, e sua assinatura foi verificada separadamente; não representa uma execução do GitHub Actions aprovada.
