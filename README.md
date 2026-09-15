<p align="center">
  <img src="assets/tayson-ps4-payload-generator-banner.jpg" alt="Tayson PS4 Payload Generator" width="100%">
</p>

<h1 align="center">Tayson PS4 Payload Generator</h1>

<p align="center">
  <img alt="Version" src="https://img.shields.io/badge/version-3.2.0-1f6feb?style=for-the-badge">
  <img alt="Android" src="https://img.shields.io/badge/platform-Android-3DDC84?style=for-the-badge&logo=android&logoColor=white">
  <img alt="PS4" src="https://img.shields.io/badge/target-PS4-006FCD?style=for-the-badge">
  <img alt="BGFT" src="https://img.shields.io/badge/BGFT-Universal-00AEEF?style=for-the-badge">
</p>

<p align="center">
  <img alt="Worker" src="https://img.shields.io/badge/Worker-v8-FF8800?style=flat-square&logo=cloudflare&logoColor=white">
  <img alt="Providers" src="https://img.shields.io/badge/providers-5%2F5%20validated-2ea44f?style=flat-square">
  <img alt="Hardware" src="https://img.shields.io/badge/PS4%20hardware-validated-2ea44f?style=flat-square">
  <img alt="BinLoader" src="https://img.shields.io/badge/BinLoader-TCP%209090-8250df?style=flat-square">
  <img alt="License" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square">
</p>

<p align="center">
  <strong>Mocha • VikingFile • FileKeeper • Rootz • AkiraBox</strong>
</p>

<p align="center">
  Gerador universal de payloads ELF para PS4 desbloqueado, com resolução de links, validação de PKG e roteamento BGFT compatível com múltiplos provedores.
</p>

---

## Visão geral

O **Tayson PS4 Payload Generator** simplifica um fluxo que normalmente exige várias etapas manuais: o aplicativo Android recebe o link do arquivo, resolve o provedor, valida o PKG, prepara a URL compatível com o BGFT, gera um ELF específico para aquela tarefa e envia o payload diretamente ao **GoldHEN BinLoader pela porta TCP `9090`**.

Não utiliza Receiver, Remote Package Installer ou retransmissão integral do PKG pelo celular. Depois que o payload é enviado, o próprio PS4 registra a tarefa no BGFT e baixa o arquivo pela rota adequada.

---

## Status atual

| Componente / Provedor | Status |
| --- | --- |
| Mocha | ✅ Validado em PS4 real |
| VikingFile | ✅ Validado em PS4 real |
| FileKeeper | ✅ Validado em PS4 real |
| Rootz | ✅ Validado em PS4 real |
| AkiraBox | ✅ Validado em PS4 real |
| Geração dinâmica de ELF | ✅ Funcional |
| GoldHEN BinLoader TCP 9090 | ✅ Funcional |
| Registro BGFT | ✅ Funcional |
| Cloudflare Worker universal | ✅ v8 |
| Cloudflare KV `ROUTES` | ✅ Suportado |

A compatibilidade acima foi validada com downloads reais iniciados pelo PS4, incluindo provedores com URLs temporárias, links assinados e restrições de sessão.

---

## Como funciona

```text
Link do usuário
      │
      ▼
Resolver do provedor
      │
      ▼
Validação do PKG
Magic / Content ID / tamanho / HTTP Range
      │
      ▼
BGFT Route Planner
      │
      ├── link direto compatível ───────────────┐
      │                                         │
      ├── Mocha → preserva query + fragmento   │
      │                                         │
      └── VikingFile / Rootz / AkiraBox        │
             ↓                                  │
      Universal Transport Worker v8            │
             ↓                                  │
      rota temporária `.pkg`                    │
                                                ▼
                                     Gerador de ELF
                                                │
                                                ▼
                               GoldHEN BinLoader :9090
                                                │
                                                ▼
                                      PS4 / BGFT
                                                │
                                                ▼
                                         Download PKG
```

O aplicativo mantém separadas a **URL real do provedor** e a **URL de transporte entregue ao BGFT**. A URL original é usada para validação; somente depois dessa verificação a rota é adaptada para o PS4.

---

## Recursos principais

- geração dinâmica de payload ELF a partir de cada PKG;
- envio direto do ELF ao GoldHEN BinLoader pela porta `9090`;
- validação de assinatura PKG antes da geração do payload;
- leitura de Content ID e metadados básicos do pacote;
- validação de tamanho e suporte a HTTP Range;
- suporte a jogos, updates e DLCs conforme os metadados do PKG;
- resolução automática de links de múltiplos provedores;
- preservação de queries, tokens e assinaturas temporárias;
- suporte a URLs AWS SigV4 / Cloudflare R2;
- tratamento de links que exigem navegador, cookies ou contexto de sessão;
- compatibilidade BGFT com URLs cuja extensão `.pkg` não aparece no final da string original;
- Cloudflare Worker universal para normalizar `HEAD`, `GET` e `Range` quando necessário;
- rotas curtas temporárias usando Cloudflare KV;
- sem Receiver;
- sem RPI;
- sem relay do PKG pelo Android;
- sem URL fixa de jogo no código.

---

## Compatibilidade por provedor

### Mocha

O link autenticado é preservado integralmente, incluindo parâmetros como token, expiração e nó. Para compatibilidade com a validação de extensão do BGFT, o gerador adiciona apenas um fragmento local terminado em `.pkg`, sem alterar a requisição HTTP enviada ao servidor.

### VikingFile

