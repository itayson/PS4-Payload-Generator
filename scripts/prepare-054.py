#!/usr/bin/env python3
from pathlib import Path
import re


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, got {count}")
    return text.replace(old, new, 1)

# Runs AFTER prepare-051.py, prepare-052.py and prepare-053.py.
# Hardware test on VikingFile/R2: Android scans the signed URL with HTTP 206,
# effective mode is NATIVE BRIDGE, but the PS4 does not start the task.
# 0.5.4 removes the speculative NetPool-before-NetInit probe and follows the
# known-good PS4 order: NetCtl -> NetInit -> NetPool -> SSL -> libSceHttp.
# It also performs a 1-byte upstream preflight on the PS4 before BGFT registration.

build = Path("app/build.gradle.kts")
b = build.read_text(encoding="utf-8")
b = replace_once(b, "versionCode = 10", "versionCode = 11", "versionCode")
b = replace_once(b, 'versionName = "0.5.3"', 'versionName = "0.5.4"', "versionName")
build.write_text(b, encoding="utf-8")

http = Path("app/src/main/java/com/tayson/ps4linksender/net/Http.kt")
h = http.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.3", "TaysonPS4LinkSender/0.5.4")
http.write_text(h, encoding="utf-8")

patcher = Path("app/src/main/java/com/tayson/ps4linksender/payload/NativeBridgePatcher.kt")
p = patcher.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.3", "TaysonPS4LinkSender/0.5.4")
patcher.write_text(p, encoding="utf-8")

activity = Path("app/src/main/java/com/tayson/ps4linksender/MainActivity.kt")
a = activity.read_text(encoding="utf-8")
a = replace_once(a, "0.5.3 • Native Bridge lazy HTTP + NetCtl", "0.5.4 • PS4 Net + libSceHttp preflight", "UI version")
activity.write_text(a, encoding="utf-8")

bridge = Path("ps4bridge/main.c")
s = bridge.read_text(encoding="utf-8")
s = replace_once(s, '#define BRIDGE_VERSION "0.5.3"', '#define BRIDGE_VERSION "0.5.4"', "bridge version")

# Use the same HTTP library family as the reference PS4 remote package installer.
s = replace_once(s, 'dlopen("/system/common/lib/libSceHttp2.sprx", RTLD_LAZY)', 'dlopen("/system/common/lib/libSceHttp.sprx", RTLD_LAZY)', "libSceHttp module")

# Keep one network pool for both local sockets and HTTP.
s = replace_once(
    s,
    "static int g_http_ctx = -1;\nstatic uint8_t *g_io_buf;",
    "static int g_net_pool = -1;\nstatic int g_http_ctx = -1;\nstatic uint8_t *g_io_buf;",
    "network pool global",
)

# Replace 0.5.3's probe-first HTTP init with deterministic PS4 network init.
init_re = re.compile(r"static int init_http\(void\) \{.*?\n\}\n\nstatic int send_all", re.S)
m = init_re.search(s)
if not m:
    raise SystemExit("0.5.3 init_http block not found")
