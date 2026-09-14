# Validação 3.2.0 — BGFT universal

## Evidência física usada

- Mocha: URL `...pkg?token=...` retornava `0x80990033`; mantendo toda a query e acrescentando `#tayson.pkg`, o PS4 retornou `Registro: Já instalado`.
- VikingFile/R2: acesso direto retornava `0x80991403` (HTTP 403). A mesma URL atrás do Worker criou uma tarefa real de download de *Ancestors: The Humankind Odyssey* no PS4.

## Regras implementadas

- análise e revalidação continuam usando a URL real do provedor;
- transformação BGFT ocorre somente depois da segunda validação do PKG;
- query de Mocha não é removida nem reordenada;
- final R2 VikingFile é detectado por host + parâmetros SigV4;
- Worker recebe a URL R2 exata, gera rota curta com KV quando disponível e fallback inline caso contrário;
- expiração é verificada antes de gerar o ELF;
- rejeições explícitas de política/expiração do Worker não são contornadas pelo fallback inline;
- o ELF continua sendo enviado pelo fluxo do Payload Generator para o BinLoader TCP 9090;
- nenhum Receiver/RPI foi adicionado.

## Testes de software esperados

`UPDATE/tests/Program.cs` cobre:

- detecção de provedores e URL policy;
- preservação de query assinada;
- validação Range/PKG;
- `#tayson.pkg` em URLs `.pkg` com query;
- detecção e expiração R2;
- criação de rota curta no Worker;
- fallback inline somente quando a API opcional de rota está indisponível;
- rejeição de fallback quando o Worker recusa explicitamente a URL;
- bypass do Worker para links diretos e Mocha;
- limites do campo URL do template.

## Teste físico recomendado

1. publicar o Worker v6;
2. confirmar `/health`;
3. instalar o APK 3.2.0;
4. testar um link Mocha novo;
5. testar um link VikingFile novo;
6. conferir no PS4 a criação e o avanço real da tarefa em Downloads.
