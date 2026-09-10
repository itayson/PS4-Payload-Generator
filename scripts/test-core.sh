#!/usr/bin/env bash
set -euo pipefail
ROOT="$(pwd)"
TMP="$(mktemp -d)"
cat ci/chunk00 ci/chunk01 ci/chunk02 ci/chunk03 ci/chunk04a ci/chunk04b ci/chunk05a ci/chunk05b | base64 --decode > "$TMP/project.tar.gz"
echo "61515eaea26e789d003ac782392c511c6629c71a23afed6390c0b895b02176df  $TMP/project.tar.gz" | sha256sum -c -
tar -xzf "$TMP/project.tar.gz" -C "$ROOT"
chmod +x scripts/test-core.sh build-apk.sh scripts/bootstrap-build.sh
python3 tools/validate_project.py
