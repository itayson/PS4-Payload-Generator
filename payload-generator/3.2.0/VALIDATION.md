# Validação 3.3.1 — Tayson PS4 Payload Generator

## Evidência em hardware

Os cinco provedores principais já tiveram o fluxo funcional validado em PS4 real:

- **Mocha:** a query autenticada é preservada e `#tayson.pkg` corrige a validação de extensão do BGFT sem alterar a requisição HTTP;
- **VikingFile:** URL R2 direta retornou `0x80991403`/HTTP 403 no BGFT; Worker normalizou o transporte e criou download real no PS4;
- **FileKeeper:** fluxo direto validado quando o endpoint final já é compatível com Range/BGFT;
- **Rootz:** final R2/SigV4 validado via Universal Transport Worker;
- **AkiraBox:** acesso BGFT direto retornou HTTP 403; Worker v8 + KV `ROUTES` permitiu download real.

O smoke test gravado da interface Tayson confirmou novamente o caminho completo com **AkiraBox**: o Android validou o PKG, o ELF foi enviado ao GoldHEN, o PS4 exibiu a notificação de payload recebido e a nova tarefa apareceu em **Downloads**.

## Ajustes derivados do smoke test

O teste físico mostrou que o núcleo funcional estava correto, mas revelou três resíduos visuais da base original:

- estado `Scanning...` durante a validação;
- estado/botão `Sending...` / `Inject Directly to PS4` durante e após o envio;
- diálogo de sucesso ainda identificando `Tayson 3.2.0`.

A 3.3.1 corrige esses pontos em duas camadas:

1. getters `AppStrings` usados pelos handlers originais são sobrescritos no IL para PT-BR;
2. a camada `UiPolish` normaliza novamente qualquer estado transitório que a `MainPage` tente escrever em inglês após um evento assíncrono.

## Invariantes funcionais

O hotfix 3.3.1 não altera o núcleo comprovado. Permanecem:

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

Na 3.3.1 o endpoint é configurável em Ajustes e o valor salvo é usado pelo `BgftRoutePlanner` durante a geração real do ELF.

## Interface 3.3.1

A camada nova preserva os controles reais da `MainPage` e os reposiciona. Isso significa que os handlers originais continuam associados aos mesmos objetos de UI.

Cobertura visual/funcional:

- Home Tayson própria, sem a composição Phoenixx antiga visível;
- status do PS4/IP/BinLoader;
- entrada de link e detecção visual do provedor;
- pacote, capa/metadados e estágio do fluxo;
- ações reais `Validar PKG`, `Gerar ELF`, `Enviar ao PS4` e `Arquivo local`;
- Histórico persistente;
- Worker com `/health` real;
- Ajustes com Worker persistente e teste TCP real do BinLoader 9090;
- estados principais e transitórios localizados em PT-BR;
- crédito `Dev. By TaySon`.

## Identidade da release

```text
application label: Tayson PS4 Payload Generator
versionName: 3.3.1
versionCode: 36
package: com.phoenixx.ps4remotepkg
```

O package ID é mantido propositalmente para preservar compatibilidade de instalação e dados do aplicativo. A mudança de identidade é feita pelo label, interface e versão.

## CI 3.3.1

GitHub Actions run **#136** concluiu com sucesso:

- **87 checks** do núcleo aprovados;
- dependency audit sem membros ausentes do runtime empacotado;
- ILRepack/patch concluídos;
- APK alinhado;
- assinatura verificada;
- integridade ZIP aprovada;
- `classes.dex` e `classes2.dex` preservados;
- template ELF final preservado;
- artifact `Tayson-PS4-Payload-Generator-3.3.1` criado;
- `versionName 3.3.1` / `versionCode 36` confirmados em `build-info.json`.

## Assinatura de teste

O CI ainda utiliza uma chave de teste descartável quando não recebe um keystore persistente. Consequentemente, APKs de execuções diferentes podem ter certificados distintos e exigir desinstalação da build anterior.

Uma release distribuída para atualização in-place no futuro deve usar um keystore persistente fornecido de forma segura ao workflow. A chave privada não deve ser adicionada ao repositório.

## Estado de validação

O vídeo de hardware confirma o fluxo funcional Android → ELF → BinLoader → BGFT. A 3.3.1 é um hotfix de identidade/localização sobre esse mesmo núcleo e passou novamente por toda a validação automatizada. O próximo teste físico pode ser tratado como confirmação visual da correção das strings, não como revalidação do transporte dos provedores.
