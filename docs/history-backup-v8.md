# Tayson PS4 Backup Lab v8 — importação e exportação direta .txt

## Objetivo
Exportar o histórico diretamente para um arquivo `.txt` na coleção pública de Downloads do Android, e importar arquivos `.txt` escolhidos pelo seletor de documentos, sem depender da área de transferência.

### Interface na aba Histórico
- **Salvar backup .txt em Downloads**: grava `Tayson-Historico-AAAAMMDD-HHMMSS.txt` na pasta pública `Downloads/Tayson-PS4-Backups/` usando MediaStore.
- **Importar backup .txt**: abre o seletor nativo de arquivos do Android (`FilePicker.Default.PickAsync`), limita o formato e tamanho, verifica o backup e pede confirmação antes de mesclar.
- **Copiar backup do histórico** e **Restaurar backup copiado**: mantidos como alternativas anteriores.

### Segurança e durabilidade
- Os dados são os mesmos metadados históricos aprovados no Backup Lab v7; **não incluem URL assinada, cookie, token, chaves ou ELF**.
- Backup versionado `TAYSON-HISTORY-BACKUP-V1` com checksum **FNV-1a contra corrupção acidental, não autenticidade criptográfica**.
- Arquivos de até 131072 bytes, UTF-8 obrigatório, versão reconhecida, até 20 registros, campos restritos e mesclagem sem duplicados.
- A pasta Downloads é externa ao armazenamento privado do aplicativo e os arquivos devem sobreviver à desinstalação. O usuário ainda deve guardar cópia segura.
- O arquivo `.txt` **NÃO É CRIPTOGRAFADO**, contém metadados e pode conter IP local. Não compartilhar publicamente.
- A exportação pública via MediaStore destina-se a **Android 10+**; a compatibilidade depende do provedor de mídia no dispositivo.
- O backup não acessa Cloudflare, não gera PKG e não envia payload. Worker `seek` inalterado.

### Identidade do APK
- Nome: **Tayson PS4 Backup Lab**
- Package: `com.tayson.ps4payloadgenerator.backuplab`
- VersionName: `3.4.3-staging.8`
- VersionCode: `44`
- Certificado de teste SHA-256: `12982c4b170f6d983e1a4dea6fc32c7464f70239bdace4562888152f66ef31ca` (**igual à v7 Backup Lab**; diferente da v6.1).
- APK SHA-256: `16d97bdf61d205b57a20e5647c345ecf3e1b63bf4c2cda85f690035b637cf397`
- APK: https://backend.composio.dev/api/v3/sl/A635sVaJj2

### Testes
- 103 verificações originais: aprovadas.
- 29 verificações de autenticação staging: aprovadas.
- 30 verificações de backup: aprovadas (inclui UTF-8 BOM, streams, arquivos grandes, formatos inválidos).
- Reconstrução Android: compilou com `/unsafe+` para interop JNI necessário na coleção Downloads.
- Verificação do APK: assinatura v1/v2/v3, integridade ZIP, conteúdo dos botões de arquivo, ELF preservado e hash após publicação: aprovados.
- **Pendente no dispositivo físico:** exportar um .txt para Downloads e importá-lo pelo seletor Android, validar 1 registro sem duplicação e confirmar preservação após reinstalação.
- **Não desinstalar a v6.1 nem o Backup Lab v7.** Instalar a v8 diretamente sobre o Backup Lab v7.

Atenção: o código nativo de MediaStore e o FilePicker foram apenas compilados estaticamente; falhas em tempo de execução (permissões, provedor de armazenamento, seleção) ainda devem ser verificadas fisicamente antes da promoção.
