# Compatibilidade de links do OrbisDrop

O OrbisDrop aceita URLs HTTP e HTTPS que entreguem diretamente os bytes de um payload PS4 em formato BIN ou ELF.

## Hosts previstos

- `vikingfile.com`
- `rootz.so`
- `akirabox.com/l/...`
- GitHub Blob e GitHub Raw
- outros hosts com download HTTP/HTTPS direto

O cliente segue redirecionamentos HTTP, baixa o arquivo para cache privado, limita o conteúdo a 64 MB, rejeita páginas HTML e valida o conteúdo antes de abrir a conexão TCP com o PS4.

## Destino

Todo envio usa exclusivamente o GoldHEN BinLoader do PlayStation 4 na porta TCP `9090`. Não há suporte a PS5, etaHEN, Remote Package Installer, DPI ou porta `12800`.
