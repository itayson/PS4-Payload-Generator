#!/data/data/com.termux/files/usr/bin/bash
# Ensaio isolado: integrar DLL C# candidata ao assembly original, sem tocar no APK.
set -Eeuo pipefail
ROOT="$HOME/tayson-ps4-344-pr25"
REFS="$HOME/tayson-ps4-344-assemblies"
BRANCH="fix/android-3.4.4-historico-identificacao"
EXPECTED_CORE="341c0b442be9be00a4561e08e5808ff02d8a41e6ca9e3aa7460329e2464d8f4a"
SOURCE_PATH="payload-generator/3.2.0/UPDATE"
umask 077
for cmd in dotnet git tar python sha256sum; do
  command -v "$cmd" >/dev/null 2>&1 || { echo "FALTA: $cmd" >&2; exit 2; }
done
[[ -d "$ROOT/.git" ]] || { echo "ERRO: clone GitHub ausente: $ROOT"; exit 2; }
[[ -s "$REFS/PS4RemotePkgMobile.dll" && -s "$REFS/inventory.json" ]] || { echo "ERRO: assemblies da Fase 3 ausentes."; exit 2; }
[[ "$(sha256sum "$REFS/PS4RemotePkgMobile.dll" | awk '{print $1}')" == "$EXPECTED_CORE" ]] || {
  echo "ERRO: DLL principal nao corresponde ao original 3.4.3."; exit 3;
}
shopt -s nullglob
candidates=("$HOME"/tayson-ps4-344-csharp-candidate.*/Tayson.Providers.candidate.dll)
if (( ${#candidates[@]} == 0 )); then
  echo "ERRO: DLL candidata nao encontrada. Execute primeiro o script 04."; exit 3;
fi
if (( $# > 0 )); then CANDIDATE="$1"; else
  CANDIDATE="${candidates[0]}"
  for c in "${candidates[@]}"; do [[ "$c" -nt "$CANDIDATE" ]] && CANDIDATE="$c"; done
fi
[[ -s "$CANDIDATE" ]] || { echo "ERRO: DLL candidata invalida: $CANDIDATE"; exit 3; }
WORK="$(mktemp -d "$HOME/tayson-ps4-344-merge-audit.XXXXXX")"
echo "[TAYSON] Ensaio isolado em: $WORK"
sha256sum "$REFS/PS4RemotePkgMobile.dll" "$CANDIDATE" | tee "$WORK/sha256-origem.txt"

echo '[1/5] Buscar auditor e ferramentas do projeto (sem checkout/reset)...'
git -C "$ROOT" fetch --no-tags origin "$BRANCH"
git -C "$ROOT" archive FETCH_HEAD "$SOURCE_PATH/patcher" "$SOURCE_PATH/tests/UiStateRegression/MergeAudit" | tar -xf - -C "$WORK"
P="$WORK/$SOURCE_PATH"
test -s "$P/patcher/Patcher.csproj"
test -s "$P/tests/UiStateRegression/MergeAudit/MergeAudit.csproj"

echo '[2/5] Compilar auditor Mono.Cecil no .NET nativo do Termux...'
dotnet build "$P/tests/UiStateRegression/MergeAudit/MergeAudit.csproj" -c Release --ignore-failed-sources -p:NuGetAudit=false > "$WORK/dotnet-merge-audit-build.log" 2>&1 || {
  echo 'FALHA compilando auditor. Ultimas linhas:'; tail -n 40 "$WORK/dotnet-merge-audit-build.log"; exit 4; }

echo '[3/5] DETECTAR COLISOES de tipos ANTES de ILRepack...'
set +e
dotnet run -c Release --no-build --project "$P/tests/UiStateRegression/MergeAudit/MergeAudit.csproj" -- "$REFS/PS4RemotePkgMobile.dll" "$CANDIDATE" > "$WORK/duplicate-type-report.log" 2>&1
types_status=$?
set -e
cat "$WORK/duplicate-type-report.log"
if (( types_status != 0 )); then
  echo "AUDITORIA INTERROMPEU MERGE (codigo $types_status). Protecao ativa, sem APK alterado."
  echo "Log: $WORK/duplicate-type-report.log"
  exit "$types_status"
fi

dotnet build "$P/patcher/Patcher.csproj" -c Release --ignore-failed-sources -p:NuGetAudit=false > "$WORK/dotnet-patcher-build.log" 2>&1 || {
  echo 'FALHA compilando patcher. Ultimas linhas:'; tail -n 40 "$WORK/dotnet-patcher-build.log"; exit 4; }

echo '[4/5] Auditar dependencias da DLL candidata...'
set +e
dotnet run -c Release --no-build --project "$P/patcher/Patcher.csproj" -- audit "$CANDIDATE" "$REFS" > "$WORK/dependencies.log" 2>&1
status=$?
set -e
tail -n 25 "$WORK/dependencies.log"
if (( status != 0 )); then
  echo 'BLOQUEADO: existem dependencias nao resolvidas. Nao fundir.'; echo "Log: $WORK/dependencies.log"; exit "$status";
fi

echo '[5/5] Ensaio de fusao ILRepack (NAO modifica APK original)...'
mkdir -p "$WORK/tools"
dotnet tool install dotnet-ilrepack --version 2.0.44 --tool-path "$WORK/tools" --ignore-failed-sources > "$WORK/ilrepack-install.log" 2>&1 || {
  echo 'BLOQUEADO: ILRepack nao pode ser instalado.'; tail -n 35 "$WORK/ilrepack-install.log"; exit 5; }
MERGED="$WORK/MERGE-EXPERIMENTAL-NAO-INSTALAR.dll"
set +e
"$WORK/tools/ilrepack" "/out:$MERGED" "/lib:$REFS" /noRepackRes /ndebug "$REFS/PS4RemotePkgMobile.dll" "$CANDIDATE" > "$WORK/merge.log" 2>&1
status=$?
set -e
if (( status != 0 )) || [[ ! -s "$MERGED" ]]; then
  echo 'BLOQUEADO: fusao falhou.'; tail -n 45 "$WORK/merge.log"; exit 6;
fi
dotnet run -c Release --no-build --project "$P/tests/UiStateRegression/MergeAudit/MergeAudit.csproj" -- "$REFS/PS4RemotePkgMobile.dll" "$CANDIDATE" "$MERGED" | tee "$WORK/metadata-report.txt"
sha256sum "$MERGED" | tee "$WORK/merged-sha256.txt"
echo 'OK: merge de laboratorio concluido (NAO E APK).'
echo "Logs: $WORK"
echo 'Pendente: patcher legado codifica versao 3.4.0; requer atualizacao, assembly store por ABI e assinatura.'
