# Tayson 3.5.0 — Remote Package Installer backend

Esta build preserva o fluxo validado da 3.4.3 até a criação da rota final do PKG:

```
Colar link
  -> resolver provedor
  -> validar PKG
  -> extrair metadados/artwork
  -> preparar rota Worker quando necessária
  -> POST para PS4:12800/api/install
  -> Remote Package Installer
  -> BGFT
```

## Requisito no PS4

Abra o **Remote Package Installer** no PS4 e mantenha o aplicativo em primeiro plano durante o envio inicial.

A porta usada pelo app é:

```
12800/TCP
```

O app envia uma única solicitação no formato compatível com o RPI:

```json
{"type":"direct","packages":["https://rota-final-do-pkg.pkg"]}
```

Uma instalação só é considerada aceita quando o RPI responde com um `task_id` BGFT válido.

## O que foi preservado

- provedores e resolução de links da 3.4.3;
- Worker v11.2 e rotas curtas;
- AkiraBox, VikingFile, FileKeeper/dlproxy, Rootz, Mocha e links diretos;
- metadados e artwork;
- histórico;
- IP salvo;
- fluxo de um toque;
- geração/envio ELF manual como fallback.

## Diagnóstico rápido

- **Conexão recusada / porta 12800**: abra o RPI no PS4 e confira o IP.
- **RPI recusou a instalação**: o corpo JSON retornado pelo RPI é exibido como erro.
- **RPI respondeu sem task_id**: a solicitação chegou ao PS4, mas não houve registro BGFT confirmado.
- **Erro de rota/Worker**: ocorre antes do POST ao RPI; valide a URL e o Worker.

O Worker continua responsável apenas por oferecer ao PS4 uma URL HTTP/HTTPS utilizável, preservando Range, tamanho e contexto dos provedores. O RPI/BGFT continua responsável pela tarefa de download e instalação.
