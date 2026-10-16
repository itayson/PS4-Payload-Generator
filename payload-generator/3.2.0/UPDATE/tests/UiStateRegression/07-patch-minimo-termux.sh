#!/data/data/com.termux/files/usr/bin/bash
# TaySon PS4 Payload Generator — patch de laboratorio em DLL separada.
# NAO modifica o APK, NAO instala, NAO assina, NAO faz merge da DLL inteira.
set -Eeuo pipefail
umask 077

ROOT="$HOME/tayson-ps4-344-pr25"
BASE="$HOME/tayson-ps4-344-assemblies/PS4RemotePkgMobile.dll"
EXPECTED="341c0b442be9be00a4561e08e5808ff02d8a41e6ca9e3aa7460329e2464d8f4a"
BRANCH="fix/android-3.4.4-historico-identificacao"
SOURCE="payload-generator/3.2.0/UPDATE/tests/UiStateRegression/MinimalPatch"

for c in dotnet git tar sha256sum; do
  command -v "$c" >/dev/null || { echo "ERRO: comando nao instalado: $c"; exit 2; }
done
[[ -d "$ROOT/.git" && -s "$BASE" ]] || {
  echo 'ERRO: codigo Git ou DLL original ausente. Preserve os arquivos antigos.'
  exit 3
}
[[ "$(sha256sum "$BASE" | awk '{print $1}')" == "$EXPECTED" ]] || {
  echo 'STOP: DLL original divergente; nenhuma modificacao.'; exit 4;
}

WORK="$(mktemp -d "$HOME/tayson-ps4-344-ilpatch.XXXXXX")"
echo "[TAYSON] Pasta isolada de laboratorio: $WORK"
echo '[1/3] Baixando exclusivamente o projeto de patch IL...'
git -C "$ROOT" fetch --no-tags origin "$BRANCH"
git -C "$ROOT" archive FETCH_HEAD "$SOURCE" | tar -xf - -C "$WORK"
PROJECT="$WORK/$SOURCE/MinimalPatch.csproj"
test -s "$PROJECT"
test -s "$WORK/$SOURCE/Program.cs"

echo '[2/3] Compilando patcher Mono.Cecil (.NET 8 ARM64)...'
if ! dotnet build "$PROJECT" -c Release --ignore-failed-sources -p:NuGetAudit=false > "$WORK/compilacao.log" 2>&1; then
  tail -n 60 "$WORK/compilacao.log"
  echo "FALHA: patcher nao compilou. Log: $WORK/compilacao.log"
  exit 5
fi

echo '[3/3] Modificando apenas copia de laboratorio da DLL .NET...'
OUT="$WORK/PS4RemotePkgMobile.3.4.4.LAB-ONLY.dll"
if dotnet run --no-build -c Release --project "$PROJECT" -- "$BASE" "$OUT" | tee "$WORK/validacao.log"; then
  echo '[OK] Patch de laboratorio passou nos verificadores do Mono.Cecil.'
  echo "DLL experimental: $OUT"
  echo "Log: $WORK/validacao.log"
  echo 'PROIBIDO instalar essa DLL: ainda nao e APK nem release.'
else
  echo '[BLOQUEADO] Validador interrompeu escrita/identidade. Nenhum APK foi alterado.'
  echo "Log: $WORK/validacao.log"
  exit 6
fi
