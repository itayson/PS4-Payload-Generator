# Tayson PS4 Payload Generator 3.3.0

Aplicativo Android para validar PKGs de PS4, gerar um ELF específico para a tarefa e enviá-lo diretamente ao **GoldHEN BinLoader TCP 9090**. O ELF registra o download no BGFT do próprio PS4.

**Não usa Receiver, Remote Package Installer nem retransmite o PKG inteiro pelo celular.**

## 3.3.0 — atualização de interface

A versão 3.3.0 introduz a interface Tayson completa em tema escuro/neon, preservando os handlers e o fluxo funcional já testados:

- Home reconstruída com cards de conexão, link, provedor, pacote, progresso e payload;
- navegação inferior **Início · Histórico · Worker · Ajustes**;
- Histórico persistente de validações, ELF gerados e payloads enviados;
- tela Worker com teste real de `/health`;
- Ajustes com IP do PS4, teste real do BinLoader 9090 e Worker configurável;
- Worker salvo passa a ser usado de fato pelo `BgftRoutePlanner` na geração do ELF;
- textos de execução e diálogos principais localizados para PT-BR;
- crédito exibido como **Dev. By TaySon**;
- nome do aplicativo no manifesto: **Tayson PS4 Payload Generator**;
- versionName `3.3.0` e versionCode `35`.

A interface reaproveita os controles reais da `MainPage` e os reposiciona em um dashboard novo. Isso mantém os eventos originais de validar PKG, gerar ELF, selecionar arquivo local e enviar ao PS4, evitando uma reimplementação desnecessária da lógica comprovada.

## Fluxo

```text
Link do usuário
    ↓
Provider resolver / navegador assistido
    ↓
Validação do PKG real
Magic • Content ID • tamanho • HTTP Range
    ↓
BGFT Route Planner
    ├── direto
    ├── Mocha + #tayson.pkg
    └── Worker v8 / KV ROUTES
    ↓
PayloadPatcher
    ↓
ELF
    ↓
GoldHEN BinLoader :9090
    ↓
BGFT do PS4
    ↓
Download do PKG
```

## Provedores validados em PS4 real

| Provedor | Transporte BGFT | Estado |
| --- | --- | --- |
| Mocha | URL autenticada preservada + fragmento local `.pkg` | Validado |
| VikingFile | R2/SigV4 via Worker v8 | Validado |
| FileKeeper | Direto quando compatível | Validado |
| Rootz | R2/SigV4 via Worker v8 | Validado |
| AkiraBox | rota temporária Worker v8 com contexto de sessão no KV | Validado |

### Mocha

A URL autenticada é preservada byte por byte. Quando a query impede o BGFT de reconhecer a extensão, o payload adiciona somente:

```text
#tayson.pkg
```

O fragmento não é enviado ao servidor HTTP.

### VikingFile e Rootz

Os finais Cloudflare R2/AWS SigV4 são mantidos intactos e registrados no Worker. O PS4 recebe uma rota curta terminada em `.pkg`. O Worker preserva `GET`/`Range` e pode responder ao comportamento de `HEAD` do BGFT usando uma pequena sondagem `GET Range: bytes=0-0`.

### AkiraBox

Quando o provedor exige contexto de navegador, o aplicativo captura a URL final liberada e valida o PKG com o contexto de sessão. A rota BGFT é registrada no Worker v8; cookies, Referer e User-Agent podem ser mantidos temporariamente no KV `ROUTES`, sem serem gravados dentro do ELF ou no histórico local.

### FileKeeper

Quando o endereço final já funciona com Range e BGFT, ele segue direto, sem proxy desnecessário.

## Universal Transport Worker v8

Worker padrão:

```text
https://drive.itaysondaniel.workers.dev
```

Health esperado:

```text
Tayson Payload Generator Universal Transport Worker v8: OK
```

Binding KV:

```text
ROUTES
```

O namespace do KV pode ter qualquer nome; o binding exposto ao Worker deve se chamar exatamente `ROUTES`.

Na versão 3.3.0 o endpoint pode ser alterado em **Ajustes**. O valor é persistido localmente e utilizado pelo fluxo real de geração do ELF. O app aceita apenas Worker HTTPS sem query string.

## Histórico e privacidade

O histórico persistente guarda somente:

- nome do pacote;
- Title ID;
- tipo;
- tamanho;
- provedor;
- etapa (`VALIDADO`, `ELF GERADO`, `PAYLOAD ENVIADO`);
- IP do PS4 usado na sessão.

Não são persistidos:

- URLs assinadas;
- cookies;
- tokens;
- credenciais de provedor.

## Compatibilidade preservada

A reconstrução continua preservando:

- `classes.dex` e `classes2.dex` originais;
- `assets/template.elf` verificado;
- fluxo JNI/MAUI original necessário ao APK base;
- PayloadPatcher;
- validação real do PKG;
- BinLoader TCP 9090.

O pacote Android permanece `com.phoenixx.ps4remotepkg` para não quebrar a identidade de instalação existente. A identidade visual e o label passam a ser Tayson.

## Assinatura do APK de CI

O workflow gera uma build de teste. Sem um keystore persistente configurado no runner, uma chave descartável é criada. Por isso uma nova execução pode exigir desinstalar uma build Tayson anterior antes de instalar.

Para atualização in-place entre releases futuras, use um keystore persistente fornecido ao GitHub Actions de forma segura. Não adicione uma chave privada ao repositório apenas para estabilizar o CI.

Os hashes exatos de cada build ficam em:

```text
SHA256SUMS.txt
build-info.json
```

## Uso responsável

O projeto é destinado a desenvolvimento, homebrew, testes e gerenciamento de conteúdo que o usuário tenha autorização para utilizar. O repositório não hospeda jogos, licenças, chaves ou conteúdo protegido.
