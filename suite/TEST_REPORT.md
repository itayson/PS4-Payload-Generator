# Relatório de testes — 1.0.0

## Já validado em software

- Estrutura Android 1.0 sem dependência do BinLoader.
- Protocolo binário Android → Receiver em big-endian (`TYJ1`, versão 1).
- Filtragem de headers e limites de tamanho.
- Scanner PKG/param.sfo e resolvers preservados da linha anterior.
- Arquitetura Receiver: controle LAN privado, proxy loopback, Range, preflight e BGFT.
- Validador estático do projeto (`scripts/test-core.sh`).

## Pendente antes de chamar 1.0 de validado

- Compilar o Receiver com OpenOrbis e gerar PKG instalável.
- Compilar/lintar o APK 1.0 no CI Android.
- Instalar Receiver em PS4 real com GoldHEN.
- Confirmar inicialização SceNet/SceSsl/SceHttp no processo homebrew.
- Testar handshake do Android em TCP 9095.
- Testar Mocha, Rootz, VikingFile e AkiraBox com URLs novas/autorizadas.
- Confirmar pausa/retomada BGFT e múltiplos jobs.

Nenhum resultado de hardware da linha 0.5.x deve ser interpretado como validação do Receiver 1.0, porque o processo de execução é diferente.
