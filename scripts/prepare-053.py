#!/usr/bin/env python3
from pathlib import Path
import re


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, got {count}")
    return text.replace(old, new, 1)

# Runs AFTER prepare-051.py and prepare-052.py.
# 0.5.2 hardware reaches the Native Bridge startup notification but nothing
# after it. That places the stall between startup and localhost-ready, where
# 0.5.2 eagerly initializes SceNet/SceSsl/SceHttp2. 0.5.3:
#   * brings localhost + BGFT up before external HTTP;
#   * initializes HTTP lazily on the first BGFT GET;
#   * initializes NetCtl before NetInit when NetInit is actually needed;
#   * tolerates SceNet already-initialized;
#   * emits a bounded stage notification before/after each network step.

build = Path("app/build.gradle.kts")
b = build.read_text(encoding="utf-8")
b = replace_once(b, "versionCode = 9", "versionCode = 10", "versionCode")
b = replace_once(b, 'versionName = "0.5.2"', 'versionName = "0.5.3"', "versionName")
build.write_text(b, encoding="utf-8")

http = Path("app/src/main/java/com/tayson/ps4linksender/net/Http.kt")
h = http.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.2", "TaysonPS4LinkSender/0.5.3")
http.write_text(h, encoding="utf-8")

patcher = Path("app/src/main/java/com/tayson/ps4linksender/payload/NativeBridgePatcher.kt")
p = patcher.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.2", "TaysonPS4LinkSender/0.5.3")
patcher.write_text(p, encoding="utf-8")

activity = Path("app/src/main/java/com/tayson/ps4linksender/MainActivity.kt")
a = activity.read_text(encoding="utf-8")
a = replace_once(a, "0.5.2 • AUTO Native Bridge universal + direto", "0.5.3 • Native Bridge lazy HTTP + NetCtl", "UI version")
activity.write_text(a, encoding="utf-8")

bridge = Path("ps4bridge/main.c")
s = bridge.read_text(encoding="utf-8")
s = replace_once(s, '#define BRIDGE_VERSION "0.5.2"', '#define BRIDGE_VERSION "0.5.3"', "bridge version")

# NetCtl ABI is part of the known-good initialization sequence used by the
# reference remote package installer.
s = replace_once(
    s,
    "typedef int (*fn_net_init)(void);\n",
    "typedef int (*fn_netctl_init)(void);\ntypedef int (*fn_net_init)(void);\n",
    "NetCtl typedef",
)
s = replace_once(
    s,
    "static fn_net_init p_net_init;\n",
    "static fn_netctl_init p_netctl_init;\nstatic fn_net_init p_net_init;\n",
    "NetCtl pointer",
)
s = replace_once(
    s,
    '    void *net = dlopen("/system/common/lib/libSceNet.sprx", RTLD_LAZY);\n',
    '    void *netctl = dlopen("/system/common/lib/libSceNetCtl.sprx", RTLD_LAZY);\n    void *net = dlopen("/system/common/lib/libSceNet.sprx", RTLD_LAZY);\n',
    "NetCtl module",
)
s = replace_once(
    s,
    '    p_net_init = (fn_net_init)load_symbol(net, "sceNetInit");\n',
    '    p_netctl_init = (fn_netctl_init)load_symbol(netctl, "sceNetCtlInit");\n    p_net_init = (fn_net_init)load_symbol(net, "sceNetInit");\n',
    "NetCtl symbol",
)

old_init = '''static int init_http(void) {
    int ret = p_net_init();
    if (ret < 0) return ret;
    int net_pool = p_net_pool_create("TaysonBridge", NET_POOL_SIZE, 0);
    if (net_pool < 0) return net_pool;
    int ssl_ctx = p_ssl_init(SSL_POOL_SIZE);
    if (ssl_ctx < 0) return ssl_ctx;
    int http_ctx = p_http_init(net_pool, ssl_ctx, HTTP_POOL_SIZE);
    if (http_ctx < 0) return http_ctx;
    g_http_ctx = http_ctx;
    return 0;
}'''
new_init = '''static int init_http(void) {
    if (g_http_ctx >= 0) return 0;

    int ret;
    int net_pool;

    /* GoldHEN payloads can run inside a process whose network stack is already
     * initialized. Probe pool creation first so we do not unnecessarily call
     * sceNetInit on an already-live stack. */
    notify_text("Tayson Bridge: HTTP etapa 1/4 - NetPool");
    net_pool = p_net_pool_create("TaysonBridge", NET_POOL_SIZE, 0);
    if (net_pool < 0) {
        notify_code("Tayson Bridge: NetPool probe", net_pool);

        if (p_netctl_init) {
            notify_text("Tayson Bridge: HTTP etapa 2/4 - NetCtl");
            ret = p_netctl_init();
            if (ret < 0) {
                /* Keep going: the host process may already own NetCtl. NetInit
                 * and the second NetPool attempt are the authoritative tests. */
                notify_code("Tayson Bridge: NetCtl retorno", ret);
            } else {
                notify_text("Tayson Bridge: NetCtl OK");
            }
        }

        notify_text("Tayson Bridge: HTTP etapa 2/4 - NetInit");
        ret = p_net_init();
        if (ret < 0 && (unsigned int)ret != 0x80410004U) {
            notify_code("Tayson Bridge: NetInit falhou", ret);
            return ret;
        }
        if ((unsigned int)ret == 0x80410004U)
            notify_text("Tayson Bridge: NetInit ja ativo");
        else
            notify_text("Tayson Bridge: NetInit OK");

        net_pool = p_net_pool_create("TaysonBridge", NET_POOL_SIZE, 0);
        if (net_pool < 0) {
            notify_code("Tayson Bridge: NetPool falhou", net_pool);
            return net_pool;
        }
    }
    notify_text("Tayson Bridge: NetPool OK");

    notify_text("Tayson Bridge: HTTP etapa 3/4 - SSL");
    int ssl_ctx = p_ssl_init(SSL_POOL_SIZE);
    if (ssl_ctx < 0) {
        notify_code("Tayson Bridge: SSL init falhou", ssl_ctx);
        return ssl_ctx;
    }
    notify_text("Tayson Bridge: SSL OK");

    notify_text("Tayson Bridge: HTTP etapa 4/4 - Http2");
    int http_ctx = p_http_init(net_pool, ssl_ctx, HTTP_POOL_SIZE);
    if (http_ctx < 0) {
        notify_code("Tayson Bridge: Http2 init falhou", http_ctx);
        return http_ctx;
    }
    g_http_ctx = http_ctx;
    notify_text("Tayson Bridge: HTTP pronto");
    return 0;
}'''
s = replace_once(s, old_init, new_init, "lazy HTTP initializer")

