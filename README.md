# OrbisDrop

[![Build APK](https://github.com/itayson/Akirabox-Android/actions/workflows/build-apk.yml/badge.svg)](https://github.com/itayson/Akirabox-Android/actions/workflows/build-apk.yml)

Aplicativo Android focado exclusivamente no PlayStation 4 desbloqueado.

O OrbisDrop recebe um link HTTP/HTTPS ou um arquivo BIN/ELF local e transmite os bytes ao GoldHEN BinLoader pela porta TCP `9090`.

## Recursos

- suporte exclusivo ao PS4;
- porta fixa `9090`;
- links diretos de VikingFile, Rootz e Akirabox `/l`;
- compatibilidade com GitHub Raw e redirecionamentos HTTP;
- streaming direto de payloads por URL, sem download completo para o cache antes do envio;
- verificação rápida dos primeiros bytes para bloquear HTML, arquivos compactados e ELF inválido;
- seleção de payload local pelo Android;
- sem limite artificial de tamanho de payload;
- cálculo SHA-256 durante a transmissão;
- compartilhamento de links por outros aplicativos;
- interface e ícone próprios do OrbisDrop.

No envio por link, o aplicativo começa a transmitir ao PS4 após verificar apenas o cabeçalho inicial. Assim, um arquivo grande não precisa ser baixado integralmente e depois enviado novamente.

O limite prático depende da velocidade do servidor, da estabilidade da rede e do que o GoldHEN BinLoader consegue receber.

## Removido

- PlayStation 5 e etaHEN;
- Remote Package Installer e DPI;
- instalação de PKG;
- porta `12800`;
- seleção manual de porta.

## Uso

1. Conecte o celular e o PS4 à mesma rede local.
2. No GoldHEN, ative **Settings → Enable BinLoader Server**.
3. Informe o IP local do PS4.
4. Cole um link direto ou selecione um payload BIN/ELF.
5. Confirme a origem do payload.
6. Envie pela porta 9090.

O botão de validação separada ainda pode ler o arquivo completo quando o usuário solicitar essa verificação. O envio normal por URL usa streaming direto.

## Compatibilidade Android

- Android 7.0 ou superior (`minSdk 24`).
- ARM de 32 bits (`armeabi-v7a`).
- ARM de 64 bits (`arm64-v8a`).
- APK universal.

## Compilação local

Requisitos: JDK 17, Android SDK 35 e Gradle 8.9.

```bash
./scripts/test-core.sh
gradle :app:assembleUniversalDebug
```

Consulte `docs/SUPPORTED_LINKS.md` para detalhes dos links aceitos.

## Uso responsável

Destinado a homebrews e ferramentas legítimas em consoles do próprio usuário. Payloads incompatíveis podem travar ou reiniciar o console. O projeto não distribui jogos, chaves ou conteúdo protegido.

## Licença

MIT. Consulte [LICENSE](LICENSE).
