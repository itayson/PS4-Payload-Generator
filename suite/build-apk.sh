#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
./scripts/test-core.sh

if [[ -x ./gradlew ]]; then
  GRADLE=(./gradlew)
elif command -v gradle >/dev/null 2>&1; then
  GRADLE=(gradle)
else
  echo "Gradle 8.9 não encontrado. Use Android Studio (JDK 17/SDK 35) ou execute scripts/bootstrap-build.sh." >&2
  exit 2
fi

"${GRADLE[@]}" --no-daemon :app:testUniversalDebugUnitTest :app:lintUniversalDebug :app:assembleUniversalDebug
APK="app/build/outputs/apk/universal/debug/app-universal-debug.apk"
[[ -f "$APK" ]] || { echo "APK esperado não foi gerado: $APK" >&2; exit 3; }
mkdir -p dist
cp "$APK" dist/Tayson-PS4-Payload-Lab-1.5.0.apk
sha256sum dist/Tayson-PS4-Payload-Lab-1.5.0.apk > dist/SHA256SUMS.txt
printf 'APK: %s\n' "$(pwd)/dist/Tayson-PS4-Payload-Lab-1.5.0.apk"
