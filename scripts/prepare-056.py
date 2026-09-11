#!/usr/bin/env python3
from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, got {count}")
    return text.replace(old, new, 1)

# Runs AFTER prepare-051.py through prepare-055.py.
# Hardware 0.5.5 reached NetInit, NetPool and localhost successfully, then
# sceSslInit returned 0x8095F001 (SCE_SSL_ERROR_BEFORE_INIT). The bridge was
# loading libSceSsl.sprx; PS4 libSceHttp users load libSceSsl2.sprx before
# sceSslInit. 0.5.6 switches the runtime SSL module accordingly.

build = Path("app/build.gradle.kts")
b = build.read_text(encoding="utf-8")
b = replace_once(b, "versionCode = 12", "versionCode = 13", "versionCode")
b = replace_once(b, 'versionName = "0.5.5"', 'versionName = "0.5.6"', "versionName")
build.write_text(b, encoding="utf-8")

http = Path("app/src/main/java/com/tayson/ps4linksender/net/Http.kt")
h = http.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.5", "TaysonPS4LinkSender/0.5.6")
http.write_text(h, encoding="utf-8")

patcher = Path("app/src/main/java/com/tayson/ps4linksender/payload/NativeBridgePatcher.kt")
p = patcher.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.5", "TaysonPS4LinkSender/0.5.6")
patcher.write_text(p, encoding="utf-8")

activity = Path("app/src/main/java/com/tayson/ps4linksender/MainActivity.kt")
a = activity.read_text(encoding="utf-8")
a = replace_once(a, "0.5.5 • PS4 Net sem NetCtl + preflight CDN", "0.5.6 • Ssl2 + PS4 preflight CDN", "UI version")
activity.write_text(a, encoding="utf-8")

bridge = Path("ps4bridge/main.c")
s = bridge.read_text(encoding="utf-8")
s = replace_once(s, '#define BRIDGE_VERSION "0.5.5"', '#define BRIDGE_VERSION "0.5.6"', "bridge version")
s = replace_once(s, 'dlopen("/system/common/lib/libSceSsl.sprx", RTLD_LAZY)', 'dlopen("/system/common/lib/libSceSsl2.sprx", RTLD_LAZY)', "SSL module")
s = replace_once(s, 'notify_text("Tayson Native Bridge 0.5.5 iniciado");', 'notify_text("Tayson Native Bridge 0.5.6 iniciado");', "startup diagnostic")
s = replace_once(s, 'notify_text("Tayson Bridge 0.5.5: inicializando rede sem NetCtl");', 'notify_text("Tayson Bridge 0.5.6: Ssl2 + rede sem NetCtl");', "main diagnostic")
s = replace_once(s, 'notify_text("Tayson Bridge: HTTP 1/2 - SSL");', 'notify_text("Tayson Bridge: HTTP 1/2 - Ssl2");', "SSL stage diagnostic")
s = replace_once(s, 'notify_text("Tayson Bridge: SSL OK");', 'notify_text("Tayson Bridge: Ssl2 OK");', "SSL success diagnostic")
bridge.write_text(s, encoding="utf-8")

validator = Path("tools/validate_project.py")
v = validator.read_text(encoding="utf-8")
v = replace_once(v, "check('version 0.5.5','versionName = \"0.5.5\"' in build)", "check('version 0.5.6','versionName = \"0.5.6\"' in build)", "validator version")
v = replace_once(v, "check('versionCode 12','versionCode = 12' in build)", "check('versionCode 13','versionCode = 13' in build)", "validator versionCode")
v = replace_once(v, "check('bridge 0.5.5 startup diagnostic','Tayson Native Bridge 0.5.5 iniciado' in bridge)", "check('bridge 0.5.6 startup diagnostic','Tayson Native Bridge 0.5.6 iniciado' in bridge)", "validator startup")
anchor = "check('bridge skips blocking NetCtl call','ret = p_netctl_init();' not in bridge)"
if anchor not in v:
    raise SystemExit("0.5.6 validator anchor missing")
v = v.replace(anchor, anchor + "\ncheck('bridge uses Ssl2 runtime module','libSceSsl2.sprx' in bridge and '/system/common/lib/libSceSsl.sprx' not in bridge)", 1)
validator.write_text(v, encoding="utf-8")

print("0.5.6 preparation complete")
