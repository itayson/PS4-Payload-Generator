# TaySon Smart Transport — desenvolvimento isolado

Status: v0.1 experimental. **Nao habilitar direct mode em producao.**

## Baseline verificada
- Worker `seek`, versao `v12.2-transport-validation`.
- Codigo integral preservado em `baselines/seek-v12.2-transport-validation.js` nesta branch.
- APK 3.4.3, payload/ELF e BGFT permanecem inalterados.

## Primeira implementacao (artefato v0.1)
O modulo `smart-transport.mjs` foi implementado e testado localmente (15 cenarios). Ele escolhe PROXY por padrao; so considera DIRECT para URLs R2/Rootz/VikingFile assinadas quando as flags `SMART_DIRECT_ENABLED=true` e `SMART_BGFT_REDIRECT_VERIFIED=true` estao presentes e a sonda GET parcial real confirma assinatura PKG, Range e validade suficiente.

A integracao preserva o formato das rotas `/api/routes`, `/api/akira-routes`, `/api/mocha-routes`, `/api/filek-routes` e `/api/viking-routes`. O modulo nao fica ativo simplesmente por existir nesta branch.

O pacote inicial, incluindo modulo, testes e aplicador de patch, foi entregue no ChatGPT sob o nome `TaySon_Smart_Transport_v0.1.zip`.

## Proximas validacoes antes de habilitar DIRECT
1. Integrar o modulo a uma copia verificada da baseline e passar testes em workerd.
2. Publicar em ambiente staging isolado mantendo secrets e KV protegidos.
3. Testar somente Range e cabecalho de PKG de URLs autorizadas e validas.
4. Confirmar em PS4 fisico que BGFT segue HTTP 307 preservando Range, ETag e retomada.
5. Habilitar por provedor somente depois de aprovar regresao e rollback.

Nenhuma alteracao foi feita na publicacao `seek` por este trabalho.