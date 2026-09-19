# Rotas de provedores — 3.4.3 estável

## Arquitetura

```text
link do usuário
→ resolver do provedor
→ validação do PKG real
→ identidade do provedor preservada
→ Worker v11.2
→ URL curta .pkg
→ ELF
→ GoldHEN BinLoader :9090
→ BGFT
```

## Rotas

| Provedor | Endpoint de criação | URL entregue ao PS4 |
| --- | --- | --- |
| AkiraBox | `POST /api/akira-routes` | `/a/<token>.pkg` |
| Rootz/R2 | `POST /api/routes` | `/d/<token>.pkg` |
| Mocha | `POST /api/mocha-routes` | `/m/<token>.pkg` |
| FileKeeper | `POST /api/filek-routes` | `/f/<token>.pkg` |
| VikingFile | `POST /api/viking-routes` | `/v/<token>.pkg` |

## FileKeeper / dlproxy

O teste físico mostrou que o transporte real do FileKeeper pode chegar ao app como:

```text
https://tunnel1.dlproxy.uk/download/...
```

A 3.4.3 reconhece `dlproxy.uk` e subdomínios como `Provider.FileKeeper`. A identidade do provedor é preservada mesmo após redirects e resolução, e o BGFT recebe sempre uma URL pública terminada em:

```text
/f/<token>.pkg
```

Isso evita que extensões/caminhos internos do provedor sejam expostos diretamente ao registrador BGFT.

## Worker v11.2

Arquivo estável:

```text
payload-generator/3.2.0/UPDATE/cloudflare/universal_transport_worker_v11_2.js
```

Binding:

```text
ROUTES -> tayson-payload-routes
```

Nenhum Durable Object adicional é necessário.

## Segurança

O Worker não funciona como proxy aberto:

- provedores usam hosts allowlisted;
- FileKeeper aceita `filekeeper.net` e `dlproxy.uk`;
- Viking/Rootz mantêm validação de URLs R2/SigV4;
- URLs sensíveis ficam atrás de tokens curtos no KV;
- o PS4 recebe somente o endereço de transporte do Worker.

## Estado

**3.4.3 + Worker v11.2: validado em PS4 real e promovido a baseline estável.**