URLs finais R2 assinadas são reconhecidas automaticamente. Quando necessário, o aplicativo cria uma rota temporária através do Worker universal, que responde ao comportamento esperado pelo BGFT e mantém suporte a downloads por intervalo.

### Rootz

Links finais baseados em Cloudflare R2 / AWS SigV4 usam a mesma camada universal de transporte. O Worker mantém a URL assinada intacta e adapta as requisições do BGFT sem reconstruir a assinatura.

### AkiraBox

O aplicativo utiliza o fluxo de navegador quando necessário, captura a URL temporária `download.akirabox.com`, valida o PKG com o contexto de sessão e registra uma rota temporária no Worker. Cookies, Referer e User-Agent podem ser mantidos no KV e não precisam aparecer dentro do ELF.

### FileKeeper

Links compatíveis são resolvidos e validados pelo aplicativo antes da geração do payload. Quando o endereço final já funciona diretamente com o BGFT, nenhuma camada intermediária é adicionada.

---

## Universal Transport Worker v8

O Worker é usado somente quando o endereço final do provedor não é adequado para acesso direto pelo BGFT.

Ele pode:

- criar URLs `.pkg` curtas e temporárias;
- armazenar rotas no Cloudflare KV;
- preservar URLs R2/SigV4 sem modificar sua assinatura;
- transformar o `HEAD` do BGFT em uma pequena requisição `GET Range: bytes=0-0` quando o provedor não aceita `HEAD` da mesma forma;
- encaminhar `GET`, `Range` e `If-Range`;
- manter contexto de sessão do AkiraBox;
- impedir uso como proxy genérico através de validação de hosts e formatos aceitos.

### Binding KV

No Cloudflare Worker, configure um binding KV com o nome exato:

```text
ROUTES
```

O namespace pode ter qualquer nome, por exemplo:

```text
tayson-payload-routes
```

Configuração conceitual:

```text
Worker
  └── env.ROUTES
          ↓
      KV Namespace
      tayson-payload-routes
```

Com o KV, o PS4 recebe somente uma rota temporária curta, como:

```text
https://seu-worker.workers.dev/a/xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.pkg
```

em vez de carregar cookies, tokens ou URLs longas diretamente no ELF.

---

## Requisitos

- PlayStation 4 desbloqueado;
- GoldHEN com **BinLoader Server** habilitado;
- porta TCP `9090` acessível na rede local;
- celular Android e PS4 na mesma rede para o envio do ELF;
- conexão com a internet para resolver e baixar os PKGs;
- Cloudflare Worker v8 para os provedores que exigem transporte intermediário;
- binding KV `ROUTES` recomendado para o modo universal completo.

---

## Uso

1. Ative **Enable BinLoader Server** no GoldHEN.
2. Informe no aplicativo o IP local do PS4.
3. Cole um link suportado ou abra o fluxo do provedor dentro do navegador integrado.
4. Aguarde a resolução e a validação do PKG.
5. Gere o payload.
6. Envie o ELF para a porta `9090`.
7. O ELF registra a tarefa no BGFT.
8. O download passa a ser controlado pelo próprio PS4.

Para links temporários, gere sempre um endereço novo quando o anterior estiver expirado.

---

## O que o projeto não faz

O Tayson PS4 Payload Generator **não** funciona como servidor de jogos, não hospeda PKGs e não distribui conteúdo protegido. Também não transforma o celular em Receiver/RPI.

O aplicativo atua como ferramenta de resolução, validação, geração e envio de payloads para conteúdo que o próprio usuário forneça e tenha autorização para utilizar.

---

## Arquitetura

Os componentes principais do projeto são separados por responsabilidade:

```text
Provider Resolver
      ↓
PKG Validator / Scanner
      ↓
BGFT Route Planner
      ↓
Payload Patcher / ELF Generator
      ↓
BinLoader Sender :9090
```

Essa separação permite adicionar novos provedores sem alterar o código nativo do payload para cada site.

---

## Segurança e integridade

Antes de gerar o ELF, o aplicativo procura confirmar que o recurso realmente se comporta como um PKG de PS4. Páginas HTML, respostas JSON inesperadas, dados compactados incompatíveis, Content IDs inválidos e servidores que não oferecem comportamento de Range adequado são rejeitados.

URLs assinadas são tratadas como valores opacos sempre que possível. O projeto evita reordenar ou reconstruir queries de autenticação, reduzindo o risco de invalidar tokens e assinaturas.

As rotas salvas em KV são temporárias e devem expirar junto com o acesso original do provedor.

---

## Desenvolvimento atual

Versão atual: **3.2.0**

Branch de desenvolvimento da camada universal:

```text
feature/payload-generator-bgft-universal
```

Fluxo atual do projeto:

```text
Android
→ resolve e valida o PKG
→ prepara a rota BGFT
→ gera o ELF
→ envia para GoldHEN BinLoader :9090
→ ELF registra no BGFT
→ PS4 baixa o PKG
```

---

## Uso responsável

Este projeto é destinado a desenvolvimento, homebrew, testes e gerenciamento de conteúdo autorizado em consoles do próprio usuário.

O repositório não inclui jogos, chaves, licenças, credenciais de terceiros ou conteúdo protegido por direitos autorais. O usuário é responsável pelos links e arquivos utilizados.

PlayStation, PS4 e marcas relacionadas pertencem aos seus respectivos proprietários. Este projeto é independente e não possui afiliação com a Sony Interactive Entertainment, GoldHEN ou com os provedores de hospedagem suportados.

---

## Licença

Distribuído sob a licença **MIT**. Consulte [LICENSE](LICENSE).
