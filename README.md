# Akirabox para Android

[![Build Akirabox APK](https://github.com/itayson/Akirabox-Android/actions/workflows/build-apk.yml/badge.svg)](https://github.com/itayson/Akirabox-Android/actions/workflows/build-apk.yml)

Aplicativo nativo para instalar pacotes PKG e enviar payloads BIN/ELF diretamente a consoles PlayStation 4 e PlayStation 5 desbloqueados.

O app foi projetado para funcionar no celular sem computador, sem Termux e sem servidor intermediário. O download e a transmissão acontecem no próprio Android.

A interface da versão 1.2 adota cartões translúcidos, navegação em quatro etapas, fundo escuro e acentos lavanda inspirados na experiência visual do Duskaryon Web Direct Package Installer, preservando a identidade Akirabox.

## Compatibilidade

- Android 7.0 ou superior (`minSdk 24`).
- Processadores ARM de 32 bits (`armeabi-v7a`).
- Processadores ARM de 64 bits (`arm64-v8a`).
- APK universal compatível com as duas arquiteturas.
- PS4 Remote Package Installer: porta padrão `12800`.
- PS5 etaHEN DPI v2: porta padrão `12800`.
- PS4 GoldHEN BinLoader: porta padrão `9090`.
- PS5 etaHEN ELF Loader: porta padrão `9021`.

## Recursos

- Instalação direta de PKG por URL.
- Envio nativo de payload BIN/ELF por TCP, sem Akira Bridge.
- Conversão automática de links `github.com/.../blob/...` para o arquivo bruto.
- Recebimento de links pelo menu **Compartilhar** de navegadores e outros aplicativos.
- Botão para colar links e memória separada dos últimos links de PKG e payload.
- Validação completa do payload antes do envio, sem abrir a porta do BinLoader.
- Bloqueio de HTML, ZIP, RAR, 7z, executáveis Windows e ELF inválido antes de acessar o console.
- Transmissão em blocos de 8 KB para maior compatibilidade com o BinLoader.
- Limite de payload configurado em 64 MB.
- Cálculo SHA-256 após o download.
- Interface em português e histórico de atividade local.
- Diagnóstico copiável e ajuda integrada para facilitar o suporte.
- Nenhum cadastro, rastreador ou armazenamento em servidor.

## Como obter o APK pelo GitHub

1. Abra a aba **Actions** do repositório.
2. Escolha **Build Akirabox APK**.
3. Toque em **Run workflow**.
4. Quando terminar, baixe o artefato **Akirabox-Android-APKs**.
5. Escolha:
   - `Akirabox-Android7-armeabi-v7a.apk` para celulares ARM de 32 bits;
   - `Akirabox-Android7-arm64-v8a.apk` para ARM de 64 bits;
   - `Akirabox-Android7-universal.apk` quando não souber a arquitetura.

Os APKs de teste são assinados automaticamente pelo Android Gradle Plugin e podem ser instalados após permitir “instalar apps desconhecidos” para o navegador ou gerenciador de arquivos usado.

## Uso

### Instalar PKG

1. Conecte o Android e o console à mesma rede local.
2. Abra o Remote Package Installer ou ative o DPI.
3. Selecione **Instalar PKG**.
4. Informe o IP do console e um link direto para `.pkg`.
5. Teste o instalador e toque em **Instalar pacote**.

### Enviar payload

1. Ative o BinLoader no GoldHEN ou o ELF Loader no etaHEN.
2. Selecione **Enviar payload**.
3. Informe o IP, a URL direta do BIN/ELF e confira a porta.
4. Confirme que o arquivo é compatível com o firmware.
5. Toque em **Validar payload** para conferir o download sem acessar o console.
6. Confirme a origem do arquivo e toque em **Enviar**.

Não abra e feche a porta do BinLoader somente para testar: alguns receptores mostram `payload format error` quando recebem uma conexão vazia. O Akirabox valida o arquivo primeiro e abre a conexão apenas no envio real.

## Compilação local

Requisitos: JDK 17, Android SDK 35 e Gradle 8.9.

```bash
./scripts/test-core.sh
gradle :app:assembleUniversalDebug
```

Os APKs ficam em `app/build/outputs/apk/`.

## Estrutura

- `MainActivity.java`: interface e fluxo do aplicativo.
- `ConsoleClient.java`: API do instalador, download e transmissão TCP.
- `PayloadFormatValidator.java`: validação preventiva do arquivo.
- `.github/workflows/build-apk.yml`: compilação automática dos três APKs.
- `tools/CoreProtocolTest.java`: teste que verifica que os bytes recebidos são idênticos ao arquivo baixado.

## Uso responsável

Destinado a homebrews e backups pessoais legítimos em consoles do próprio usuário. Payloads incorretos ou incompatíveis podem travar ou reiniciar o console. O projeto não distribui jogos, chaves, exploits ou payloads protegidos.

## Licença

MIT. Consulte [LICENSE](LICENSE).
