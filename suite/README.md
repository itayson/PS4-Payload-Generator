# Atualização 1.5.0 — Payload Lab

Modo experimental de diagnóstico adicionado sobre o Android 1.1.0 e Receiver 1.4.2.
Veja [PAYLOAD_TEST.md](PAYLOAD_TEST.md) para uso, limites e reprodução.
A documentação abaixo é o registro histórico da arquitetura anterior.

# Tayson PS4 Link Sender + PS4 Receiver 1.0

A versão 1.0 muda completamente a arquitetura do projeto. O Android não injeta mais um ELF por download e não retransmite o PKG. Em vez disso, um homebrew **Tayson PS4 Receiver** é instalado uma vez no PS4 e recebe do Android somente a URL autorizada, headers/cookies necessários e os metadados do PKG.

## Arquitetura

```text
Rootz / VikingFile / AkiraBox / Mocha / URL direta
                    │ HTTPS
                    ▼
             Tayson PS4 Receiver
              (processo homebrew)
                    │
          proxy 127.0.0.1:32112
                    │
                    ▼
                  BGFT
                    │
                    ▼
             Downloads do PS4

Android ── TCP 9095 ──► Receiver
(URL + contexto HTTP + metadados; sem bytes do PKG)
```

O Receiver usa o stack normal de rede do OpenOrbis (`SceNet`, `SceSsl`, `SceHttp`), faz um preflight `Range: bytes=0-0` diretamente do console para o CDN e só então registra uma URL localhost no BGFT. O proxy local repassa `Range`, `Cookie`, `Referer`, `User-Agent` e headers seguros. O conteúdo é transmitido em memória em blocos de 128 KiB; não há cópia completa do PKG no telefone nem arquivo intermediário no HDD.

## Uso

1. Instale `Tayson-PS4-Receiver-1.0.0.pkg` uma vez pelo instalador de packages do GoldHEN.
2. Abra **Tayson PS4 Receiver** no console. Ele escuta TCP `9095` na LAN e mantém o proxy do BGFT apenas em `127.0.0.1:32112`.
3. No Android, abra **Tayson PS4 Link Sender 1.0.0**, analise/capture o link e toque em **LOCALIZAR RECEIVER AUTOMATICAMENTE** ou informe o IPv4 do PS4.
4. Toque em **ENVIAR PKG AO PS4 RECEIVER**. O Android envia apenas o job; o download é Internet → PS4.

## Protocolo de controle

O Android e o Receiver usam `TAYSON/1` por TCP 9095. `PING` confirma que a porta pertence ao Receiver. `JOB` envia um pacote binário com URL final, tamanho, Content ID, nome, tipo do PKG e contexto HTTP. O Receiver rejeita clientes fora de redes privadas IPv4.

## Provedores

Os resolvers do Android continuam reconhecendo Rootz, VikingFile, AkiraBox, Mocha e URLs diretas. Quando o provedor exige fluxo oficial, o WebView só captura a URL/headers que o próprio fluxo autorizado forneceu. O projeto não remove CAPTCHA, anúncios, login, premium ou outras proteções do provedor.

## Build Android

Requer JDK 17, Android SDK 35 e Gradle 8.9. Para o APK universal:

```bash
gradle --no-daemon :app:lintUniversalDebug :app:assembleUniversalDebug
```

## Build PS4 Receiver

Requer OpenOrbis PS4 Toolchain. Exporte `OO_PS4_TOOLCHAIN` e execute:

```bash
make -C ps4receiver clean all
```

O resultado é um PKG instalável com TITLE_ID `TYRC00001`.

## Estado

A estrutura e o protocolo 1.0 possuem validação estática/local. O resultado só deve ser considerado validado em hardware após instalar o Receiver PKG em um PS4 com GoldHEN e testar os provedores reais.
