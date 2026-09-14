# Validação 3.2.0 — BGFT universal

## Evidência física usada

- Mocha: URL `...pkg?token=...` retornava `0x80990033`; mantendo toda a query e acrescentando `#tayson.pkg`, o PS4 retornou `Registro: Já instalado`.
- VikingFile/R2: acesso direto retornava `0x80991403` (HTTP 403). A mesma URL atrás do Worker criou uma tarefa real de download de *Ancestors: The Humankind Odyssey* no PS4.

## Regras implementadas

- análise e revalidação continuam usando a URL real do provedor;
- transformação BGFT ocorre somente depois da segunda validação do PKG;
- query de Mocha não é removida nem reordenada;
- final R2 VikingFile é detectado por host + parâmetros SigV4;
- Android valida formato da data/TTL VikingFile e formato numérico de `expires` no Mocha sem depender de APIs de relógio removidas pelo runtime Xamarin original;
- Worker v6 é a autoridade de expiração por relógio do VikingFile e rejeita URLs expiradas/quase expiradas;
- o próprio Mocha é a autoridade final sobre expiração do token direto;
- Worker recebe a URL R2 exata, gera rota curta com KV quando disponível e fallback inline caso contrário;
- rejeições explícitas de política/expiração do Worker não são contornadas pelo fallback inline;
- o ELF continua sendo enviado pelo fluxo do Payload Generator para o BinLoader TCP 9090;
- nenhum Receiver/RPI foi adicionado.

## CI concluído

GitHub Actions concluiu o build do Payload Generator 3.2.0 com sucesso:

- **87 checks aprovados** em `UPDATE/tests/Program.cs`;
- auditoria de dependências: **0 missing/trimmed members**;
- ILRepack concluído preservando MVID e TypeDef tokens JNI críticos;
- APK alinhado e assinatura verificada em v1/v2/v3;
- integridade ZIP aprovada;
- `classes.dex` e `classes2.dex` originais preservados;
- template ELF final incluído no APK e validado.

SHA-256 do APK produzido nesse build funcional:

```text
0deb061b66bd77ab71f299d02ed7679db7c542a7f99332b78fe613d65cd1baf2
```

## Cobertura dos testes

`UPDATE/tests/Program.cs` cobre:

- detecção de provedores e URL policy;
- preservação de query assinada;
- validação Range/PKG;
- `#tayson.pkg` em URLs `.pkg` com query;
- detecção R2 e validação estrutural dos parâmetros temporais;
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
