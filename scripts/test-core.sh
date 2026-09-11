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

# Preserve the signed-URL .pkg compatibility fix introduced in 0.3.1.
patcher = Path('app/src/main/java/com/tayson/ps4linksender/payload/ElfPayloadPatcher.kt')
s = patcher.read_text(encoding='utf-8')
if 'import java.net.URI' not in s:
    s = s.replace('import java.nio.charset.StandardCharsets\n', 'import java.nio.charset.StandardCharsets\nimport java.net.URI\n')
if 'val bgftUrl = makeBgftCompatibleUrl(info.finalUrl)' not in s:
    s = s.replace('        patchStringStrict(data, URL_MARKER, 2048, info.finalUrl, "URL")\n', '        val bgftUrl = makeBgftCompatibleUrl(info.finalUrl)\n        patchStringStrict(data, URL_MARKER, 2048, bgftUrl, "URL")\n')
helper = '''\n    /**\n     * BGFT requires the textual URL to appear to end in .pkg. A URI fragment\n     * is not transmitted to the HTTP server, so signed path/query bytes stay\n     * untouched while BGFT sees a .pkg suffix.\n     */\n    internal fun makeBgftCompatibleUrl(url: String): String {\n        val base = url.substringBefore('#')\n        if (base.lowercase().endsWith(".pkg")) return base\n        val uri = try { URI(base) } catch (_: Exception) { null }\n        require(uri != null && (uri.scheme.equals("http", true) || uri.scheme.equals("https", true))) {\n            "URL final precisa ser HTTP/HTTPS"\n        }\n        return "$base#download.pkg"\n    }\n\n'''
if 'internal fun makeBgftCompatibleUrl' not in s:
    s = s.replace('    private fun patchStringStrict', helper + '    private fun patchStringStrict')
patcher.write_text(s, encoding='utf-8')

# 0.3.2: patch the embedded PS4 ELF.
# SceBgftDownloadParam.option lives at rbp-0x88 in this template. Reference
# implementations set SCE_BGFT_TASK_OPT_DISABLE_CDN_QUERY_PARAM (0x10000),
# which prevents BGFT from modifying signed CDN/R2 query strings.
elf_path = Path('app/src/main/assets/template.elf')
data = bytearray(elf_path.read_bytes())
off = 0x46dd
old = bytes.fromhex('c7 85 3c ff ff ff ff ff ff ff')
new = bytes.fromhex('c7 85 78 ff ff ff 00 00 01 00')
if bytes(data[off:off+10]) != old:
    raise SystemExit(f'Unexpected template.elf bytes at 0x{off:x}: {data[off:off+10].hex()}')
data[off:off+10] = new

# Fix unknown-error sentinel so notify_error falls through to the existing
# hexadecimal formatter instead of hiding the useful BGFT error code.
upper = b'DESCONHECIDO\x00'
title = b'Desconhecido\x00'
if data.count(upper) != 1:
    raise SystemExit(f'Expected exactly one uppercase unknown sentinel, got {data.count(upper)}')
data = data.replace(upper, title, 1)
elf_path.write_bytes(data)

# Version metadata / visible app version.
build = Path('app/build.gradle.kts')
s = build.read_text(encoding='utf-8')
s = s.replace('versionCode = 3', 'versionCode = 5')
s = s.replace('versionCode = 4', 'versionCode = 5')
s = s.replace('versionName = "0.3.0"', 'versionName = "0.3.2"')
s = s.replace('versionName = "0.3.1"', 'versionName = "0.3.2"')
build.write_text(s, encoding='utf-8')

main = Path('app/src/main/java/com/tayson/ps4linksender/MainActivity.kt')
s = main.read_text(encoding='utf-8').replace('0.3.0 • Rootz', '0.3.2 • Rootz').replace('0.3.1 • Rootz', '0.3.2 • Rootz')
main.write_text(s, encoding='utf-8')

http = Path('app/src/main/java/com/tayson/ps4linksender/net/Http.kt')
s = http.read_text(encoding='utf-8').replace('TaysonPS4LinkSender/0.3.0', 'TaysonPS4LinkSender/0.3.2').replace('TaysonPS4LinkSender/0.3.1', 'TaysonPS4LinkSender/0.3.2')
http.write_text(s, encoding='utf-8')
PY

# CI assertions specific to 0.3.2.
grep -q 'versionCode = 5' app/build.gradle.kts
grep -q 'versionName = "0.3.2"' app/build.gradle.kts
grep -q '#download.pkg' app/src/main/java/com/tayson/ps4linksender/payload/ElfPayloadPatcher.kt
grep -q '0.3.2 • Rootz' app/src/main/java/com/tayson/ps4linksender/MainActivity.kt
python3 - <<'PY'
from pathlib import Path
p = Path('app/src/main/assets/template.elf').read_bytes()
assert p[0x46dd:0x46e7] == bytes.fromhex('c7 85 78 ff ff ff 00 00 01 00')
assert b'DESCONHECIDO\x00' not in p
assert p.count(b'Desconhecido\x00') >= 1
print('0.3.2 ELF checks: OK')
PY
