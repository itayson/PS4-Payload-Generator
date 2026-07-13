# Akirabox para Android

[![Build Akirabox APK](https://github.com/itayson/Akirabox-Android/actions/workflows/build-apk.yml/badge.svg)](https://github.com/itayson/Akirabox-Android/actions/workflows/build-apk.yml)

Aplicativo nativo para instalar pacotes PKG e enviar payloads BIN/ELF diretamente a consoles PlayStation 4 e PlayStation 5 desbloqueados.

O app foi projetado para funcionar no celular sem computador, sem Termux e sem servidor intermediário. O download e a transmissão acontecem no próprio Android.

A interface adota cartões translúcidos, navegação em quatro etapas, fundo escuro e acentos lavanda inspirados na experiência visual do Duskaryon Web Direct Package Installer, preservando a identidade Akirabox.

A versão 1.3 adiciona um catálogo de fontes oficiais, payloads pré-configurados e importação de arquivos locais. O catálogo do GoldHEN aponta para os itens verificados da loja oficial do SiSTR0 até `v2.4b18.10`; compras e downloads continuam acontecendo no próprio Ko-fi.

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
- Catálogo GoldHEN com 21 versões verificadas no Ko-fi e atalho para a loja oficial completa.
- Payloads pré-configurados: NP Fake Signin 1.1 e FTP Server 0.20 para PS4/PS5.
- PS4Debug 1.1.19 oficial pré-configurado.
- Importação de BIN/ELF pelo seletor de arquivos do Android ou pelo menu **Compartilhar**.
- Permissão de arquivo persistente do Android, sem solicitar acesso geral ao armazenamento.
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

Cada versão concluída também fica disponível na seção **Releases**, com APKs ARMv7, ARM64, universal e o arquivo `SHA256SUMS.txt`. Quando uma nova versão é mesclada no branch `main`, o workflow lê o `versionName`, cria a tag correspondente e publica ou atualiza automaticamente a Release.

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
3. Informe o IP e escolha uma fonte: URL direta, catálogo ou BIN/ELF do celular.
4. Confirme que o arquivo é compatível com o firmware.
5. Toque em **Validar payload** para conferir o download sem acessar o console.
6. Confirme a origem do arquivo e toque em **Enviar**.

Não abra e feche a porta do BinLoader somente para testar: alguns receptores mostram `payload format error` quando recebem uma conexão vazia. O Akirabox valida o arquivo primeiro e abre a conexão apenas no envio real.

### GoldHEN e catálogo oficial

1. Em **Enviar payload**, toque em **Catálogo oficial**.
2. Abra **GoldHEN oficial** e escolha uma versão.
3. O Akirabox abre o item correspondente na loja oficial do SiSTR0.
4. Depois de baixar o arquivo pelo Ko-fi, volte ao app e use **Arquivo do celular**.

O Akirabox não contorna login, verificação humana, assinatura ou pagamento do Ko-fi. GoldHEN depende do exploit, do firmware e do `stage2` compatíveis; o próprio GoldHEN não deve ser enviado como um payload comum para o BinLoader de uma sessão GoldHEN já ativa.

### Payloads prontos

- **NP Fake Signin 1.1:** URLs oficiais para PS4 e PS5. Exige ativação offline prévia com Account ID diferente de zero e reinicialização após a execução.
- **FTP Server 0.20:** URLs oficiais para PS4 e PS5. Use apenas na rede local do seu próprio console.
- **PS4Debug 1.1.19:** `ps4debug.bin` oficial pré-configurado para os firmwares indicados pelo projeto.

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
