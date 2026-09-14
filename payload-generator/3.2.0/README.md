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

### AkiraBox

O app aceita tanto `akirabox.com` quanto o domínio atual `akirabox.to`. Links de página como:

```text
https://akirabox.to/<id>/file
```

abrem no WebView assistido. O capturador observa links, formulários, `fetch`, XHR e URLs assinadas `/download/...` geradas depois da verificação/Turnstile. O download só é aceito quando `DownloadClient.ProbeAsync` confirma que a resposta é realmente um PKG PS4 e suporta `Range`.

Nenhum cookie ou token de navegador é gravado no ELF. O objetivo é capturar a URL final liberada pelo próprio site e validar se ela é utilizável diretamente pelo PS4.

### Rootz

Links públicos no formato:

```text
https://rootz.so/d/<shortId>
```

são tentados primeiro pelo endpoint público de proxy de download:

```text
https://www.rootz.so/api/files/proxy-download/<shortId>
```

Se esse caminho não estiver disponível para um arquivo específico, o app cai automaticamente para o WebView e captura o fluxo de download do site. Como a URL Rootz de proxy não precisa terminar em `.pkg`, o `PayloadPatcher` aplica o fragmento local `#tayson.pkg` quando necessário para a validação de extensão do BGFT.

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
- assinatura AWS, data e TTL são obrigatórios e validados estruturalmente no Android;
- o Worker v6 aplica a verificação por relógio e recusa VikingFile expirado ou com menos de 90 segundos de validade;
- no Mocha, o app valida a forma do parâmetro `expires`, mas o próprio provedor continua sendo a autoridade sobre a validade temporal do token;
- AkiraBox e Rootz continuam sujeitos à mesma validação real de PKG, tamanho e `Range` antes de qualquer ELF ser gerado;
- o Worker não é um proxy HTTP genérico;
- a URL SigV4 original é armazenada/encaminhada sem reconstruir a query.

### Assinatura do APK de CI

O workflow gera uma **build de teste**. Sem um keystore persistente configurado no runner, ele cria uma chave de teste descartável. Por isso o certificado e o SHA-256 do APK podem mudar entre execuções, mesmo sem mudança funcional no aplicativo. Nesse caso, a nova build pode exigir desinstalar uma versão Tayson anterior antes de instalar.

Para atualização in-place, configure um keystore persistente de forma segura (por exemplo, via GitHub Actions secret) ou reutilize explicitamente a chave de teste já adotada pelo projeto. Não adicione uma chave privada ao repositório somente para estabilizar o CI.

Os hashes exatos de cada execução ficam em `SHA256SUMS.txt` e `build-info.json` dentro do artifact do GitHub Actions.

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
