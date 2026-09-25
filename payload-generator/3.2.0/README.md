# Tayson PS4 Payload Generator 3.5.0 — Mega BGFT / Worker v12

Aplicativo Android para validar PKGs de PS4, preparar a rota BGFT, gerar um ELF específico para a tarefa e enviá-lo diretamente ao **GoldHEN BinLoader TCP 9090**.

**Não usa Receiver, Remote Package Installer nem retransmite o PKG inteiro pelo celular.**

## Novidades da 3.5.0

- volta ao **template ELF/BGFT da 3.4.3**, sem as experiências Int/Service/Debug das builds de diagnóstico;
- preserva o botão **Colar**, artwork, histórico e validação por Range da linha 3.4.3;
- Worker **v12-bgft-compat-9-12** com HEAD emulado por Range, validação estrita de `Content-Range` e upstream sem compressão;
- Rootz ganha rota dedicada `/z/<token>.pkg`, com fallback para o endpoint R2 legado;
- AkiraBox reconhece hosts regionais `akirabox.com` e `akirabox.to`;
- Mocha e FileKeeper podem usar rota inline quando não dependem de cookie e o KV não estiver disponível;
- redirects do Worker ficam confinados ao mesmo provedor aprovado;
- `versionName 3.5.0` e `versionCode 50`.

A 3.4.3 continua sendo o baseline físico conhecido. A 3.5.0 permanece prerelease até fechar a matriz de testes em hardware.

Falhas ao carregar imagens extras não invalidam um PKG que já passou na validação principal.

## Fluxo

```text
Link do usuário
    ↓
Provider resolver / navegador assistido
    ↓
Validação do PKG real
Magic • Content ID • tamanho • HTTP Range
    ↓
Metadados / artwork
icon0 • pic0 • pic1 • fallback PSN por Title ID
    ↓
BGFT Route Planner
    ├── direto quando compatível
    └── Worker v12 / KV ROUTES
        ├── AkiraBox   /a/<token>.pkg
        ├── Rootz/R2   /z/<token>.pkg
        ├── Mocha      /m/<token>.pkg
        ├── FileKeeper /f/<token>.pkg
        └── VikingFile /v/<token>.pkg
    ↓
PayloadPatcher
    ↓
ELF
    ↓
GoldHEN BinLoader :9090
    ↓
BGFT do PS4
```

## Provedores validados em PS4 real

| Provedor | Transporte BGFT | Estado |
| --- | --- | --- |
| Mocha | rota curta Worker `/m/<token>.pkg` | Validado |
| VikingFile | R2/SigV4 via Worker `/v/<token>.pkg` | Validado |
| FileKeeper | rota curta Worker `/f/<token>.pkg`, incluindo `dlproxy.uk` | Validado |
| Rootz | R2/SigV4 via Worker `/d/<token>.pkg` | Validado |
| AkiraBox | rota temporária Worker `/a/<token>.pkg` com contexto de sessão no KV | Validado |

O fluxo Android → ELF → GoldHEN → BGFT já foi confirmado em hardware real, inclusive com AkiraBox.

## Universal Transport Worker v12

Worker padrão:

```text
https://drive.itaysondaniel.workers.dev
```

Binding KV:

```text
ROUTES
```

O endpoint pode ser alterado em **Ajustes** e o valor persistido é usado pelo `BgftRoutePlanner` na geração real do ELF.

## Interface

A navegação principal permanece:

```text
Início · Histórico · Worker · Ajustes
```

A Home agora contém o botão **Colar** e, após a validação, mostra uma galeria horizontal quando o PKG disponibiliza mais de uma imagem.

## Histórico e privacidade

O histórico persistente continua armazenando somente metadados não sensíveis. A galeria de artwork não é persistida.

Não são persistidos:

- URLs assinadas;
- cookies;
- tokens;
- credenciais de provedor;
- imagens extraídas do PKG.

## Identidade da release

```text
Application label: Tayson PS4 Payload Generator
Version name: 3.5.0
Version code: 50
Package: com.tayson.ps4payloadgenerator
```

O package ID agora é próprio do projeto Tayson. Como o Android trata esse identificador como a identidade do aplicativo, esta build é instalada como um aplicativo diferente da versão antiga `com.phoenixx.ps4remotepkg`; os dados locais da instalação anterior não são migrados automaticamente.

A classe herdada `com.phoenixx.ps4remotepkg.mono.MonoRuntimeProvider.__mono_init__` permanece com o namespace original porque ela pertence ao runtime MAUI/Xamarin compilado em `classes2.dex`; apenas a identidade pública do pacote, a permissão dinâmica e a authority do AndroidX Startup são alteradas.

## Compatibilidade preservada

A reconstrução continua preservando `classes.dex`, `classes2.dex`, `assets/template.elf`, integração JNI/MAUI, PayloadPatcher, Worker v12 e BinLoader 9090.

## Uso responsável

Destinado a desenvolvimento, homebrew, testes e gerenciamento de conteúdo autorizado. O projeto não distribui jogos, licenças, chaves ou conteúdo protegido.
