# TaySon Smart Transport — fase de homologação

**Status:** código integrado e Worker de laboratório publicado. **DIRECT desabilitado** até validação BGFT no console.

## Arquitetura
APK 3.4.3 → rotas antigas do Worker → `servePkg()` com decisão Smart Transport → DIRECT (307) ou PROXY existente → BGFT.

## Componentes na branch
- `baselines/seek-v12.2-transport-validation.js`: cópia integral verificada do código `seek`.
- `src/smart-transport.mjs`: política DIRECT fail-closed por provedor e expiração.
- `scripts/apply-smart-transport.mjs`: integrador com duas âncoras obrigatórias.
- `builds/seek-smart-transport-v0.1.js`: Worker completo gerado do baseline.
- `test/smart-transport.test.mjs`: testes unitários.
- `.github/workflows/smart-transport.yml`: CI Node 22, valida sintaxe, testes e reprodutibilidade.

## Implantação isolada confirmada
- Worker: `tayson-smart-transport-lab` na conta Cloudflare.
- URL esperada: https://tayson-smart-transport-lab.itayson.workers.dev/health
- KV separado: `tayson-smart-transport-lab-routes`.
- Bindings: `ROUTE_API_KEY` secreta; `SMART_DIRECT_ENABLED=false`; `SMART_BGFT_REDIRECT_VERIFIED=false`; `DISABLE_PUBLIC_FALLBACK=true`.
- Produção `seek` e APK 3.4.3 não alterados.
- A API de homologação exige credencial ausente no APK 3.4.3: **não trocar o endereço do app para o laboratório**.

## Resultados em 19/10/2026
- Node 22: **31/31 testes locais PASS**, incluindo intervalos inválidos, expiração, SSRF, assinatura, autenticação contextual, >4 GiB.
- Motor integrado: duas âncoras do código real substituídas e sintaxe validada.
- API Cloudflare aceitou o Worker completo: **HTTP 200**, deploy isolado.
- CI GitHub Actions: workflows novo e legado falharam sem etapas/logs disponíveis; causa **não comprovada**.
- Smoke HTTP externo: bloqueado pela restrição da ferramenta para domínio workers.dev (não implica erro no Worker).
- Teste físico 307 → BGFT / instalação no PS4: **PENDENTE**.

## Gate antes de habilitar DIRECT
1. Confirmar que o laboratório atende `/health` (via navegador Android ou canal não bloqueado).
2. Validar link PKG autorizado com GET parcial e assinatura real; não baixar arquivo inteiro na sonda.
3. Observar se o BGFT aceita 307, mantém Range, inicia, retoma e conclui a instalação.
4. Só então habilitar as duas flags para o provedor testado e planejar rollout canário e rollback.
5. **Nunca promover para `seek` só por sucesso em mocks.**

PR de implementação: https://github.com/itayson/PS4-Payload-Generator/pull/32

## Atualização v0.2 — 19/10/2026

- DIRECT agora depende também de `SMART_DIRECT_PROVIDERS`, uma lista explícita de provedores permitidos (`r2`, `viking`). Sem a lista, permanece PROXY mesmo que as duas flags globais sejam ligadas.
- Código completo atualizado no Worker isolado `tayson-smart-transport-lab` via API de conteúdo, sem alterar os bindings ou segredos.
- O Worker de produção `seek` permanece `v12.2-transport-validation`.
- Testes unitários do pacote v0.2: **34/34 PASS** em Node.js 22.16.0.
- Nova suíte versionada na PR (`test/integrated-worker.test.mjs`): **30/30 PASS** ao executar os arquivos do GitHub em Node.js 20.20.2, incluindo onze asserções no Worker completo com HTTP e KV simulados.
- O modo DIRECT **continua desativado** no laboratório (`SMART_DIRECT_ENABLED=false`, `SMART_BGFT_REDIRECT_VERIFIED=false`).
- CI GitHub Actions: **failure** reportado para o workflow legado e para Smart Transport; o serviço de logs respondeu 404 BlobNotFound, impossibilitando diagnóstico da causa. Os testes externos foram executados separadamente e não equivalem a CI aprovado.
- O HTTP externo real dos Workers foi bloqueado pelo ambiente de consulta. O teste físico do PS4/BGFT (307, Range, retomada, instalação) **não foi realizado**.

**Não habilitar DIRECT ou mesclar o PR até haver evidência de compatibilidade do BGFT e correção do CI.**
