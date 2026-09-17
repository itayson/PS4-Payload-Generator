# Validação 3.5.0 — Tayson PS4 Payload Generator

## Evidência em hardware preservada

Os cinco provedores principais já tiveram o núcleo Android → ELF → GoldHEN → BGFT validado em PS4 real na linha 3.4.x:

- **Mocha:** query autenticada preservada e `#tayson.pkg` usado somente para compatibilidade BGFT;
- **VikingFile:** R2 direto retornou HTTP 403 no BGFT; Worker v8 normalizou o transporte;
- **FileKeeper:** fluxo direto validado quando o endpoint final suporta Range/BGFT;
- **Rootz:** final R2/SigV4 validado via Universal Transport Worker;
- **AkiraBox:** Worker v8 + KV `ROUTES` permitiu download real após o acesso direto ao BGFT ser recusado.

A 3.5.0 não redesenha esse núcleo. Ela adiciona confiabilidade, fila e diagnóstico ao redor dele.

## Smoke test físico parcial — 2026-09-17

Vídeo de validação em Android + PS4 confirmou na build 3.5.0:

- fluxo AkiraBox de um único PKG iniciado pelo botão **Colar**;
- validação automática do PKG sem seletor de arquivo;
- geração do ELF em memória e envio direto ao GoldHEN BinLoader 9090;
- PS4 exibindo **Payload received** para a conexão originada pelo Android;
- app exibindo **Payload enviado ao PS4. Registro BGFT iniciado.**;
- histórico registrando o envio como **ENVIADO**;
- artwork/metadados do pacote exibidos após a validação;
- Worker v8 respondendo **Online • Worker v8 OK**, com latência observada de aproximadamente 49 ms;
- interface, navegação entre Início/Histórico/Worker/Ajustes e identidade 3.5.0 funcionando durante o teste.

Este vídeo valida o caminho de **envio único** e o health check do Worker. Ele não demonstra ainda a fila com dois links consecutivos, a preservação da fila sob falha 9090 nem a inspeção do conteúdo copiado pelo diagnóstico. Esses itens permanecem como bloqueadores antes do merge.


## Mudanças específicas da 3.5.0

### Fila automática transacional

O clipboard pode conter até 20 links HTTP/HTTPS, um por linha. Links repetidos são eliminados na sessão.

A fila obedece às seguintes invariantes:

1. somente um PKG é validado/enviado por vez;
2. o próximo link é consultado com `peek` sem ser removido;
3. ele só é consumido depois que a validação realmente foi iniciada;
4. se a UI ainda estiver ocupada, o item continua na fila;
5. se o acionamento do botão de validação falhar, o item continua na fila;
6. um novo **Colar** não substitui o estado enquanto uma validação ou envio automático estiver ativo;
7. links da fila nunca são persistidos em `Preferences` ou histórico.

### Envio automático

Depois da validação, o ELF é gerado em memória e enviado ao GoldHEN BinLoader TCP 9090 sem abrir o seletor Android de arquivo.

Retry é permitido apenas durante o estabelecimento da conexão TCP. Depois que uma conexão é estabelecida e o envio do payload começa, o ELF não é retransmitido automaticamente, evitando duplicar ou corromper registro BGFT em uma falha parcial.

### Diagnóstico de sessão

O journal em memória registra estágios como:

- fila;
- validação PKG;
- Worker;
- geração do ELF;
- conexão BinLoader;
- erros de UI/artwork.

Antes de entrar no relatório, URLs com query string e campos como `token`, `cookie`, `authorization`, `signature` e `sig` são mascarados. O diagnóstico não é persistido e pode ser limpo em **Ajustes**.

### Worker

A tela Worker consulta `/health`, mostra o resultado e mede a latência sem abrir a porta TCP 9090 do PS4.

Não há varredura automática do BinLoader: uma conexão de teste pode consumir o listener do GoldHEN, portanto a 9090 é aberta somente durante o envio real do ELF.

### Artwork e identidade Android

Permanecem as funções da 3.4.0:

- leitura de `icon0.png`, `pic0.png`, `pic1.png` e assets opcionais por pequenas faixas HTTP;
- fallback de artwork por Title ID;
- 20 recursos de launcher substituídos;
- package público `com.tayson.ps4payloadgenerator`;
- `MonoRuntimeProvider` herdado mantido no namespace compilado original.

## Invariantes funcionais

Permanecem:

- validação do PKG usando a URL real do provedor;
- segunda verificação antes da geração do ELF;
- preservação de queries autenticadas quando necessário;
- detecção de Cloudflare R2/AWS SigV4;
- `BgftRoutePlanner` separado da URL original;
- Universal Transport Worker v8 + KV `ROUTES`;
- `PayloadPatcher` e template ELF existentes;
- envio ao GoldHEN BinLoader TCP 9090;
- nenhum Receiver/RPI.

## Identidade da release

```text
application label: Tayson PS4 Payload Generator
versionName: 3.5.0
versionCode: 38
package: com.tayson.ps4payloadgenerator
```

## CI 3.5.0

O pipeline exige:

- **89 checks** do núcleo aprovados;
- teste transacional da fila, incluindo `peek`, commit esperado e preservação após commit inválido;
- dependency audit com zero membros ausentes/trimmed;
- ILRepack/patch preservando os tokens JNI críticos;
- APK alinhado e assinado;
- assinatura v1/v2/v3 válida;
- `aapt` confirma `com.tayson.ps4payloadgenerator` e `versionName 3.5.0`;
- `versionCode 38` registrado em `build-info.json`;
- runtime Mono herdado preservado;
- artifact `Tayson-PS4-Payload-Generator-3.5.0` com APK, `build-info.json` e `SHA256SUMS.txt`.

## Smoke test físico obrigatório antes da promoção para estável

Executar em Android + PS4 real:

1. confirmar instalação, ícone e abertura da 3.5.0;
2. configurar o IP do PS4 e confirmar que nenhuma conexão 9090 ocorre antes do envio;
3. abrir **Worker** e confirmar `/health` e latência;
4. colar um único PKG e confirmar validação → geração em memória → envio → BGFT;
5. colar pelo menos **dois links válidos**, um por linha;
6. confirmar que o segundo só começa depois do primeiro ELF ser transmitido;
7. durante o primeiro item, tocar **Colar** novamente e confirmar que a fila ativa não é substituída;
8. repetir a fila com pelo menos um fluxo R2 (VikingFile/Rootz) e, se disponível, AkiraBox;
9. provocar uma falha de conexão 9090 antes do envio e confirmar que os itens ainda pendentes permanecem na fila;
10. abrir **Ajustes → Copiar diagnóstico** e confirmar que query strings/tokens não aparecem em claro;
11. confirmar histórico, artwork e galeria;
12. confirmar no PS4 que cada tarefa foi registrada apenas uma vez.

A branch só deve ser integrada à `main` após esse smoke test não revelar regressões.

## Assinatura de teste

O CI usa a keystore de teste do projeto para produzir a build de validação. Antes de uma distribuição pública definitiva, a estratégia de assinatura deve permanecer consistente entre releases para permitir atualização normal pelo Android.
