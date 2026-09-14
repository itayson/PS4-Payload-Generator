# Tayson PS4 Payload Generator 3.2.0 — BGFT universal

Esta versão continua usando o fluxo original do **PS4 Payload Generator**:

1. o Android valida o PKG real;
2. gera um ELF a partir de `assets/template.elf`;
3. injeta esse ELF diretamente no **GoldHEN BinLoader TCP 9090**.

**Não usa Receiver, RPI nem um aplicativo residente no PS4.**

## Compatibilidade de provedores

### Links diretos

Links HTTP/HTTPS de PKG que já aceitam `Range` continuam diretos.

### Mocha

A URL autenticada é preservada integralmente e só recebe o fragmento local:

```text
#tayson.pkg
```

Exemplo:

```text
https://cdn.mocha.my/.../game.pkg?token=...&expires=...&node=sugar#tayson.pkg
```

O fragmento não é enviado ao servidor HTTP. Em hardware real, isso eliminou o erro BGFT `0x80990033` (extensão inválida) e o console chegou a reconhecer o conteúdo como já instalado.

### VikingFile

A URL R2/SigV4 recém-liberada é validada no Android e, somente na geração do ELF, é convertida para uma rota no Worker:

```text
https://drive.itaysondaniel.workers.dev/d/<token>.pkg
```

Se o Worker não tiver KV `ROUTES`, ele devolve uma rota inline:

```text
https://drive.itaysondaniel.workers.dev/viking.pkg?u=<base64url>#tayson.pkg
```

O telefone não retransmite o PKG. O PS4 baixa do Worker, que encaminha `GET`/`Range` ao R2. `HEAD` é atendido por uma sondagem `GET Range: bytes=0-0`, evitando a incompatibilidade observada com URLs R2 assinadas apenas para GET.

## Worker v6

Publique `UPDATE/cloudflare/vikingfile_worker_v6.js` em:

```text
https://drive.itaysondaniel.workers.dev
```

Opcionalmente crie um namespace Cloudflare KV e faça o binding com o nome:

```text
ROUTES
```

Com KV, o app recebe URLs curtas e não depende do limite de 2.047 bytes do campo URL do ELF. Sem KV, o Worker continua funcional para URLs que cabem no payload.

Health check esperado:

```text
Tayson Payload Generator VikingFile Worker v6: OK
```

## Segurança e assinatura

- a URL VikingFile real é validada antes da transformação;
- somente hosts `vikingfile.<account-id>.r2.cloudflarestorage.com` são aceitos pelo Worker;
- assinatura AWS, data e expiração são obrigatórias;
- o Worker não é um proxy HTTP genérico;
- a URL SigV4 original é armazenada/encaminhada sem reconstruir a query;
- URLs Mocha e VikingFile expiradas ou com menos de 90 segundos de validade são recusadas antes da geração do payload.

## Arquitetura

```text
URL do usuário
    ↓
ResolveAsync / ProviderBrowser
    ↓
DownloadClient.ProbeAsync   ← valida PKG real, Content ID, tamanho e Range
    ↓
Integration.GenerateAsync
    ↓
BgftRoutePlanner            ← adapta somente o transporte BGFT
    ↓
PayloadPatcher              ← grava URL + metadados no ELF
    ↓
GoldHEN BinLoader :9090
    ↓
ELF registra tarefa BGFT no PS4
```
