# 3.5.0 Recovery — núcleo 3.4.0 congelado

Esta linha substitui a branch experimental da 3.5.0 após regressões de BGFT observadas em PS4 real.

## Base

Commit funcional preservado:

`77e8bc5bcc0c97b5fa9f829e142aaecab11acb10`

A branch foi criada diretamente desse commit. O pipeline falha se qualquer um destes componentes divergir da 3.4.0:

- `UPDATE/src/DownloadCore.cs`
- `UPDATE/src/Integration.cs`
- `UPDATE/src/UiAutoPipeline.cs`
- `UPDATE/src/BgftRoutePlanner.cs`
- `UPDATE/scripts/patch_diagnostics.py`
- `UPDATE/cloudflare/universal_transport_worker_v8.js`

## Núcleo preservado

O comportamento volta a ser o mesmo já validado em hardware:

`Colar → validar PKG → gerar ELF em memória → GoldHEN BinLoader 9090 → BGFT`

Não entram nesta linha os experimentos da branch anterior envolvendo:

- troca dinâmica de API BGFT por tipo de PKG;
- alteração da estrutura nativa `SceBgftDownloadParam`;
- preflight adicional da rota BGFT;
- instrumentação nativa de `dlopen/dlsym/Register/Start`;
- wrappers temporários no espaço de `show_credits()`.

O template ELF final deve permanecer:

`ec778c6b39835e6d9ee8681faf99e827b18da0148b8d8e75f0c9d85925f4b1bc`

## Identidade do candidato

Somente a identidade de release é elevada durante o CI:

- versionName: `3.5.0`
- versionCode: `38`
- package: `com.tayson.ps4payloadgenerator`

Nenhuma mudança lógica é aplicada ao núcleo 3.4.0.

## CI

Run #285:

- verificação de núcleo congelado: aprovada;
- 89 checks originais: aprovados;
- template ELF 3.4.0: hash aprovado;
- build e assinatura Android: aprovadas;
- artefato: `Tayson-PS4-Payload-Generator-3.5.0-recovery`.

## Critério de promoção

Antes de qualquer nova feature ser adicionada, este candidato precisa reproduzir no PS4 o comportamento funcional da 3.4.0, especialmente o início real do download BGFT. Novas mudanças serão reaplicadas uma a uma somente depois dessa confirmação.
