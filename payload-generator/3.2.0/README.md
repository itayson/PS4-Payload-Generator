# Tayson PS4 Payload Generator 3.4.0

Aplicativo Android para validar PKGs de PS4, preparar a rota BGFT, gerar um ELF específico para a tarefa e enviá-lo diretamente ao **GoldHEN BinLoader TCP 9090**.

**Não usa Receiver, Remote Package Installer nem retransmite o PKG inteiro pelo celular.**

## Novidades da 3.4.0

- botão **Colar** ao lado do campo de link;
- galeria de artwork do PKG;
- leitura da tabela de entradas do PKG para localizar `icon0.png`, `pic0.png` e `pic1.png` quando presentes;
- leitura opcional por HTTP Range dos assets de imagem sem baixar o pacote inteiro;
- cache das imagens somente durante a sessão;
- novo ícone Tayson fornecido para launcher Android e adaptive icon;
- `versionName 3.4.0` e `versionCode 37`.

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
    ├── direto
    ├── Mocha + #tayson.pkg
    └── Worker v8 / KV ROUTES
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
| Mocha | URL autenticada preservada + fragmento local `.pkg` | Validado |
| VikingFile | R2/SigV4 via Worker v8 | Validado |
| FileKeeper | Direto quando compatível | Validado |
| Rootz | R2/SigV4 via Worker v8 | Validado |
| AkiraBox | rota temporária Worker v8 com contexto de sessão no KV | Validado |

O fluxo Android → ELF → GoldHEN → BGFT já foi confirmado em hardware real, inclusive com AkiraBox.

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
Version name: 3.4.0
Version code: 37
Package: com.tayson.ps4payloadgenerator
```

O package ID agora é próprio do projeto Tayson. Como o Android trata esse identificador como a identidade do aplicativo, esta build é instalada como um aplicativo diferente da versão antiga `com.phoenixx.ps4remotepkg`; os dados locais da instalação anterior não são migrados automaticamente.

A classe herdada `com.phoenixx.ps4remotepkg.mono.MonoRuntimeProvider.__mono_init__` permanece com o namespace original porque ela pertence ao runtime MAUI/Xamarin compilado em `classes2.dex`; apenas a identidade pública do pacote, a permissão dinâmica e a authority do AndroidX Startup são alteradas.

## Compatibilidade preservada

A reconstrução continua preservando `classes.dex`, `classes2.dex`, `assets/template.elf`, integração JNI/MAUI, PayloadPatcher, Worker v8 e BinLoader 9090.

## Uso responsável

Destinado a desenvolvimento, homebrew, testes e gerenciamento de conteúdo autorizado. O projeto não distribui jogos, licenças, chaves ou conteúdo protegido.
