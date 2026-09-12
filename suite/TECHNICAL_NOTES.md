# Atualização 1.5.0 — Payload Lab

Modo experimental de diagnóstico adicionado sobre o Android 1.1.0 e Receiver 1.4.2.
Veja [PAYLOAD_TEST.md](PAYLOAD_TEST.md) para uso, limites e reprodução.
A documentação abaixo é o registro histórico da arquitetura anterior.

# Notas técnicas — arquitetura 1.0

## Por que abandonar o Native Bridge via BinLoader

Os testes 0.5.x mostraram que um ELF executado no processo hospedeiro do BinLoader não tem o mesmo ambiente de um aplicativo PS4 normal: `sceNetCtlInit()` bloqueou em hardware e a inicialização SSL se mostrou dependente do contexto do processo. A versão 1.0 elimina essa variável executando a rede em um homebrew dedicado.

## Receiver

- Controle LAN: TCP `9095`, somente clientes IPv4 privados.
- Proxy BGFT: `127.0.0.1:32112`, não exposto à LAN.
- Até 8 jobs simultaneamente registrados.
- Worker de proxy por conexão BGFT.
- Streaming: 128 KiB por worker, sem arquivo PKG intermediário.
- Preflight: `Range: bytes=0-0`; HTTP 200 ou 206 é aceito.
- Suporte de retomada: o `Range` recebido do BGFT é encaminhado ao CDN.
- Contexto HTTP: UA, Referer, Cookie e headers extras filtrados.
- Cabeçalhos hop-by-hop e sensíveis à assinatura (`Host`, `Content-Length`, `Connection`, `Range`, etc.) não são aceitos no bloco genérico.
- BGFT recebe apenas `http://127.0.0.1:32112/pkg/<job>.pkg` com `ORBIS_BGFT_TASK_OPT_DISABLE_CDN_QUERY_PARAM`.

## Android

O Android continua responsável por:

- identificar provedor;
- seguir redirects/capturar o fluxo oficial quando necessário;
- validar o PKG via HTTP Range;
- ler metadados e `param.sfo` remotamente;
- preservar o contexto HTTP;
- localizar o Receiver pela assinatura `TAYSON/1`;
- enviar jobs e fila.

O Android não hospeda relay e não precisa manter a tela ligada durante o download depois que o Receiver aceitou o job.

## Limites

Uma URL que expire durante um download longo ainda pode falhar quando o CDN exigir uma nova assinatura. CAPTCHA/Turnstile/login também continuam exigindo o fluxo oficial do provedor. A arquitetura não promete compatibilidade com um host que explicitamente bloqueie o próprio PS4 ou altere sua API; ela remove o gargalo do BGFT fazendo a requisição externa em um processo homebrew controlado.
