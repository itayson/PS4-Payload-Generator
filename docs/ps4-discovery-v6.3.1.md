# Tayson PS4 v6.3.1 — diagnóstico e descoberta UDP

Relato no Android v6.3: o usuário confirmou que **192.168.0.5 é o IP real** do PS4, porém a descoberta automática falha; o IP foi digitado manualmente. Não existe evidência de sucesso de descoberta, ELF enviado ou BGFT nesta conversa.

## Alterações isoladas
- `Ps4Discovery.IsPs4Response`: interpreta `HTTP/1.1 200 OK` sem distinção entre `Ok` e `OK`. Respostas válidas requerem `host-type:PS4`.
- `Ps4Discovery.DiscoverAsync(knownIp)`: opcionalmente envia uma solicitação `SRCH` **direta ao IPv4 local já configurado**, seguida de broadcast UDP. As consultas são repetidas uma vez dentro de janela máxima de 4 segundos.
- Verificação do destino antes de qualquer consulta dirigida (IPv4 privado RFC 1918, formato estrito). Sem varrer hosts ou portas.
- Mensagens de diagnóstico distinguem falha para abrir socket, falha de transmissão, nenhuma resposta, resposta não-PS4 e resposta PS4 válida, sem registrar dados sensíveis.
- Botões **Localizar PS4** (Início) e **Localizar PS4 automaticamente** (Ajustes) usam o IP preenchido para consulta dirigida e permitem digitar manualmente se nenhuma resposta chegar.
- **Não envia payload, não abre TCP 9090, não altera Worker, GoldHEN nem BGFT.**
- `versionName=3.4.3-staging.6.3.1`, `versionCode=45` para futura atualização direta SOBRE a v6.3 (código 44), desde que seja usado o mesmo certificado original.

## Diagnóstico pendente
1. Android e PS4 ligados na mesma rede Wi-Fi/LAN, com PS4 fora do modo repouso; verificar conexão PS4 Segunda Tela.
2. Comparar descoberta no app oficial *PS4 Second Screen*.
3. Instalar v6.3.1 assinada com o certificado original da v6.3 (SHA-256 `12982c4b170f6d983e1a4dea6fc32c7464f70239bdace4562888152f66ef31ca`), sem desinstalar o aplicativo. **Este APK ainda não foi compilado nem publicado nesta etapa**, pois ambiente de compilação reiniciou e não contém o SDK nem o keystore.
4. Executar testes de protocolo .NET e teste físico Android, inclusive variação `HTTP/1.1 200 Ok`, e registrar diagnóstico do aplicativo.

O código fonte e os testes foram alterados, mas o build Android e a execução física da v6.3.1 **não foram concluídos**. Não afirmar que o problema foi resolvido até obter evidência.
