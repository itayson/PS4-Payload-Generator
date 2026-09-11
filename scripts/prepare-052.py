#!/usr/bin/env python3
from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, got {count}")
    return text.replace(old, new, 1)

# This script runs AFTER prepare-051.py. The 0.5.1 hardware photo showed the
# legacy direct-template notification "Registro: 0x80991403", proving that AUTO
# had selected the direct payload for a provider CDN URL that had been classified
# as Provider.DIRECT. 0.5.2 makes AUTO route every non-Mocha package through the
# PS4 Native Bridge; users can still explicitly force DIRETO.

build = Path("app/build.gradle.kts")
b = build.read_text(encoding="utf-8")
b = replace_once(b, "versionCode = 8", "versionCode = 9", "versionCode")
b = replace_once(b, 'versionName = "0.5.1"', 'versionName = "0.5.2"', "versionName")
build.write_text(b, encoding="utf-8")

http = Path("app/src/main/java/com/tayson/ps4linksender/net/Http.kt")
h = http.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.1", "TaysonPS4LinkSender/0.5.2")
http.write_text(h, encoding="utf-8")

patcher = Path("app/src/main/java/com/tayson/ps4linksender/payload/NativeBridgePatcher.kt")
p = patcher.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.1", "TaysonPS4LinkSender/0.5.2")
patcher.write_text(p, encoding="utf-8")

activity = Path("app/src/main/java/com/tayson/ps4linksender/MainActivity.kt")
a = activity.read_text(encoding="utf-8")
a = replace_once(a, "0.5.1 • Native Bridge 403 fix + direto", "0.5.2 • AUTO Native Bridge universal + direto", "UI version")
a = replace_once(
    a,
    "AUTO: Rootz/VikingFile/AkiraBox usam o Native Bridge dentro do próprio PS4; Mocha e links diretos usam BGFT direto. Nenhum byte do PKG passa pelo celular.",
    "AUTO: todo PKG não-Mocha usa o Native Bridge dentro do próprio PS4, inclusive URLs CDN tratadas como 'direto'. Mocha continua BGFT direto. Use DIRETO apenas para forçar BGFT sem bridge. Nenhum byte do PKG passa pelo celular.",
    "AUTO help text",
)
old_rule = '''    private fun shouldNativeBridge(provider: Provider, info: PkgInfo? = pkgInfo): Boolean = when (selectedSendMode()) {
        SendMode.NATIVE_BRIDGE -> true
        SendMode.DIRECT -> false
        SendMode.AUTO -> provider == Provider.ROOTZ || provider == Provider.VIKINGFILE || provider == Provider.AKIRABOX ||
            (provider != Provider.MOCHA && info?.requestContext != null)
    }'''
new_rule = '''    private fun shouldNativeBridge(provider: Provider, info: PkgInfo? = pkgInfo): Boolean = when (selectedSendMode()) {
        SendMode.NATIVE_BRIDGE -> true
        SendMode.DIRECT -> false
        // CDN URLs from Rootz/Viking/Akira often end in .pkg and therefore look
        // like Provider.DIRECT. AUTO must not trust that classification: the
        // real-hardware 0x80991403 test proved BGFT direct is rejected there.
        SendMode.AUTO -> provider != Provider.MOCHA
    }'''
a = replace_once(a, old_rule, new_rule, "AUTO universal Native Bridge rule")

# Persist the user's explicit override; default remains AUTO.
a = replace_once(
    a,
    '''    private fun restoreState() {
        val p = getSharedPreferences("state", MODE_PRIVATE)
        ipInput.setText(p.getString("ps4_ip", ""))
        linkInput.setText(p.getString("last_link", ""))
    }''',
    '''    private fun restoreState() {
        val p = getSharedPreferences("state", MODE_PRIVATE)
        ipInput.setText(p.getString("ps4_ip", ""))
        linkInput.setText(p.getString("last_link", ""))
        modeSpinner.setSelection(p.getInt("send_mode", 0).coerceIn(0, 2))
    }''',
    "restore send mode",
)
a = replace_once(
    a,
    '''        getSharedPreferences("state", MODE_PRIVATE).edit()
            .putString("ps4_ip", ipInput.text.toString().trim())
            .putString("last_link", linkInput.text.toString().trim())
            .apply()''',
    '''        getSharedPreferences("state", MODE_PRIVATE).edit()
            .putString("ps4_ip", ipInput.text.toString().trim())
            .putString("last_link", linkInput.text.toString().trim())
            .putInt("send_mode", modeSpinner.selectedItemPosition)
            .apply()''',
    "persist send mode",
)
activity.write_text(a, encoding="utf-8")

# Give the PS4 an unmistakable startup notification before HTTP/BGFT work.
bridge = Path("ps4bridge/main.c")
s = bridge.read_text(encoding="utf-8")
s = replace_once(s, '#define BRIDGE_VERSION "0.5.1"', '#define BRIDGE_VERSION "0.5.2"', "bridge version")
s = replace_once(
    s,
    '''    if (resolve_modules()) return -1;
    if (!p_notify) return -1;

    int ret = init_http();''',
    '''    if (resolve_modules()) return -1;
    if (!p_notify) return -1;
    notify_text("Tayson Native Bridge 0.5.2 iniciado");

    int ret = init_http();''',
    "bridge startup diagnostic",
)
bridge.write_text(s, encoding="utf-8")

# Update validator after prepare-051.py has already converted it to 0.5.1.
validator = Path("tools/validate_project.py")
v = validator.read_text(encoding="utf-8")
v = replace_once(v, "check('version 0.5.1','versionName = \"0.5.1\"' in build)", "check('version 0.5.2','versionName = \"0.5.2\"' in build)", "validator version")
v = replace_once(v, "check('versionCode 8','versionCode = 8' in build)", "check('versionCode 9','versionCode = 9' in build)", "validator versionCode")
v = replace_once(
    v,
    "check('AUTO captured protected flow uses bridge','SendMode.DIRECT' in main and 'info?.requestContext != null' in main and 'provider != Provider.MOCHA' in main)",
    "check('AUTO universal non-Mocha bridge','SendMode.DIRECT' in main and 'SendMode.AUTO -> provider != Provider.MOCHA' in main)",
    "validator AUTO routing",
)
insert_after = "check('bridge BGFT stage diagnostics','BGFT register:' in bridge and 'BGFT start:' in bridge)"
if insert_after not in v:
    raise SystemExit("validator BGFT diagnostics check not found")
v = v.replace(insert_after, insert_after + "\ncheck('bridge 0.5.2 startup diagnostic','Tayson Native Bridge 0.5.2 iniciado' in bridge)\ncheck('send mode persistence','putInt(\"send_mode\"' in main and 'getInt(\"send_mode\"' in main)", 1)
validator.write_text(v, encoding="utf-8")

print("0.5.2 preparation complete")
