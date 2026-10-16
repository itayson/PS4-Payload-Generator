#!/data/data/com.termux/files/usr/bin/bash
# TaySon PS4 Payload Generator — etapa 6, diagnostico de IL em modo somente leitura.
set -Eeuo pipefail
umask 077
ROOT="$HOME/tayson-ps4-344-pr25"
BASE="$HOME/tayson-ps4-344-assemblies/PS4RemotePkgMobile.dll"
EXPECTED="341c0b442be9be00a4561e08e5808ff02d8a41e6ca9e3aa7460329e2464d8f4a"
BRANCH="fix/android-3.4.4-historico-identificacao"
SOURCE="payload-generator/3.2.0/UPDATE/tests/UiStateRegression/MethodAudit"
for cmd in dotnet git tar sha256sum; do
  command -v "$cmd" >/dev/null 2>&1 || { echo "FALTA: $cmd" >&2; exit 2; }
done
[[ -d "$ROOT/.git" ]] || { echo "ERRO: clone Git ausente."; exit 2; }
[[ -s "$BASE" ]] || { echo "ERRO: DLL original nao encontrada."; exit 2; }
ACTUAL="$(sha256sum "$BASE" | awk '{print $1}')"
[[ "$ACTUAL" == "$EXPECTED" ]] || {
  echo "STOP: hash de DLL divergente. Esperado $EXPECTED; obtido $ACTUAL"; exit 3;
}
OUT="$HOME/tayson-ps4-344-method-audit"
mkdir -p "$OUT"
RUN="$(mktemp -d "$OUT/audit.XXXXXX")"
echo "[TAYSON] Auditoria IL somente leitura: $RUN"
echo '[1/3] Buscar apenas fontes do auditor via git archive, sem checkout...'
git -C "$ROOT" fetch --no-tags origin "$BRANCH"
git -C "$ROOT" archive FETCH_HEAD "$SOURCE" | tar -xf - -C "$RUN"
PROJECT="$RUN/$SOURCE/MethodAudit.csproj"
test -s "$PROJECT"
echo '[2/3] Compilar Mono.Cecil no .NET ARM64...'
if ! dotnet build "$PROJECT" -c Release --ignore-failed-sources -p:NuGetAudit=false > "$RUN/compile.log" 2>&1; then
  echo 'ERRO na compilacao do auditor; veja as ultimas 40 linhas:'
  tail -n 40 "$RUN/compile.log"
  exit 4
fi
echo '[3/3] Inspecionar exclusivamente UiStateStore.PackageKey e UiHistoryScreen.Build...'
dotnet run --no-build --configuration Release --project "$PROJECT" -- "$BASE" | tee "$RUN/il-audit.txt"
echo "LOG: $RUN/il-audit.txt"
echo 'Nenhuma DLL ou APK original foi alterado. Nao utilize ILRepack nessa etapa.'
