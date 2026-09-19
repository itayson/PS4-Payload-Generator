<p align="center">
  <img src="assets/file_000000003724820e8f20792443e9ac67.png" alt="Tayson PS4 Payload Generator" width="100%">
</p>

<h1 align="center">Tayson PS4 Payload Generator</h1>

<p align="center">
  <img alt="Version" src="https://img.shields.io/badge/version-3.4.3-1f6feb?style=for-the-badge">
  <img alt="Android" src="https://img.shields.io/badge/platform-Android-3DDC84?style=for-the-badge&logo=android&logoColor=white">
  <img alt="PS4" src="https://img.shields.io/badge/target-PS4-006FCD?style=for-the-badge">
  <img alt="BGFT" src="https://img.shields.io/badge/BGFT-Universal-00AEEF?style=for-the-badge">
</p>

<p align="center">
  <img alt="Worker" src="https://img.shields.io/badge/Worker-v11.2-FF8800?style=flat-square&logo=cloudflare&logoColor=white">
  <img alt="Providers" src="https://img.shields.io/badge/providers-5%2F5%20validated-2ea44f?style=flat-square">
  <img alt="BinLoader" src="https://img.shields.io/badge/BinLoader-TCP%209090-8250df?style=flat-square">
  <img alt="License" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square">
</p>

<p align="center"><strong>Mocha • VikingFile • FileKeeper • Rootz • AkiraBox</strong></p>

<p align="center">
  Gerador universal de payloads ELF para PS4 desbloqueado, com validação de PKG, roteamento BGFT e interface Tayson.
</p>

---

## Tayson 3.4.3

A 3.4.3 é o baseline estável validado em PS4 real. Ela preserva o núcleo BGFT/ELF conhecido e consolida o roteamento por provedor:

- botão **Colar** e pipeline automático de validação;
- galeria de metadados/artwork do PKG;
- Mocha via rota curta do Worker;
- VikingFile via rota dedicada do Worker para R2/SigV4;
- FileKeeper com reconhecimento de `filekeeper.net` e do transporte real `dlproxy.uk`;
- Rootz preservado via Worker/R2;
- AkiraBox preservado com contexto temporário no KV `ROUTES`;
- FileKeeper mantém a identidade do provedor mesmo após redirects/resolução;
- o BGFT recebe sempre uma URL curta terminada em `.pkg` para FileKeeper;
- `versionName 3.4.3` / `versionCode 40`;
- Worker estável: **v11.2**.

A galeria é mantida somente em memória durante a sessão. URLs assinadas, cookies e tokens continuam fora do histórico persistente.

---

## Status

| Componente / Provedor | Estado |
| --- | --- |
| Mocha | ✅ Validado em PS4 real |
| VikingFile | ✅ Validado em PS4 real |
| FileKeeper | ✅ Validado em PS4 real |
| Rootz | ✅ Validado em PS4 real |
| AkiraBox | ✅ Validado em PS4 real |
| Geração dinâmica de ELF | ✅ Funcional |
| GoldHEN BinLoader TCP 9090 | ✅ Funcional |
| Registro BGFT | ✅ Funcional |
| Universal Transport Worker | ✅ v11.2 |
| Cloudflare KV `ROUTES` | ✅ Suportado |
| Interface Tayson / PT-BR | ✅ 3.4.3 |

O fluxo já validado permanece:

```text
Android
→ resolve o provedor
→ valida o PKG real
→ lê metadados/artwork
→ prepara a rota BGFT
→ gera o ELF
→ envia ao GoldHEN BinLoader :9090
→ ELF registra a tarefa no BGFT
→ PS4 baixa o PKG
```

---

## Metadados e imagens do PKG

A validação continua usando requests limitadas por Range. Na 3.4.0, depois da validação principal, o aplicativo também tenta ler a tabela de entradas do PKG e localizar os assets de sistema mais úteis para apresentação.

Prioridade da galeria:

```text
icon0.png
pic0.png
pic1.png
shareoverlayimage.png
save_data.png
shareprivacyguardimage.png
```

São exibidas no máximo cinco imagens por pacote. A busca de artwork é opcional: se um provedor recusar uma Range auxiliar, a validação do PKG continua normalmente.

---

## Universal Transport Worker v11.2

Endpoint padrão:

```text
https://drive.itaysondaniel.workers.dev
```

Binding KV esperado:

```text
ROUTES
```

O Worker normaliza os provedores para URLs compatíveis com BGFT e mantém o binding `ROUTES`.

Rotas estáveis:

```text
AkiraBox   → /a/<token>.pkg
Rootz/R2   → /d/<token>.pkg
Mocha      → /m/<token>.pkg
FileKeeper → /f/<token>.pkg
VikingFile → /v/<token>.pkg
```

O FileKeeper reconhece também `dlproxy.uk`, host real observado no transporte do provedor. URLs R2/SigV4 continuam preservadas sem reconstrução da assinatura.

---

## Interface

A navegação principal é:

```text
Início · Histórico · Worker · Ajustes
```

A Home reúne conexão com PS4, link do PKG, botão **Colar**, provedor detectado, metadados, galeria, estágio do fluxo, geração do ELF e envio ao console.

O Histórico persiste somente metadados não sensíveis. Worker e IP do PS4 são configuráveis e persistentes.

---

## Release

```text
Application label: Tayson PS4 Payload Generator
Version name: 3.4.3
Version code: 40
Package: com.tayson.ps4payloadgenerator
Worker: Universal Transport Worker v11.2
BinLoader: TCP 9090
```

O package ID público atual é `com.tayson.ps4payloadgenerator`.

---


## Download estável

A release estável atual é **v3.4.3**.

Artefatos publicados:

- `PS4-Payload-Generator-3.4.3-TaySon.apk`;
- `universal_transport_worker_v11_2.js`;
- `SHA256SUMS.txt`.

APK SHA-256 validado:

```text
88e340f6db250c20506e570832c7a3ba9800181dd4390e372b022bae59527e6e
```

## Uso responsável

Destinado a desenvolvimento, homebrew, testes e gerenciamento de conteúdo que o usuário tenha autorização para utilizar. O projeto não distribui jogos, licenças, chaves ou conteúdo protegido.

## Licença

MIT. Consulte [LICENSE](LICENSE).
