# Changelog

## 3.4.3 — estável

Data: 2026-09-19

Baseline validado em PS4 real.

### Aplicativo

- `versionName 3.4.3`;
- `versionCode 40`;
- package `com.tayson.ps4payloadgenerator`;
- preserva o template ELF/BGFT conhecido;
- mantém Colar, validação PKG, metadados e artwork;
- preserva a identidade do provedor até a geração do ELF.

### Provedores

- **Mocha:** rota Worker `/m/<token>.pkg`;
- **VikingFile:** rota Worker `/v/<token>.pkg`, mantendo R2/SigV4;
- **FileKeeper:** rota Worker `/f/<token>.pkg`;
- **FileKeeper/dlproxy:** reconhecimento de `dlproxy.uk` e subdomínios;
- **Rootz:** rota R2 compatível `/d/<token>.pkg`;
- **AkiraBox:** rota `/a/<token>.pkg` com contexto temporário no KV.

### Worker v11.2

- binding mantido: `ROUTES -> tayson-payload-routes`;
- normalização FileKeeper para URL pública terminada em `.pkg`;
- allowlist FileKeeper inclui `filekeeper.net` e `dlproxy.uk`;
- URLs reais permanecem no Worker/KV; o PS4 recebe a rota curta.

### Validação

- CI #300 e #301: sucesso;
- 103 checks aprovados;
- APK assinado e validado;
- template ELF SHA-256:
  `ec778c6b39835e6d9ee8681faf99e827b18da0148b8d8e75f0c9d85925f4b1bc`;
- APK SHA-256:
  `a251efb5fbdcc48acf402d0d54ed2a91b21fec058f563436a54f6b3ff5ae526e`;
- aplicativo 3.4.3 + Worker v11.2 confirmados funcionando em hardware real.

## 3.4.2 — prerelease

Correção intermediária para preservar o contexto FileKeeper após a resolução da URL. Substituída pela 3.4.3 após identificar `dlproxy.uk` como host real de transporte.

## 3.4.1 — prerelease

Introdução das rotas unificadas Mocha, FileKeeper e VikingFile no Worker. Substituída pelas correções 3.4.2/3.4.3.

## 3.4.0

Baseline anterior com botão Colar, artwork do PKG, identidade Tayson e núcleo BGFT conhecido.
