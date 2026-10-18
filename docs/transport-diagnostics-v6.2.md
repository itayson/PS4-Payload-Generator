# Tayson PS4 Payload Generator — fase de diagnóstico de transporte v6.2

## Escopo
Branch baseada no commit `35779579c46d772bc4a6344c49dd8e5fe8264ec0` da v6.1 experimental, **anterior às alterações de backup**. Esta fase não contém exportação/importação de arquivos, clipboard de backup nem Android MediaStore/SAF.

Fluxo preservado: Android → Cloudflare Worker de homologação → ELF em memória → GoldHEN BinLoader TCP 9090 → BGFT.

## Alterações
1. `Ps4LanAddressPolicy`: destino permitido apenas em IPv4 RFC 1918 (10/8, 172.16/12, 192.168/16), sem DNS ou portas anexadas; rejeição de loopback, destinos públicos, broadcast convencional, octetos malformados e endereço de rede. Exemplos de PS4 192.168.0.5 e 192.168.0.7 são aceitos. Política local; **não abre conexão para testar 9090**.
2. `UiSettingsScreen`: usa a mesma validação antes de guardar IP.
3. `UiAutoPipeline`: valida IP *antes de gerar ELF*. Quando geração ou envio falha, registra estado local `FALHA ELF` ou `FALHA ENVIO`, sem incluir URLs, cookies ou mensagens excepcionais do provedor. **Não repete transmissão ELF depois de conectar**.
4. `UiWorkerScreen`: botão "Verificar preparo (sem PS4)" checa IP configurado e, se houver Worker de homologação, credencial em memória. Não acessa BinLoader, BGFT, nem afirma que pacote foi instalado.
5. Sem mudanças no Worker de produção `seek`, no Worker staging, no ELF base, no processo BGFT ou nos provedores.

## Resultados de testes
- 33/33: teste isolado da política de endereço local (novo).
- 103/103: suíte original .NET de payload/transport.
- 29/29: suíte de autenticação de homologação.
- Build Android: **compilado com sucesso** em `3.4.3-staging.6.2`, versionCode `43`.
- Inspeção do pacote de compilação: ZIP/CRC íntegro, mensagens da v6.2 presentes, código do backup ausente, SHA-256 do template ELF `ec778c6b39835e6d9ee8681faf99e827b18da0148b8d8e75f0c9d85925f4b1bc`.

## Restrições para distribuição
O certificado original da v6.1 experimental tinha SHA-256 `c693d66ac7e2c95411c730aefa0c9da62a617586e9b3a5d57d0dd90ab8f62ec5`; o certificado de teste disponível agora é **diferente** (`12982c4b170f6d983e1a4dea6fc32c7464f70239bdace4562888152f66ef31ca`). Não assinar a v6.2 com chave substituta alegando compatibilidade com atualização in-place. **Não desinstalar v6.1 nem criar app paralelo** para contornar assinatura. Para um APK instalável sobre a v6.1, a cadeia original de assinatura deve estar disponível de modo seguro (CI/keystore original). Nunca enviar a chave para chat ou commitá-la no GitHub.

## Pendente
- Revisão técnica do comportamento do histórico nos erros reais de TCP.
- Build assinado com o certificado original de v6.1.
- Teste físico da v6.2 com PS4 firmware 12.02; teste distinto para 13.02, sem assumir suporte.
- Telemetria de conclusão BGFT verdadeira não foi implementada e não deve ser inferida de escrita TCP.

Status: experimental, sem merge, sem promoção do Worker produção.
