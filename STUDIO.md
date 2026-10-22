# Tayson PS4 Studio 1.0.1

Aplicativo independente derivado do código MIT da versão 3.4.3 do Tayson PS4 Payload Generator. Identidade Android: `com.tayson.ps4studio`. O projeto mantém o fluxo Android → Worker → ELF → GoldHEN/BGFT → download no PS4.

Base fixada: tag `v3.4.3`, commit `37eb93b2e919ba14cd6e45437763dad12fe6e9ff`. O APK 3.4.3 original permanece intocado. O nome e a identidade separados permitem testar o Studio sem substituir a instalação original, após a geração e validação do APK.

## Mudanças implementadas

- Central de Transporte na aba Worker: teste HTTP de `/health`, resolução por `/api/link`, validação do PKG e importação da rota para o Início.
- A preparação passa pelo Worker antes de tentar ler o PKG no Android. Isso permite usar a rota do Worker quando o acesso direto à origem pelo celular falha.
- Verificação real de magic PKG, Content ID, tamanho e coerência de leituras Range 0–127 e 127–127, reutilizando o parser da base 3.4.3.
- Cancelamento durante as operações; sair da central cancela a requisição em andamento. A central nunca chama o injetor de payload.
- Respostas do Worker limitadas a 4096 bytes; URL HTTPS `.pkg` da mesma origem; URL de até 2047 bytes. Tokens e query de origem são preservados no corpo de registro.
- Tratamento de HTTP 401/403, 404/405, 410, 429 e falhas de origem sem reproduzir corpos externos ou URLs assinadas nas mensagens de diagnóstico.
- Envio automático opcional, desativado por padrão. O usuário continua tendo acesso à geração e ao envio manual.
- Fluxo visual numerado na página inicial, identificação Studio e configuração do Worker independente do IP do PS4.
- Base privada do Worker oculta na central e no campo de configuração. O histórico herdado continua sem gravar links de download ou cookies.

Não há fila de instalações, monitoramento remoto de progresso BGFT ou comprovação de download concluído. Enviar um ELF não comprova registro BGFT ou instalação. Esses recursos não são apresentados como implementados.

## Configurar o Worker

Na versão 1.0.1, o botão **Colar e salvar Worker** lê o texto diretamente da área de transferência. A validação remove somente aspas/crases externas, delimitadores de bloco de código e marcas invisíveis nas bordas. Espaços e marcas dentro do endereço continuam sendo recusados; caminhos de autenticação escapados são preservados. Mensagens `WORKER_EMPTY`, `WORKER_HTTPS`, `WORKER_CHARACTERS`, `WORKER_QUERY` e similares identificam o motivo sem reproduzir credenciais.

A causa exata da rejeição no aparelho ainda não foi reproduzida; o endereço fornecido foi aceito localmente. As alterações cobrem formatos de cópia e oferecem um caminho de salvamento direto sem depender da edição do campo mascarado.

Em Ajustes, cole a base privada completa do Tayson Universal Proxy fornecida para o aplicativo, incluindo `/app/…`. Nenhuma chave administrativa ou chave privada de produção está embutida no clone. A base fica nas preferências locais do aplicativo; ocultá-la visualmente não significa criptografá-la em repouso.

O Worker precisa implementar `POST <base>/api/link`, recebendo a URL como texto UTF-8 e respondendo com uma URL HTTPS `.pkg` do mesmo host/porta. Isso corresponde à extensão já implementada no Tayson Universal Proxy 1.0.1. A API JSON `/resolve` sozinha não atende esse contrato. Para os fluxos legados, foram preservados os endpoints da base 3.4.3.

Na aba Worker: teste a conexão, cole o link, prepare e valide o PKG, importe a rota. Volte ao Início para validar os metadados, gerar o ELF e enviar ao PS4. A preparação lê somente pequenas amostras; não baixa o PKG inteiro nem calcula seu hash completo.

## Verificação executada neste ambiente