new_init = r'''static int init_network(void) {
    if (g_net_pool >= 0) return 0;

    int ret;
    if (p_netctl_init) {
        notify_text("Tayson Bridge: NET 1/3 - NetCtl");
        ret = p_netctl_init();
        if (ret < 0) {
            /* A payload may run in a process where NetCtl already exists.
             * Continue and let NetInit/NetPool determine whether networking is usable. */
            notify_code("Tayson Bridge: NetCtl retorno", ret);
        } else {
            notify_text("Tayson Bridge: NetCtl OK");
        }
    }

    notify_text("Tayson Bridge: NET 2/3 - NetInit");
    ret = p_net_init();
    if (ret < 0 && (unsigned int)ret != 0x80410004U) {
        notify_code("Tayson Bridge: NetInit falhou", ret);
        return ret;
    }
    if ((unsigned int)ret == 0x80410004U)
        notify_text("Tayson Bridge: NetInit ja ativo");
    else
        notify_text("Tayson Bridge: NetInit OK");

    notify_text("Tayson Bridge: NET 3/3 - NetPool");
    int net_pool = p_net_pool_create("TaysonBridge", NET_POOL_SIZE, 0);
    if (net_pool < 0) {
        notify_code("Tayson Bridge: NetPool falhou", net_pool);
        return net_pool;
    }
    g_net_pool = net_pool;
    notify_text("Tayson Bridge: rede PS4 pronta");
    return 0;
}

static int init_http(void) {
    if (g_http_ctx >= 0) return 0;

    int ret = init_network();
    if (ret < 0) return ret;

    notify_text("Tayson Bridge: HTTP 1/2 - SSL");
    int ssl_ctx = p_ssl_init(SSL_POOL_SIZE);
    if (ssl_ctx < 0) {
        notify_code("Tayson Bridge: SSL init falhou", ssl_ctx);
        return ssl_ctx;
    }
    notify_text("Tayson Bridge: SSL OK");

    notify_text("Tayson Bridge: HTTP 2/2 - libSceHttp");
    int http_ctx = p_http_init(g_net_pool, ssl_ctx, HTTP_POOL_SIZE);
    if (http_ctx < 0) {
        notify_code("Tayson Bridge: libSceHttp init falhou", http_ctx);
        return http_ctx;
    }
    g_http_ctx = http_ctx;
    notify_text("Tayson Bridge: libSceHttp pronto");
    return 0;
}

static int send_all'''
s = s[:m.start()] + new_init + s[m.end():]

# Accept BGFT query suffixes if the service appends one despite option flags,
# and show the first local request before validating it.
s = replace_once(
    s,
    '''    char method[8] = {0};
    char target[512] = {0};
    if (sscanf(header, "%7s %511s", method, target) != 2) { reply_error(client, 400, "Bad Request"); return -1; }
    if (strcmp(target, "/download.pkg") != 0) { reply_error(client, 404, "Not Found"); return -1; }
''',
    '''    char method[8] = {0};
    char target[512] = {0};
    if (sscanf(header, "%7s %511s", method, target) != 2) { reply_error(client, 400, "Bad Request"); return -1; }
    char reqmsg[192];
    snprintf(reqmsg, sizeof(reqmsg), "Tayson Bridge local: %.7s %.150s", method, target);
    notify_text(reqmsg);
    if (strncmp(target, "/download.pkg", 13) != 0 || (target[13] && target[13] != '?')) {
        reply_error(client, 404, "Not Found");
        return -1;
    }
''',
    "local target diagnostics",
)

# 0.5.3 added a GET-only notification; the generic method/target diagnostic supersedes it.
s = s.replace('    notify_text("Tayson Bridge: GET localhost recebido");\n\n', '', 1)

# Add an upstream one-byte preflight. This proves PS4 -> CDN before BGFT is registered.
register_marker = "static int register_bgft(uint16_t port) {"
if register_marker not in s:
    raise SystemExit("register_bgft marker missing")
preflight = r'''static int upstream_preflight(void) {
    notify_text("Tayson Bridge: preflight CDN bytes=0-0");
    upstream_request u;
    int ret = upstream_open("bytes=0-0", &u);
    if (ret < 0) {
        notify_code("Tayson Bridge: preflight request falhou", ret);
        return ret;
    }
    char msg[160];
    snprintf(msg, sizeof(msg), "Tayson Bridge: preflight upstream HTTP %d", u.status);
    notify_text(msg);
    int status = u.status;
    upstream_close(&u);
    if (status != 200 && status != 206) return -1;
    notify_text("Tayson Bridge: CDN acessivel pelo PS4");
    return 0;
}

'''
s = replace_once(s, register_marker, preflight + register_marker, "upstream preflight insertion")

# Network must be initialized before creating the localhost listener. HTTP/TLS
# stays deferred until the upstream preflight, after localhost is already alive.
old_main_stage = '''    int ret = 0;
    notify_text("Tayson Bridge 0.5.3: preparando localhost antes do HTTP");

    g_io_buf = (uint8_t *)malloc(IO_BUFFER_SIZE);'''
