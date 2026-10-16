#!/data/data/com.termux/files/usr/bin/bash
# Auditoria Termux ARM64 do APK 3.4.3 do próprio usuário.
# Somente leitura do APK; NÃO compila, re-assina ou instala nenhuma versão.
set -Eeuo pipefail

EXPECTED_SHA='a251efb5fbdcc48acf402d0d54ed2a91b21fec058f563436a54f6b3ff5ae526e'
EXPECTED_CERT='79BE2ED871A39FB5B366933DD690C54E351AC9EE327D7190677CBBCB65DABFA9'
LOGDIR="$HOME/tayson-ps4-344-logs"
mkdir -p "$LOGDIR"
LOG="$LOGDIR/04-apk-preflight.log"
exec > >(tee "$LOG") 2>&1

echo '[TAYSON] Pré-compilação local: ferramentas Android + baseline autêntica.'
echo '[1/5] Verificar Termux ARM64 e SDK C#'
if ! command -v pkg >/dev/null 2>&1; then
  echo 'ERRO: execute isto no aplicativo Termux.' >&2
  exit 2
fi
if [[ "$(uname -m)" != 'aarch64' ]]; then
  echo "AVISO: arquitetura $(uname -m) (não é o alvo ARM64 esperado)."
fi
dotnet --list-sdks
echo '[2/5] Instalar ferramentas Android NATIVAS dos repositórios Termux'
pkg install -y aapt aapt2 zipalign apksigner openjdk-21 liblz4 openssl
for cmd in aapt aapt2 zipalign apksigner java keytool openssl unzip sha256sum; do
  command -v "$cmd" || { echo "ERRO: ferramenta ausente: $cmd"; exit 3; }
done

echo '[3/5] Identificar APK ORIGINAL local'
APK="${1:-}"
if [[ -z "$APK" ]]; then
  DOWNLOADS="$HOME/storage/downloads"
  if [[ -d "$DOWNLOADS" ]]; then
    APK="$(find "$DOWNLOADS" -maxdepth 1 -type f -iname 'PS4-Payload-Generator-3.4.3-TaySon*.apk' -print -quit)"
  fi
fi
if [[ ! -f "$APK" ]]; then
  echo 'ARQUIVO AUSENTE: salve o APK ORIGINAL 3.4.3 em Downloads e execute novamente.'
  echo 'Ou indique explicitamente o caminho: bash 02-termux-apk-preflight.sh "/caminho/arquivo.apk"'
  exit 4
fi
echo "Nome: $(basename "$APK")"
SIZE=$(wc -c < "$APK")
echo "Bytes: $SIZE"
if [[ "$SIZE" != '37237710' ]]; then
  echo 'ERRO: tamanho divergente da baseline 3.4.3; não alterar o APK.' >&2
  exit 5
fi
SHA="$(sha256sum "$APK" | awk '{print $1}')"
if [[ "$SHA" != "$EXPECTED_SHA" ]]; then
  echo 'ERRO: SHA-256 divergente do APK 3.4.3 de referência.' >&2
  exit 5
fi
unzip -tq "$APK" >/dev/null
echo "PASS: SHA-256 e integridade ZIP confirmados ($SHA)"

echo '[4/5] Auditar manifesto e certificado de assinatura'
aapt dump badging "$APK" | sed -n '1,8p'
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
unzip -p "$APK" META-INF/TAYSON-T.RSA > "$TMP/signer.p7b"
openssl pkcs7 -inform DER -in "$TMP/signer.p7b" -print_certs -out "$TMP/cert.pem"
CERT="$(openssl x509 -in "$TMP/cert.pem" -noout -fingerprint -sha256 | sed 's/.*=//' | tr -d ':\r\n' | tr '[:lower:]' '[:upper:]')"
if [[ "$CERT" != "$EXPECTED_CERT" ]]; then
  echo 'ERRO: o certificado do APK não coincide com o original.' >&2
  exit 6
fi
echo "PASS: certificado público SHA256 $CERT"
apksigner verify --verbose --print-certs "$APK"

echo '[5/5] Resumo dos pré-requisitos (nenhum APK gerado)'
df -h "$HOME" | tail -n 1
echo '[OK] Termux tem SDK .NET, ferramentas Android nativas e baseline íntegra.'
echo 'IMPORTANTE: ainda faltam adaptar Mono.Cecil/ILRepack/assembly store e resolver'
echo 'a assinatura compatível para uma atualização in-place. Não desinstale o 3.4.3.'
echo "Log disponível em: $LOG"
