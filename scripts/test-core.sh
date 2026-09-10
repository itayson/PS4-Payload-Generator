#!/usr/bin/env bash
set -euo pipefail
ROOT="$(pwd)"
TMP="$(mktemp -d)"
cat ci/chunk00 ci/chunk01 ci/chunk02 ci/chunk03 ci/chunk04a ci/chunk04b ci/chunk05a ci/chunk05b | base64 --decode > "$TMP/project.tar.gz"
echo "61515eaea26e789d003ac782392c511c6629c71a23afed6390c0b895b02176df  $TMP/project.tar.gz" | sha256sum -c -
tar -xzf "$TMP/project.tar.gz" -C "$ROOT"
chmod +x scripts/test-core.sh build-apk.sh scripts/bootstrap-build.sh
python3 tools/validate_project.py

python3 - <<'PY'
from pathlib import Path

patcher = Path('app/src/main/java/com/tayson/ps4linksender/payload/ElfPayloadPatcher.kt')
s = patcher.read_text(encoding='utf-8')
if 'import java.net.URI' not in s:
    s = s.replace('import java.nio.charset.StandardCharsets\n', 'import java.nio.charset.StandardCharsets\nimport java.net.URI\n')
if 'val bgftUrl = makeBgftCompatibleUrl(info.finalUrl)' not in s:
    s = s.replace('        patchStringStrict(data, URL_MARKER, 2048, info.finalUrl, "URL")\n', '        val bgftUrl = makeBgftCompatibleUrl(info.finalUrl)\n        patchStringStrict(data, URL_MARKER, 2048, bgftUrl, "URL")\n')
helper = '''\n    /**\n     * BGFT on PS4 requires the textual package URL to appear to end in .pkg.\n     * Signed CDN links often append query parameters after the real .pkg path.\n     * A URI fragment is not transmitted in the HTTP request, so #download.pkg\n     * keeps the signed scheme/host/path/query untouched while satisfying BGFT.\n     */\n    internal fun makeBgftCompatibleUrl(url: String): String {\n        val base = url.substringBefore('#')\n        if (base.lowercase().endsWith(".pkg")) return base\n\n        val uri = try { URI(base) } catch (_: Exception) { null }\n        require(uri != null && (uri.scheme.equals("http", true) || uri.scheme.equals("https", true))) {\n            "URL final precisa ser HTTP/HTTPS"\n        }\n        return "$base#download.pkg"\n    }\n\n'''
if 'internal fun makeBgftCompatibleUrl' not in s:
    s = s.replace('    private fun patchStringStrict', helper + '    private fun patchStringStrict')
patcher.write_text(s, encoding='utf-8')

build = Path('app/build.gradle.kts')
s = build.read_text(encoding='utf-8')
s = s.replace('versionCode = 3', 'versionCode = 4').replace('versionName = "0.3.0"', 'versionName = "0.3.1"')
build.write_text(s, encoding='utf-8')

main = Path('app/src/main/java/com/tayson/ps4linksender/MainActivity.kt')
s = main.read_text(encoding='utf-8').replace('0.3.0 • Rootz', '0.3.1 • Rootz')
main.write_text(s, encoding='utf-8')
PY

grep -q 'versionName = "0.3.1"' app/build.gradle.kts
grep -q '#download.pkg' app/src/main/java/com/tayson/ps4linksender/payload/ElfPayloadPatcher.kt
grep -q '0.3.1 • Rootz' app/src/main/java/com/tayson/ps4linksender/MainActivity.kt
