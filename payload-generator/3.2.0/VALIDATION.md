# Validação 3.4.3 — Tayson PS4 Payload Generator

## Evidência em hardware já preservada

Os cinco provedores principais já tiveram o fluxo funcional validado em PS4 real:

- **Mocha:** query autenticada preservada e `#tayson.pkg` usado somente para compatibilidade BGFT;
- **VikingFile:** R2 direto retornou HTTP 403 no BGFT; Worker v8 normalizou o transporte;
- **FileKeeper:** fluxo direto validado quando o endpoint final suporta Range/BGFT;
- **Rootz:** final R2/SigV4 validado via Universal Transport Worker;
- **AkiraBox:** Worker v8 + KV `ROUTES` permitiu download real após o acesso direto ao BGFT ser recusado.

A 3.4.3 mantém o núcleo BGFT/ELF validado e altera somente o roteamento por provedor.

## Mudanças específicas da 3.4.0

### Botão Colar

A Home adiciona um botão **Colar** ao lado da entrada de URL. A implementação aciona o comando nativo de colagem do campo Android, evitando dependência de APIs de clipboard removidas pelo linker da aplicação-base.

### Artwork do PKG

A camada de metadados agora:

1. mantém a leitura inicial limitada do PKG;
2. procura imagens PNG/JPEG já presentes no prefixo;
3. interpreta a tabela big-endian de entradas do PKG;
4. prioriza `icon0.png`, `pic0.png` e `pic1.png`;
5. pode solicitar pequenas faixas HTTP adicionais, limitadas a 2 MiB por imagem;
6. trata falhas de artwork como opcionais, sem transformar um PKG válido em erro de validação.

As imagens ficam somente em memória durante a sessão e não são gravadas no histórico.

### Novo ícone Android

O build substitui os 20 recursos de launcher da aplicação-base usando a arte oficial fornecida para o projeto:

- `icone.png`;
- `icone_round.png`;
- `icone_foreground.png`;
- `icone_background.png`;
- densidades mdpi, hdpi, xhdpi, xxhdpi e xxxhdpi.

O adaptive icon continua usando os XMLs e resource IDs existentes; apenas os bitmaps são substituídos, evitando alterar a tabela de recursos compilada.

### Identidade Android própria

A etapa final do build altera a identidade pública do APK de `com.phoenixx.ps4remotepkg` para `com.tayson.ps4payloadgenerator`. Também são atualizadas a permissão dinâmica e a authority `androidx-startup`. A classe `com.phoenixx.ps4remotepkg.mono.MonoRuntimeProvider.__mono_init__` é preservada propositalmente porque o runtime MAUI/Xamarin compilado em `classes2.dex` ainda usa esse namespace.

A troca do package ID significa que o Android considera a 3.4.0 Tayson um aplicativo diferente da instalação antiga; dados locais não migram automaticamente entre os dois IDs.

## Invariantes funcionais

Permanecem:

- validação do PKG usando a URL real do provedor;
- segunda verificação antes da geração do ELF;
- preservação de queries autenticadas quando necessário;
- detecção de Cloudflare R2/AWS SigV4;
- `BgftRoutePlanner` separado da URL original;
- Universal Transport Worker v11.2 + KV `ROUTES`;
- `PayloadPatcher` e template ELF existentes;
- envio ao GoldHEN BinLoader TCP 9090;
- nenhum Receiver/RPI.

## Identidade da release

```text
application label: Tayson PS4 Payload Generator
versionName: 3.4.3
versionCode: 40
package: com.tayson.ps4payloadgenerator
```

## CI 3.4.3

O pipeline exige:

- **103 checks** aprovados;
- testes adicionais para leitura da tabela de artwork e ordem `icon0 → pic0 → pic1`;
- dependency audit do runtime empacotado;
- ILRepack/patch preservando MVID/JNI TypeDef tokens críticos;
- APK alinhado e assinado;
- integridade ZIP aprovada;
- `classes.dex` e `classes2.dex` preservados;
- template ELF preservado;
- substituição verificada dos 20 recursos de ícone;
- `aapt` confirma o package `com.tayson.ps4payloadgenerator`;
- classe MonoRuntimeProvider herdada preservada no namespace compilado original;
- release `v3.4.3` com `build-info.json` e `SHA256SUMS.txt`.


## Validação física final 3.4.3

A combinação promovida a baseline estável foi confirmada em PS4 real:

- aplicativo **3.4.3**;
- Worker **v11.2**;
- FileKeeper via `dlproxy.uk` reconhecido como `Provider.FileKeeper`;
- rota FileKeeper normalizada para `/f/<token>.pkg`;
- Mocha e VikingFile permanecem nas rotas dedicadas do Worker;
- Rootz e AkiraBox preservam os fluxos já funcionais;
- template ELF preservado com SHA-256:
  `ec778c6b39835e6d9ee8681faf99e827b18da0148b8d8e75f0c9d85925f4b1bc`;
- APK estável SHA-256:
  `0a07d1809efb3479f6984dd12704507cefea1b768a82c0803c4806e6abf6fd0b`.

## Smoke test recomendado

Para validar a 3.4.0 em aparelho real:

1. confirmar o novo ícone no launcher;
2. abrir a Home e usar **Colar** sem pressionar/segurar o campo;
3. validar um PKG que possua mais de uma imagem de sistema;
4. confirmar a galeria horizontal abaixo dos metadados;
5. gerar o ELF;
6. enviar ao PS4;
7. confirmar a tarefa no BGFT/Downloads;
8. repetir com pelo menos um fluxo AkiraBox e um R2 (VikingFile ou Rootz).

## Assinatura de teste

O CI ainda usa uma chave descartável quando não recebe keystore persistente. Builds diferentes podem exigir desinstalar a versão anterior antes da instalação.
