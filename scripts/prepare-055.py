#!/usr/bin/env python3
from pathlib import Path
import re


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, got {count}")
    return text.replace(old, new, 1)

# Runs AFTER prepare-051.py through prepare-054.py.
# Hardware video on 0.5.4 shows the payload reaches:
#   "Tayson Bridge: NET 1/3 - NetCtl"
# and never returns. Therefore sceNetCtlInit blocks in the GoldHEN payload host.
# 0.5.5 removes NetCtl from the runtime path and starts at sceNetInit, tolerating
# the documented already-initialized result, then proceeds to NetPool/SSL/HTTP.

build = Path("app/build.gradle.kts")
b = build.read_text(encoding="utf-8")
b = replace_once(b, "versionCode = 11", "versionCode = 12", "versionCode")
b = replace_once(b, 'versionName = "0.5.4"', 'versionName = "0.5.5"', "versionName")
build.write_text(b, encoding="utf-8")

http = Path("app/src/main/java/com/tayson/ps4linksender/net/Http.kt")
h = http.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.4", "TaysonPS4LinkSender/0.5.5")
http.write_text(h, encoding="utf-8")

patcher = Path("app/src/main/java/com/tayson/ps4linksender/payload/NativeBridgePatcher.kt")
p = patcher.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.4", "TaysonPS4LinkSender/0.5.5")
patcher.write_text(p, encoding="utf-8")

activity = Path("app/src/main/java/com/tayson/ps4linksender/MainActivity.kt")
a = activity.read_text(encoding="utf-8")
a = replace_once(a, "0.5.4 • PS4 Net + libSceHttp preflight", "0.5.5 • PS4 Net sem NetCtl + preflight CDN", "UI version")
activity.write_text(a, encoding="utf-8")

bridge = Path("ps4bridge/main.c")
s = bridge.read_text(encoding="utf-8")
s = replace_once(s, '#define BRIDGE_VERSION "0.5.4"', '#define BRIDGE_VERSION "0.5.5"', "bridge version")

init_re = re.compile(r"static int init_network\(void\) \{.*?\n\}\n\nstatic int init_http", re.S)
m = init_re.search(s)
if not m:
    raise SystemExit("0.5.4 init_network block not found")
new_init = r'''static int init_network(void) {
    if (g_net_pool >= 0) return 0;

    int ret;

    /* Hardware 0.5.4 proved sceNetCtlInit blocks indefinitely in the process
     * used by GoldHEN BinLoader. Do not call NetCtl from this payload. */
    notify_text("Tayson Bridge: NET 1/2 - NetInit (NetCtl ignorado)");
    ret = p_net_init();
    if (ret < 0 && (unsigned int)ret != 0x80410004U) {
        notify_code("Tayson Bridge: NetInit falhou", ret);
        return ret;
    }
    if ((unsigned int)ret == 0x80410004U)
        notify_text("Tayson Bridge: NetInit ja ativo");
    else
        notify_text("Tayson Bridge: NetInit OK");

    notify_text("Tayson Bridge: NET 2/2 - NetPool");
    int net_pool = p_net_pool_create("TaysonBridge", NET_POOL_SIZE, 0);
    if (net_pool < 0) {
        notify_code("Tayson Bridge: NetPool falhou", net_pool);
        return net_pool;
    }
    g_net_pool = net_pool;
    notify_text("Tayson Bridge: rede PS4 pronta sem NetCtl");
    return 0;
}

static int init_http'''
s = s[:m.start()] + new_init + s[m.end():]

s = replace_once(s, 'notify_text("Tayson Native Bridge 0.5.4 iniciado");', 'notify_text("Tayson Native Bridge 0.5.5 iniciado");', "startup diagnostic")
s = replace_once(s, 'notify_text("Tayson Bridge 0.5.4: inicializando rede base do PS4");', 'notify_text("Tayson Bridge 0.5.5: inicializando rede sem NetCtl");', "main network diagnostic")
bridge.write_text(s, encoding="utf-8")

validator = Path("tools/validate_project.py")
v = validator.read_text(encoding="utf-8")
v = replace_once(v, "check('version 0.5.4','versionName = \"0.5.4\"' in build)", "check('version 0.5.5','versionName = \"0.5.5\"' in build)", "validator version")
v = replace_once(v, "check('versionCode 11','versionCode = 11' in build)", "check('versionCode 12','versionCode = 12' in build)", "validator versionCode")
v = replace_once(v, "check('bridge 0.5.4 startup diagnostic','Tayson Native Bridge 0.5.4 iniciado' in bridge)", "check('bridge 0.5.5 startup diagnostic','Tayson Native Bridge 0.5.5 iniciado' in bridge)", "validator startup")
v = v.replace("check('bridge deterministic network init','NET 1/3 - NetCtl' in bridge and 'NET 3/3 - NetPool' in bridge and 'rede PS4 pronta' in bridge)", "check('bridge deterministic network init','NET 1/2 - NetInit (NetCtl ignorado)' in bridge and 'NET 2/2 - NetPool' in bridge and 'rede PS4 pronta sem NetCtl' in bridge)")
anchor = "check('bridge deterministic network init','NET 1/2 - NetInit (NetCtl ignorado)' in bridge and 'NET 2/2 - NetPool' in bridge and 'rede PS4 pronta sem NetCtl' in bridge)"
if anchor not in v:
    raise SystemExit("0.5.5 validator anchor missing")
v = v.replace(anchor, anchor + "\ncheck('bridge skips blocking NetCtl call','ret = p_netctl_init();' not in bridge)", 1)
validator.write_text(v, encoding="utf-8")

print("0.5.5 preparation complete")
