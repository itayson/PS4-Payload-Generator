# Atualização 1.5.0 — Payload Lab

Modo experimental de diagnóstico adicionado sobre o Android 1.1.0 e Receiver 1.4.2.
Veja [PAYLOAD_TEST.md](PAYLOAD_TEST.md) para uso, limites e reprodução.
A documentação abaixo é o registro histórico da arquitetura anterior.

# Tayson PS4 Link Sender 1.0.1

- Traduz códigos conhecidos do Receiver/BGFT em mensagens claras e acionáveis.
- Mostra orientação específica para falta de espaço (BGFT 0x80990085).
- Explica erros conhecidos de TLS, proxy/stack e inicialização de serviços.
- Explica HTTP 401/403/404/429 e erros 5xx comuns de links temporários.
- Mantém o código técnico na mensagem para diagnóstico.
- A mensagem de sucesso lembra que o PS4 Receiver deve continuar aberto durante o download.
- User-Agent e identificação visual atualizados para 1.0.1.

# Changelog

## 1.0.0

- Mudança de arquitetura: BinLoader/ELF por download removido do fluxo normal.
- Novo homebrew instalável **Tayson PS4 Receiver**.
- Protocolo `TAYSON/1` em TCP 9095 entre Android e PS4.
- Descoberta automática verifica assinatura do Receiver, não apenas porta aberta.
- Proxy exclusivamente localhost em `127.0.0.1:32112` para o BGFT.
- Requisições externas feitas pelo próprio PS4 via SceNet/SceSsl/SceHttp.
- Preflight de 1 byte antes do registro BGFT.
- Forward de Range, User-Agent, Referer, Cookie e headers seguros.
- Streaming em memória; sem Relay Android e sem PKG temporário completo no HDD.
- Jobs/fila suportados pelo Receiver.
- Android atualizado para 1.0.0 / versionCode 14.

## 0.5.x

Linha experimental Native Bridge via GoldHEN BinLoader. Encerrada após testes em hardware evidenciarem limitações de contexto de rede/SSL do processo hospedeiro.
