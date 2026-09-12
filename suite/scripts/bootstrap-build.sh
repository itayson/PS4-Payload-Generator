#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CACHE="${HOME}/.cache/tayson-ps4-build"
ANDROID_HOME="${ANDROID_HOME:-$CACHE/android-sdk}"
GRADLE_HOME="$CACHE/gradle-8.9"
TOOLS_ZIP="$CACHE/commandlinetools-linux-15859902_latest.zip"
GRADLE_ZIP="$CACHE/gradle-8.9-bin.zip"
TOOLS_SHA256="4e4c464f145a7512b57d088ac6c278c03c9eea610886b35a5e0804e74eedf583"

command -v java >/dev/null || { echo "JDK 17+ é obrigatório." >&2; exit 2; }
command -v curl >/dev/null || { echo "curl é obrigatório." >&2; exit 2; }
command -v unzip >/dev/null || { echo "unzip é obrigatório." >&2; exit 2; }
mkdir -p "$CACHE" "$ANDROID_HOME/cmdline-tools"

if [[ ! -x "$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager" ]]; then
  curl -fL --retry 3 -o "$TOOLS_ZIP" "https://dl.google.com/android/repository/commandlinetools-linux-15859902_latest.zip"
  echo "$TOOLS_SHA256  $TOOLS_ZIP" | sha256sum -c -
  rm -rf "$ANDROID_HOME/cmdline-tools/latest" "$CACHE/cmdline-tools-extract"
  mkdir -p "$CACHE/cmdline-tools-extract"
  unzip -q "$TOOLS_ZIP" -d "$CACHE/cmdline-tools-extract"
  mv "$CACHE/cmdline-tools-extract/cmdline-tools" "$ANDROID_HOME/cmdline-tools/latest"
fi

export ANDROID_HOME
export ANDROID_SDK_ROOT="$ANDROID_HOME"
yes | "$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager" --licenses >/dev/null || true
"$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager" "platform-tools" "platforms;android-35" "build-tools;35.0.0"

if [[ ! -x "$GRADLE_HOME/bin/gradle" ]]; then
  curl -fL --retry 3 -o "$GRADLE_ZIP" "https://services.gradle.org/distributions/gradle-8.9-bin.zip"
  rm -rf "$GRADLE_HOME"
  unzip -q "$GRADLE_ZIP" -d "$CACHE"
fi

cd "$ROOT"
./scripts/test-core.sh
"$GRADLE_HOME/bin/gradle" --no-daemon :app:testUniversalDebugUnitTest :app:lintUniversalDebug :app:assembleUniversalDebug
mkdir -p dist
cp app/build/outputs/apk/universal/debug/app-universal-debug.apk dist/Tayson-PS4-Payload-Lab-1.5.0.apk
sha256sum dist/Tayson-PS4-Payload-Lab-1.5.0.apk > dist/SHA256SUMS.txt
printf 'Pronto: %s\n' "$ROOT/dist/Tayson-PS4-Payload-Lab-1.5.0.apk"