- 103 verificações herdadas de core passaram, usando o template extraído do APK 3.4.3.
- 66 verificações de transporte/configuração passaram com mocks: contratos, assinatura da URL preservada, limites, cancelamento, erros, HTML, arquivo simulado de 8 GiB e formatos de cópia do endereço.
- Todos os arquivos C# do módulo compilaram contra as bibliotecas efetivamente extraídas do APK 3.4.3.
- Auditoria de dependências contra essas bibliotecas: zero membros ausentes/removidos. Foi necessário fornecer ao resolver uma cópia da referência com o nome de identidade observado `merged.dll`; isso ficou apenas no diretório local de ferramentas.
- Os novos scripts Python passaram por compilação sintática.

As verificações de rede são simuladas e a compilação da biblioteca não equivale a um APK. A execução da interface, o ciclo de navegação, permissões Android, coexistência de instalações e o fluxo real PS4 exigem validação física. Resultados de empacotamento e assinatura devem ser consultados no workflow Studio; não são inferidos desses testes.

## Gerar APK de testes

Use o workflow **Build Tayson PS4 Studio (test APK)** em `.github/workflows/studio-build.yml`. Ele utiliza Ubuntu x86_64, Java 17 e as ferramentas fixadas herdadas da base. O workflow tem acesso de leitura ao repositório, gera um artefato de teste e não publica releases nem altera Workers.

O download da base original é validado pelo SHA-256 herdado `a441a8321edba13ebd2597863477c409004e7d4475b9221951bf2991e2feee81`. O APK já modificado da versão 3.4.3 não pode ser usado como se fosse essa base original. A indisponibilidade pública da base interrompe o build; o script não contorna CAPTCHA ou login.

Em Linux x86_64 com Java 17, javac, keytool e liblz4 já disponíveis:

```sh
python3 scripts/fetch-runtime-base.py /tmp/studio-base.apk
python3 scripts/build-studio.py --base-apk /tmp/studio-base.apk --tools /tmp/studio-tools --output out
```

O instalador herdado prepara .NET 8.0.425 e ferramentas Android somente no diretório de ferramentas escolhido. A assinatura de teste é criada em diretório temporário e não serve como identidade estável de atualização entre builds. Para distribuição continuada, é necessário usar e preservar uma chave de assinatura própria. A chave de teste não é enviada como artefato.

Saída esperada **quando o workflow concluir com sucesso**: `out/Tayson-PS4-Studio-1.0.1.apk`, `build-info.json`, `SHA256SUMS.txt` e logs. Não existe URL de download de APK antes desse resultado.

Para compilar apenas o C# contra referências extraídas, inclusive em Alpine ARM64 com SDK compatível:

```sh
python3 scripts/compile-studio.py --sdk /caminho/dotnet --refs /caminho/assemblies --output out
dotnet run --project studio-tests/Studio.Tests.csproj
```

O SDK portátil usado localmente é a variante `linux-musl-arm64`; ver a [documentação oficial de .NET no Alpine](https://learn.microsoft.com/dotnet/core/install/linux-alpine). A empacotação herdada continua exigindo Linux x86_64; os scripts recusam executá-la em ARM64 em vez de tentar rodar ferramentas incompatíveis.

## Licença e atribuição

Mantida a licença MIT de Tayson Daniel Cardoso de Oliveira, em `LICENSE`. O runtime MAUI/JNI e o template são herdados da mesma base utilizada pela versão 3.4.3; o build mantém a verificação dos bytes de DEX e do template entre as fases apropriadas. Este trabalho não redefine ou modifica o gerenciador de transferências do console.

## Instalar esta revisão de testes

A build 1.0.1 usa uma nova assinatura de teste. Para instalar sobre o mesmo identificador Android, será necessário remover somente o Tayson PS4 Studio 1.0.0 antes; isso remove os dados locais desse clone. Não remova o aplicativo original 3.4.3. A configuração do Worker pode ser refeita com o botão Colar e salvar Worker. Uma assinatura persistente continua necessária para atualizações futuras sem reinstalação.
