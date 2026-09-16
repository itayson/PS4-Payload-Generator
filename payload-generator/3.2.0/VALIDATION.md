# Validação 3.3.0 — Tayson PS4 Payload Generator

## Evidência em hardware

Os cinco provedores principais já tiveram o fluxo funcional validado em PS4 real antes da finalização visual da 3.3.0:

- **Mocha:** a query autenticada é preservada e `#tayson.pkg` corrige a validação de extensão do BGFT sem alterar a requisição HTTP;
- **VikingFile:** URL R2 direta retornou `0x80991403`/HTTP 403 no BGFT; Worker normalizou o transporte e criou download real no PS4;
- **FileKeeper:** fluxo direto validado quando o endpoint final já é compatível com Range/BGFT;
- **Rootz:** final R2/SigV4 validado via Universal Transport Worker;
- **AkiraBox:** acesso BGFT direto retornou HTTP 403; Worker v8 + KV `ROUTES` permitiu download real, inclusive com progresso observado no console.

## Invariantes funcionais

A atualização de interface não substitui o núcleo comprovado. Permanecem:

- validação do PKG usando a URL real do provedor;
- segunda verificação antes da geração do ELF;
- preservação byte a byte das queries autenticadas quando necessário;
- detecção estrutural de Cloudflare R2/AWS SigV4;
- `BgftRoutePlanner` separado da URL original;
- Worker v8 somente quando o BGFT necessita da camada de transporte;
- PayloadPatcher baseado no template ELF existente;
- envio ao GoldHEN BinLoader TCP 9090;
- nenhum Receiver/RPI.

## Worker v8

Endpoint padrão:

```text
https://drive.itaysondaniel.workers.dev
```

Binding esperado:

```text
ROUTES
```

O Worker v8 trata:

- VikingFile R2;
- Rootz R2;
- AkiraBox com contexto de sessão;
- `HEAD` do BGFT quando o upstream aceita somente o padrão de GET/Range necessário ao download.

Na 3.3.0 o endpoint é configurável em Ajustes e o valor salvo é usado pelo `BgftRoutePlanner` durante a geração real do ELF.

## Validação da interface 3.3.0

A camada nova preserva os controles reais da `MainPage` e os reposiciona. Isso significa que os handlers originais continuam associados aos mesmos objetos de UI.

Cobertura visual/funcional implementada:

- Home Tayson própria, sem a composição Phoenixx antiga visível;
- status do PS4/IP/BinLoader;
- entrada de link e detecção visual do provedor;
- pacote, capa/metadados e estágio do fluxo;
- ações reais `Validar PKG`, `Gerar ELF`, `Enviar ao PS4` e `Arquivo local`;
- Histórico persistente;
- Worker com `/health` real;
- Ajustes com Worker persistente e teste TCP real do BinLoader 9090;
- textos principais e diálogos gerenciados localizados em PT-BR;
- crédito `Dev. By TaySon`.

## Identidade da release

```text
application label: Tayson PS4 Payload Generator
versionName: 3.3.0
versionCode: 35
package: com.phoenixx.ps4remotepkg
```

O package ID é mantido propositalmente para preservar compatibilidade de instalação e dados do aplicativo. A mudança de identidade é feita pelo label, interface e versão.

## CI

O workflow exige:

- **87 checks** do núcleo aprovados;
- dependency audit sem membros ausentes do runtime empacotado;
- ILRepack/patch concluídos;
- APK alinhado;
- assinatura verificada;
- integridade ZIP aprovada;
- `classes.dex` e `classes2.dex` preservados;
- template ELF final preservado;
- artifact com `SHA256SUMS.txt` e `build-info.json`.

## Assinatura de teste

O CI ainda utiliza uma chave de teste descartável quando não recebe um keystore persistente. Consequentemente, APKs de execuções diferentes podem ter certificados distintos e exigir desinstalação da build anterior.

Uma release distribuída para atualização in-place no futuro deve usar um keystore persistente fornecido de forma segura ao workflow. A chave privada não deve ser adicionada ao repositório.

## Smoke test final recomendado

Antes de mesclar a 3.3.0 na branch universal:

1. instalar o APK final;
2. confirmar label **Tayson PS4 Payload Generator** e Home nova;
3. abrir Histórico, Worker e Ajustes;
4. testar `/health` do Worker;
5. testar a conexão do BinLoader 9090;
6. validar um PKG real;
7. gerar o ELF;
8. enviar ao PS4;
9. confirmar o registro/download no BGFT;
10. fechar e abrir o app para confirmar persistência do Histórico, IP e Worker.

Como o núcleo dos provedores não foi reescrito, esse smoke test final é focado em regressão de integração e na nova composição da interface.