new_main_stage = '''    int ret = 0;
    notify_text("Tayson Bridge 0.5.4: inicializando rede base do PS4");
    ret = init_network();
    if (ret < 0) return -1;

    g_io_buf = (uint8_t *)malloc(IO_BUFFER_SIZE);'''
s = replace_once(s, old_main_stage, new_main_stage, "main network stage")

# Run CDN preflight after localhost server thread is ready, before BGFT registration.
local_ready_block = '''    char local_ready[160];
    snprintf(local_ready, sizeof(local_ready), "Tayson Bridge %s: localhost pronto - porta %u", BRIDGE_VERSION, (unsigned)((uint16_t)g_bridge_port));
    notify_text(local_ready);

    ret = register_bgft((uint16_t)g_bridge_port);'''
local_ready_new = '''    char local_ready[160];
    snprintf(local_ready, sizeof(local_ready), "Tayson Bridge %s: localhost pronto - porta %u", BRIDGE_VERSION, (unsigned)((uint16_t)g_bridge_port));
    notify_text(local_ready);

    ret = upstream_preflight();
    if (ret < 0) {
        g_server_running = 0;
        close(listener);
        return -1;
    }

    ret = register_bgft((uint16_t)g_bridge_port);'''
s = replace_once(s, local_ready_block, local_ready_new, "preflight before BGFT")

s = replace_once(s, 'notify_text("Tayson Native Bridge 0.5.3 iniciado");', 'notify_text("Tayson Native Bridge 0.5.4 iniciado");', "startup diagnostic")
bridge.write_text(s, encoding="utf-8")

validator = Path("tools/validate_project.py")
v = validator.read_text(encoding="utf-8")
v = replace_once(v, "check('version 0.5.3','versionName = \"0.5.3\"' in build)", "check('version 0.5.4','versionName = \"0.5.4\"' in build)", "validator version")
v = replace_once(v, "check('versionCode 10','versionCode = 10' in build)", "check('versionCode 11','versionCode = 11' in build)", "validator versionCode")
v = replace_once(v, "check('bridge 0.5.3 startup diagnostic','Tayson Native Bridge 0.5.3 iniciado' in bridge)", "check('bridge 0.5.4 startup diagnostic','Tayson Native Bridge 0.5.4 iniciado' in bridge)", "validator startup")
# Replace checks that specifically described the 0.5.3 lazy probe.
v = v.replace("check('bridge lazy HTTP init','GET localhost recebido' in bridge and bridge.find('register_bgft((uint16_t)g_bridge_port)') < bridge.find('for (;;) sleep(60)'))", "check('bridge deterministic network init','NET 1/3 - NetCtl' in bridge and 'NET 3/3 - NetPool' in bridge and 'rede PS4 pronta' in bridge)")
v = v.replace("check('bridge network stage diagnostics','HTTP etapa 1/4' in bridge and 'HTTP etapa 4/4' in bridge and 'HTTP pronto' in bridge)", "check('bridge libSceHttp preflight','libSceHttp.sprx' in bridge and 'preflight CDN bytes=0-0' in bridge and 'CDN acessivel pelo PS4' in bridge)")
# Extra regressions.
anchor = "check('bridge libSceHttp preflight','libSceHttp.sprx' in bridge and 'preflight CDN bytes=0-0' in bridge and 'CDN acessivel pelo PS4' in bridge)"
if anchor not in v:
    raise SystemExit("0.5.4 validator anchor missing")
v = v.replace(anchor, anchor + "\ncheck('bridge no Http2 module','libSceHttp2.sprx' not in bridge)\ncheck('bridge tolerant local target','strncmp(target, \"/download.pkg\", 13)' in bridge)\ncheck('bridge network before listener',bridge.find('init_network();') < bridge.find('start_listener();'))", 1)
validator.write_text(v, encoding="utf-8")

print("0.5.4 preparation complete")
