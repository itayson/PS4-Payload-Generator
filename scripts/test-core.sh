#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUILD_DIR="$ROOT/build/core-test"
mkdir -p "$BUILD_DIR"

javac \
  --add-modules jdk.httpserver \
  -encoding UTF-8 \
  -d "$BUILD_DIR" \
  "$ROOT/app/src/main/java/com/akirabox/android/net/ProgressListener.java" \
  "$ROOT/app/src/main/java/com/akirabox/android/net/TransferResult.java" \
  "$ROOT/app/src/main/java/com/akirabox/android/net/UrlUtils.java" \
  "$ROOT/app/src/main/java/com/akirabox/android/net/PayloadFormatValidator.java" \
  "$ROOT/app/src/main/java/com/akirabox/android/net/ConsoleClient.java" \
  "$ROOT/tools/CoreProtocolTest.java"

java --add-modules jdk.httpserver -cp "$BUILD_DIR" CoreProtocolTest
