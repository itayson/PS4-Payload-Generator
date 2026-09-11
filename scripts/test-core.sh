#!/usr/bin/env bash
set -euo pipefail
ROOT="$(pwd)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Restore the last fully validated project bundle used by the CI branch.
cat ci/chunk00 ci/chunk01 ci/chunk02 ci/chunk03 ci/chunk04a ci/chunk04b ci/chunk05a ci/chunk05b | base64 --decode > "$TMP/project.tar.gz"
echo "61515eaea26e789d003ac782392c511c6629c71a23afed6390c0b895b02176df  $TMP/project.tar.gz" | sha256sum -c -
tar -xzf "$TMP/project.tar.gz" -C "$ROOT"
chmod +x scripts/test-core.sh build-apk.sh scripts/bootstrap-build.sh

# Re-apply the 0.3.1/0.3.2 BGFT fixes that live outside the 0.4 overlay.
python3 - <<'PY'
from pathlib import Path

patcher = Path('app/src/main/java/com/tayson/ps4linksender/payload/ElfPayloadPatcher.kt')
s = patcher.read_text(encoding='utf-8')
if 'import java.net.URI' not in s:
    s = s.replace('import java.nio.charset.StandardCharsets\n', 'import java.nio.charset.StandardCharsets\nimport java.net.URI\n')
if 'val bgftUrl = makeBgftCompatibleUrl(info.finalUrl)' not in s:
    s = s.replace('        patchStringStrict(data, URL_MARKER, 2048, info.finalUrl, "URL")\n', '        val bgftUrl = makeBgftCompatibleUrl(info.finalUrl)\n        patchStringStrict(data, URL_MARKER, 2048, bgftUrl, "URL")\n')
helper = '''\n    internal fun makeBgftCompatibleUrl(url: String): String {\n        val base = url.substringBefore('#')\n        if (base.lowercase().endsWith(".pkg")) return base\n        val uri = try { URI(base) } catch (_: Exception) { null }\n        require(uri != null && (uri.scheme.equals("http", true) || uri.scheme.equals("https", true))) {\n            "URL final precisa ser HTTP/HTTPS"\n        }\n        return "$base#download.pkg"\n    }\n\n'''
if 'internal fun makeBgftCompatibleUrl' not in s:
    s = s.replace('    private fun patchStringStrict', helper + '    private fun patchStringStrict')
patcher.write_text(s, encoding='utf-8')

elf_path = Path('app/src/main/assets/template.elf')
data = bytearray(elf_path.read_bytes())
off = 0x46dd
old = bytes.fromhex('c7 85 3c ff ff ff ff ff ff ff')
new = bytes.fromhex('c7 85 78 ff ff ff 00 00 01 00')
if bytes(data[off:off+10]) != old:
    raise SystemExit(f'Unexpected template.elf bytes at 0x{off:x}: {data[off:off+10].hex()}')
data[off:off+10] = new
upper = b'DESCONHECIDO\x00'
title = b'Desconhecido\x00'
if data.count(upper) != 1:
    raise SystemExit(f'Expected one uppercase unknown sentinel, got {data.count(upper)}')
data = data.replace(upper, title, 1)
elf_path.write_bytes(data)
PY

# Apply the 0.4.0 Relay overlay.
cat ci/overlay040_00 ci/overlay040_01 ci/overlay040_02 ci/overlay040_03 | base64 --decode > "$TMP/overlay040.tar.gz"
echo "e207db7db73ea7b328fe4bdac85ec343811d8038b2ee3874b91034e8a8784f16  $TMP/overlay040.tar.gz" | sha256sum -c -
tar -xzf "$TMP/overlay040.tar.gz" -C "$ROOT"

# Android 13+: foreground relay notification support present in the 0.4 source snapshot.
python3 - <<'PY2'
from pathlib import Path
manifest = Path('app/src/main/AndroidManifest.xml')
s = manifest.read_text(encoding='utf-8')
needle = '    <uses-permission android:name="android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE" />\n'
if 'android.permission.POST_NOTIFICATIONS' not in s:
    s = s.replace(needle, needle + '    <uses-permission android:name="android.permission.POST_NOTIFICATIONS" />\n')
