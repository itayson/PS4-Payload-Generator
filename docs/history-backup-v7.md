# Tayson PS4 Backup Lab v7 — histórico portátil

## Objetivo
Proteger os registros de histórico nas **futuras** desinstalações/reinstalações do aplicativo Android.

## Interface
A aba **Histórico** adiciona **Copiar backup do histórico** e **Restaurar backup copiado**.

1. Copiar backup transforma até 20 registros existentes em uma sequência de texto versionada `TAYSON-HISTORY-BACKUP-V1`, com integridade FNV32 para corrupção acidental.
2. O texto é colocado na área de transferência do Android via JNI, pois a base MAUI reduzida não expõe `Microsoft.Maui.ApplicationModel.DataTransfer`.
3. **Antes de desinstalar**, cole o texto integral em uma nota ou arquivo fora do aplicativo (por exemplo um editor de notas). **Copiar para a área de transferência não é backup permanente.**
4. Depois de reinstalar, copie todo esse texto salvo e toque em **Restaurar backup copiado** na aba Histórico.
5. A importação confirma contagem, valida formato e integridade antes da escrita, preserva registros locais existentes sem duplicar identificadores e limita a 20 entradas. Navegue de Início de volta ao Histórico para atualizar os cartões.

## Dados e segurança
- Metadados: chave de conteúdo do registro, título, Title ID, provedor, tipo, etapa, IP local do PS4 e tamanho.
- Não inclui URL de download, assinatura temporária, cookie, token, cabeçalho `x-api-key`, ELF, PKG ou credencial.
- O texto **não é criptografado** e pode ser lido por qualquer pessoa que o receba. Não publique nem compartilhe.
- Não utiliza Cloudflare nem serviços externos para fazer backup.
- A restauração não declara conclusão BGFT automaticamente; preserva `CONCLUÍDO (MANUAL)` como estado declarado pelo usuário.

## Compatibilidade de assinatura: importante
A assinatura privada que assinou os APKs experimentais v5/v6/v6.1 não estava disponível na nova sessão de compilação. Assim, **o APK v7 NÃO PODE substituir a v6.1**.

Para não apagar o histórico existente nem obrigar desinstalação, o APK v7 é uma variante de testes separada, com:
- package ID: `com.tayson.ps4payloadgenerator.backuplab`
- app label: `Tayson PS4 Backup Lab`
- versionCode: `43`
- versionName: `3.4.3-staging.7`
- certificado de teste diferente da v6.1.

**Não desinstale a v6.1.** Seus registros anteriores continuam nela, mas não são transferidos automaticamente para Backup Lab, pois o Android isola os dados entre aplicativos diferentes.

## Verificações
- 22 testes de exportação, integridade, restauração, limites, deduplicação, dados sensíveis e preservação de estados aprovados.
- 103 testes originais de código C# aprovados.
- 29 testes de autenticação de staging aprovados.
- Build Android bem-sucedido; assinatura APK v1/v2/v3, integridade ZIP e template ELF preservado, confirmados.
- Testes físicos dos botões de copiar/colar/restaurar ainda **pendentes**.

**Não fazer merge nem promover para produção sem teste no Android e estratégia de assinatura persistente.**
