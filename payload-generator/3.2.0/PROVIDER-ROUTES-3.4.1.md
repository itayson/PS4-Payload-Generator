# 3.4.1 — rotas unificadas Mocha, FileKeeper e VikingFile

Esta linha parte do commit conhecido da 3.4.0 e muda somente a camada de transporte dos provedores solicitados.

## Objetivo

O PS4 deixa de receber diretamente a URL final de Mocha e FileKeeper. VikingFile, que já usava Worker para R2/SigV4, passa a ter endpoint nominal próprio.

Fluxo:

```
provedor -> app resolve/valida PKG -> Worker cria rota curta -> PS4 BGFT
```

Rotas:

- Mocha: `POST /api/mocha-routes` -> `/m/<token>.pkg`
- FileKeeper: `POST /api/filek-routes` -> `/f/<token>.pkg`
- VikingFile: `POST /api/viking-routes` -> `/v/<token>.pkg`
- AkiraBox: permanece `/a/<token>.pkg`
- Rootz/R2: permanece compatível com `/api/routes` e `/d/<token>.pkg`

## Compatibilidade

O binding existente continua sendo:

`ROUTES -> tayson-payload-routes`

Nenhum Durable Object novo é exigido.

O Worker v8 permanece no repositório como rollback. O novo arquivo é:

`UPDATE/cloudflare/universal_transport_worker_v11.js`

## Regras por provedor

### Mocha

- aceita somente HTTPS em `mocha.my` e subdomínios;
- exige alvo final `.pkg`;
- preserva query/token/expiry sem reserializar no app;
- Worker usa User-Agent/Referer de Mocha e Range recebido do PS4.

### FileKeeper

- reconhecido explicitamente pelo app como provedor;
- aceita somente HTTPS em `filekeeper.net` e subdomínios;
- exige alvo final `.pkg`;
- Worker usa User-Agent/Referer FileKeeper e mantém Range.

### VikingFile

- continua exigindo URL final R2/SigV4 válida;
- usa rota dedicada v11;
- se o endpoint v11 estiver indisponível, o app mantém fallback para o endpoint inline Viking já existente.

## Segurança

O Worker não é um proxy aberto:

- hosts são allowlisted por provedor;
- não aceita userinfo/fragment;
- Mocha/FileKeeper exigem `.pkg`;
- Viking exige assinatura R2/SigV4 válida;
- as URLs reais permanecem no KV e o PS4 recebe somente a rota curta.

## Validação automatizada

O candidato exige:

- Worker v11 válido como ES module;
- endpoints Mocha/FileK/Viking presentes;
- testes de detecção de FileKeeper;
- testes de roteamento Mocha, FileKeeper e Viking;
- preservação exata da URL autenticada enviada pelo app;
- build Android 3.4.1 / versionCode 38.

A promoção para main continua bloqueada até teste físico no PS4.