manifest.write_text(s, encoding='utf-8')

service = Path('app/src/main/java/com/tayson/ps4linksender/relay/RelayService.kt')
s = service.read_text(encoding='utf-8')
if 'import android.content.pm.PackageManager' not in s:
    s = s.replace('import android.content.pm.ServiceInfo\n', 'import android.content.pm.ServiceInfo\nimport android.content.pm.PackageManager\n')
old = '        val nm = getSystemService(NotificationManager::class.java)\n        nm.notify(NOTIFICATION_ID, notification(txt))\n'
new = '        val nm = getSystemService(NotificationManager::class.java)\n        if (Build.VERSION.SDK_INT < 33 || checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) {\n            nm.notify(NOTIFICATION_ID, notification(txt))\n        }\n'
if old in s:
    s = s.replace(old, new)
service.write_text(s, encoding='utf-8')

main = Path('app/src/main/java/com/tayson/ps4linksender/MainActivity.kt')
s = main.read_text(encoding='utf-8')
if 'import android.Manifest' not in s:
    s = s.replace('package com.tayson.ps4linksender\n\n', 'package com.tayson.ps4linksender\n\nimport android.Manifest\n')
if 'import android.content.pm.PackageManager' not in s:
    s = s.replace('import android.content.Intent\n', 'import android.content.Intent\nimport android.content.pm.PackageManager\n')
if 'import android.os.Build' not in s:
    s = s.replace('import android.os.Bundle\n', 'import android.os.Build\nimport android.os.Bundle\n')
marker = '        super.onCreate(savedInstanceState)\n'
request = marker + '        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {\n            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 4040)\n        }\n'
if 'requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS)' not in s:
    s = s.replace(marker, request, 1)
main.write_text(s, encoding='utf-8')
PY2

# Apply the 0.5.0 Native Bridge source overlay.
cat ci/050build_00 ci/050build_01 ci/050build_02 ci/050build_03 ci/050build_04 ci/050build_05 | base64 --decode > "$TMP/050.patch.gz"
echo "90079c72c60ba84acfad6a2eba30aa9daaf506a2f5c37a73c627ad6d78cab3ec  $TMP/050.patch.gz" | sha256sum -c -
gzip -dc "$TMP/050.patch.gz" > "$TMP/050.patch"
patch -p1 --forward --batch < "$TMP/050.patch"

mkdir -p ps4bridge
cat > ps4bridge/README.md <<'DOC'
# Tayson PS4 Native Bridge

Payload ELF usado pelo Tayson PS4 Link Sender 0.5.0. O Android patcha a URL upstream e os metadados do PKG no ELF. No PS4, o payload abre um servidor **somente em 127.0.0.1**, registra no BGFT uma URL `http://127.0.0.1:<porta>/download.pkg` e retransmite as requisições HTTP Range para o host original usando `libSceHttp2`.

O arquivo PKG não passa pelo Android e não é salvo previamente no HDD. O tráfego de download é Internet -> PS4.

Build CI: ps4-payload-dev/sdk + sleirsgoevy/ps4-libjbc.
DOC

python3 tools/validate_project.py
grep -q 'versionCode = 7' app/build.gradle.kts
grep -q 'versionName = "0.5.0"' app/build.gradle.kts
grep -q 'NATIVE BRIDGE' app/src/main/java/com/tayson/ps4linksender/MainActivity.kt
grep -q 'libSceHttp2.sprx' ps4bridge/main.c
grep -q 'INADDR_LOOPBACK' ps4bridge/main.c
grep -q 'TAYSON_BRIDGE_EXTRA_HEADERS_V1' ps4bridge/main.c
echo '0.5.0 source restore + Native Bridge checks: OK'