old_upstream_start = '''static int upstream_open(const char *range, upstream_request *u) {
    memset(u, 0, sizeof(*u));
    u->tpl = u->conn = u->req = -1;
    const char *ua = g_user_agent[0] ? g_user_agent : "TaysonPS4Bridge/" BRIDGE_VERSION;
    int ret = p_http_template(g_http_ctx, ua, HTTP_VERSION_1_1, 1);'''
new_upstream_start = '''static int upstream_open(const char *range, upstream_request *u) {
    memset(u, 0, sizeof(*u));
    u->tpl = u->conn = u->req = -1;

    int ret = init_http();
    if (ret < 0) return ret;

    const char *ua = g_user_agent[0] ? g_user_agent : "TaysonPS4Bridge/" BRIDGE_VERSION;
    ret = p_http_template(g_http_ctx, ua, HTTP_VERSION_1_1, 1);'''
s = replace_once(s, old_upstream_start, new_upstream_start, "lazy HTTP in upstream_open")

# Mark that BGFT has reached our local server before external HTTP starts.
s = replace_once(
    s,
    '''    if (strcmp(method, "GET") != 0) { reply_error(client, 405, "Method Not Allowed"); return -1; }

    char range_value[128] = {0};''',
    '''    if (strcmp(method, "GET") != 0) { reply_error(client, 405, "Method Not Allowed"); return -1; }

    notify_text("Tayson Bridge: GET localhost recebido");

    char range_value[128] = {0};''',
    "local GET diagnostic",
)

# HTTP is no longer initialized in main. Local listener and BGFT must come up
# first; upstream HTTP is initialized by upstream_open on the first GET.
s = replace_once(
    s,
    '''    int ret = init_http();
    if (ret < 0) { notify_code("Tayson Bridge HTTP init:", ret); return -1; }

    g_io_buf = (uint8_t *)malloc(IO_BUFFER_SIZE);''',
    '''    int ret = 0;
    notify_text("Tayson Bridge 0.5.3: preparando localhost antes do HTTP");

    g_io_buf = (uint8_t *)malloc(IO_BUFFER_SIZE);''',
    "defer HTTP initialization",
)

# Update unmistakable startup marker.
s = replace_once(s, 'notify_text("Tayson Native Bridge 0.5.2 iniciado");', 'notify_text("Tayson Native Bridge 0.5.3 iniciado");', "startup diagnostic")
bridge.write_text(s, encoding="utf-8")

validator = Path("tools/validate_project.py")
v = validator.read_text(encoding="utf-8")
v = replace_once(v, "check('version 0.5.2','versionName = \"0.5.2\"' in build)", "check('version 0.5.3','versionName = \"0.5.3\"' in build)", "validator version")
v = replace_once(v, "check('versionCode 9','versionCode = 9' in build)", "check('versionCode 10','versionCode = 10' in build)", "validator versionCode")
v = replace_once(v, "check('bridge 0.5.2 startup diagnostic','Tayson Native Bridge 0.5.2 iniciado' in bridge)", "check('bridge 0.5.3 startup diagnostic','Tayson Native Bridge 0.5.3 iniciado' in bridge)", "validator startup")
anchor = "check('send mode persistence','putInt(\"send_mode\"' in main and 'getInt(\"send_mode\"' in main)"
if anchor not in v:
    raise SystemExit("validator send mode anchor missing")
v = v.replace(anchor, anchor + "\ncheck('bridge NetCtl init','libSceNetCtl.sprx' in bridge and 'sceNetCtlInit' in bridge)\ncheck('bridge lazy HTTP init','GET localhost recebido' in bridge and bridge.find('register_bgft((uint16_t)g_bridge_port)') < bridge.find('for (;;) sleep(60)'))\ncheck('bridge network stage diagnostics','HTTP etapa 1/4' in bridge and 'HTTP etapa 4/4' in bridge and 'HTTP pronto' in bridge)", 1)
validator.write_text(v, encoding="utf-8")

print("0.5.3 preparation complete")
