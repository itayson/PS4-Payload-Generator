<p align="center">
  <img src="assets/file_000000003724820e8f20792443e9ac67.png" alt="Tayson PS4 Payload Generator" width="100%">
</p>

<h1 align="center">Tayson PS4 Payload Generator</h1>

<p align="center">
  <img alt="Version" src="https://img.shields.io/badge/version-3.5.0-1f6feb?style=for-the-badge">
  <img alt="Android" src="https://img.shields.io/badge/platform-Android-3DDC84?style=for-the-badge&logo=android&logoColor=white">
  <img alt="PS4" src="https://img.shields.io/badge/target-PS4-006FCD?style=for-the-badge">
  <img alt="BGFT" src="https://img.shields.io/badge/BGFT-Universal-00AEEF?style=for-the-badge">
</p>

<p align="center">
  <img alt="Worker" src="https://img.shields.io/badge/Worker-v8-FF8800?style=flat-square&logo=cloudflare&logoColor=white">
  <img alt="Providers" src="https://img.shields.io/badge/providers-5%2F5%20validated-2ea44f?style=flat-square">
  <img alt="BinLoader" src="https://img.shields.io/badge/BinLoader-TCP%209090-8250df?style=flat-square">
  <img alt="License" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square">
</p>

<p align="center"><strong>Mocha • VikingFile • FileKeeper • Rootz • AkiraBox</strong></p>

<p align="center">
  Gerador de payloads ELF para PS4 desbloqueado, com validação de PKG, roteamento BGFT, envio direto ao GoldHEN BinLoader e interface Tayson.
</p>

---

## Tayson 3.5.0

A 3.5.0 é uma atualização de **confiabilidade e automação** construída sobre o núcleo BGFT já validado em hardware na linha 3.4.x.

Principais mudanças:

- **fila automática de até 20 links** copiados, um por linha;
- processamento estritamente sequencial: um ELF por vez no BinLoader TCP 9090;
- avanço transacional da fila: o próximo link só é consumido depois que a validação realmente começa;
- proteção contra um novo **Colar** substituir uma validação/envio ainda ativo;
- geração do ELF em memória e envio automático, sem abrir seletor de arquivo no fluxo one-touch;
- retry apenas na fase de conexão ao BinLoader; o payload não é retransmitido depois que uma conexão TCP bem-sucedida começou;
- diagnóstico de sessão com estágios de PKG, Worker, fila, ELF e BinLoader;
- URLs com query, tokens, cookies e assinaturas são mascarados antes de entrar no diagnóstico;
- botão para copiar/limpar diagnóstico;
- tela Worker com health check e indicação de latência;
- preservação da galeria de artwork e fallback por Title ID introduzidos na 3.4.0;
- `versionName 3.5.0` / `versionCode 38`.

A fila e o diagnóstico são mantidos somente em memória. URLs assinadas, cookies e tokens não são persistidos no histórico.

---

## Status

| Componente / Provedor | Estado |
| --- | --- |
| Mocha | ✅ Núcleo validado em PS4 real |
| VikingFile | ✅ Núcleo validado em PS4 real |
| FileKeeper | ✅ Núcleo validado em PS4 real |
| Rootz | ✅ Núcleo validado em PS4 real |
| AkiraBox | ✅ Núcleo validado em PS4 real |
| Geração dinâmica de ELF | ✅ Funcional |
| GoldHEN BinLoader TCP 9090 | ✅ Funcional |
| Registro BGFT | ✅ Funcional |
| Universal Transport Worker | ✅ v8 |
| Cloudflare KV `ROUTES` | ✅ Suportado |
| Fila transacional 3.5.0 | ✅ CI / smoke físico pendente |
| Diagnóstico 3.5.0 | ✅ CI / smoke físico pendente |

O fluxo preservado é:

```text
Android
→ resolve o provedor
→ valida o PKG real
→ lê metadados/artwork
→ prepara a rota BGFT
→ gera o ELF em memória
→ envia ao GoldHEN BinLoader :9090
→ ELF registra a tarefa no BGFT
→ PS4 baixa o PKG
```

Com vários links:

```text
Clipboard (1 link por linha)
→ validar item 1
→ gerar/enviar item 1
→ confirmar transmissão
→ validar item 2
→ ...
```

Nenhum próximo item é enviado em paralelo.

---

## Metadados e imagens do PKG

A validação usa requests limitadas por Range. Depois da validação principal, o aplicativo pode interpretar a tabela de entradas do PKG e localizar assets úteis para apresentação.

Prioridade da galeria:

```text
icon0.png
pic0.png
pic1.png
shareoverlayimage.png
save_data.png
shareprivacyguardimage.png
```

São exibidas no máximo cinco imagens por pacote. A busca de artwork é opcional: uma falha nessa etapa não transforma um PKG já validado em inválido.

---

## Universal Transport Worker v8

Endpoint padrão:

```text
https://drive.itaysondaniel.workers.dev
```

Binding KV esperado:

```text
ROUTES
```

O Worker é usado quando o endereço final do provedor precisa de normalização para o BGFT. URLs R2/SigV4 são preservadas sem reconstrução da assinatura; o AkiraBox pode manter temporariamente cookies, Referer e User-Agent no KV.

A tela **Worker** permite consultar `/health` sem tocar no listener TCP 9090 do PS4.

---

## Interface

A navegação principal é:

```text
Início · Histórico · Worker · Ajustes
```

A Home reúne conexão com PS4, link do PKG, botão **Colar**, provedor detectado, metadados, galeria e estado do fluxo.

Em **Ajustes** ficam o IP do PS4, endpoint do Worker, estado da fila e diagnóstico da sessão. A porta 9090 não é sondada previamente porque uma conexão de teste pode consumir o listener do GoldHEN; ela é aberta somente durante o envio real do ELF.

---

## Release

```text
Application label: Tayson PS4 Payload Generator
Version name: 3.5.0
Version code: 38
Package: com.tayson.ps4payloadgenerator
Worker: Universal Transport Worker v8
BinLoader: TCP 9090
```

A identidade pública do aplicativo é `com.tayson.ps4payloadgenerator`. O namespace herdado do `MonoRuntimeProvider` permanece inalterado internamente para preservar o runtime MAUI/Xamarin da aplicação-base.

---

## Validação

O CI da 3.5.0 exige os 89 checks do núcleo, o teste transacional da fila, auditoria de dependências do runtime empacotado, assinatura APK e confirmação de `versionName`/package via `aapt`.

A 3.5.0 só deve substituir a versão estável depois do smoke test físico Android + PS4, principalmente com dois ou mais PKGs consecutivos.

---

## Uso responsável

Destinado a desenvolvimento, homebrew, testes e gerenciamento de conteúdo que o usuário tenha autorização para utilizar. O projeto não distribui jogos, licenças, chaves ou conteúdo protegido.

## Licença

MIT. Consulte [LICENSE](LICENSE).
