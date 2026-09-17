# Tayson PS4 Payload Generator 3.5.0

Aplicativo Android para validar PKGs de PS4, preparar a rota BGFT, gerar um ELF específico para a tarefa e enviá-lo diretamente ao **GoldHEN BinLoader TCP 9090**.

**Não usa Receiver, Remote Package Installer nem retransmite o PKG inteiro pelo celular.**

## Novidades da 3.5.0

- fila automática de até 20 links copiados, um por linha;
- validação, geração e envio estritamente sequenciais;
- avanço transacional: o próximo item só é removido da fila quando sua validação realmente começa;
- proteção contra substituir a fila durante um fluxo automático ainda ativo;
- geração do ELF em memória no fluxo one-touch;
- retry somente na conexão TCP 9090, nunca depois que o envio do payload começou;
- diagnóstico de sessão com redaction de query strings, tokens, cookies e assinaturas;
- botão para copiar/limpar diagnóstico;
- health check do Worker com latência;
- galeria/artwork da 3.4.0 preservados;
- `versionName 3.5.0` e `versionCode 38`.

Falhas ao carregar imagens extras continuam sem invalidar um PKG que já passou na validação principal.

## Fluxo

```text
Link(s) do usuário
    ↓
Provider resolver / navegador assistido
    ↓
Validação do PKG real
Magic • Content ID • tamanho • HTTP Range
    ↓
Metadados / artwork
icon0 • pic0 • pic1 • fallback por Title ID
    ↓
BGFT Route Planner
    ├── direto
    ├── Mocha + #tayson.pkg
    └── Worker v8 / KV ROUTES
    ↓
PayloadPatcher
    ↓
ELF em memória
    ↓
GoldHEN BinLoader :9090
    ↓
BGFT do PS4
```

Quando há mais de um link, o item seguinte só inicia depois da transmissão bem-sucedida do ELF atual.

## Provedores com núcleo validado em PS4 real

| Provedor | Transporte BGFT | Estado |
| --- | --- | --- |
| Mocha | URL autenticada preservada + fragmento local `.pkg` | Validado |
| VikingFile | R2/SigV4 via Worker v8 | Validado |
| FileKeeper | Direto quando compatível | Validado |
| Rootz | R2/SigV4 via Worker v8 | Validado |
| AkiraBox | rota temporária Worker v8 com contexto de sessão no KV | Validado |

O fluxo Android → ELF → GoldHEN → BGFT já foi confirmado em hardware real na linha 3.4.x. As novas camadas de fila e diagnóstico da 3.5.0 ainda exigem o smoke test físico antes de promoção para estável.

## Universal Transport Worker v8

Worker padrão:

```text
https://drive.itaysondaniel.workers.dev
```

Binding KV:

```text
ROUTES
```

O endpoint pode ser alterado em **Ajustes** e o valor persistido é usado pelo `BgftRoutePlanner` na geração real do ELF.

A tela Worker consulta `/health` sem abrir a porta 9090 do PS4. A porta do BinLoader só é usada durante o envio real, evitando consumir o listener do GoldHEN com sondagens prévias.

## Interface

A navegação principal permanece:

```text
Início · Histórico · Worker · Ajustes
```

A Home contém o botão **Colar**, metadados, galeria e estado do fluxo. Em **Ajustes** ficam IP do PS4, Worker, fila pendente e diagnóstico da sessão.

## Histórico e privacidade

O histórico persistente armazena somente metadados não sensíveis. A fila, o diagnóstico e a galeria de artwork ficam somente em memória.

Não são persistidos:

- URLs assinadas;
- cookies;
- tokens;
- credenciais de provedor;
- imagens extraídas do PKG;
- links pendentes da fila.

## Identidade da release

```text
Application label: Tayson PS4 Payload Generator
Version name: 3.5.0
Version code: 38
Package: com.tayson.ps4payloadgenerator
```

O package ID é próprio do projeto Tayson. Builds com a identidade antiga `com.phoenixx.ps4remotepkg` são tratadas pelo Android como outro aplicativo e não compartilham automaticamente os dados locais.

A classe herdada `com.phoenixx.ps4remotepkg.mono.MonoRuntimeProvider.__mono_init__` permanece com o namespace original porque pertence ao runtime MAUI/Xamarin compilado em `classes2.dex`; apenas a identidade pública do APK é alterada.

## Compatibilidade preservada

A reconstrução continua preservando `classes.dex`, `classes2.dex`, integração JNI/MAUI, PayloadPatcher, Worker v8 e BinLoader 9090. O template ELF é apenas corrigido pelo patch de diagnóstico já usado pela linha estável; o fluxo BGFT não foi redesenhado na 3.5.0.

## Uso responsável

Destinado a desenvolvimento, homebrew, testes e gerenciamento de conteúdo autorizado. O projeto não distribui jogos, licenças, chaves ou conteúdo protegido.
