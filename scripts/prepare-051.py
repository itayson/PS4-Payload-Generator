#!/usr/bin/env python3
from pathlib import Path
import re

# This script runs after scripts/test-core.sh restores the validated 0.5.0 tree.
# It applies the modern SDK compatibility changes already proven in 0.5.0 plus
# the 0.5.1 HTTP/BGFT fixes discovered from the real-hardware 0x80991403 test.


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, got {count}")
    return text.replace(old, new, 1)


main = Path("ps4bridge/main.c")
s = main.read_text(encoding="utf-8")

# Legacy ps4-libjbc headers conflict with the current ps4-payload-sdk pid_t.
legacy = '#include "ps4-libjbc/jailbreak.h"'
compat = r'''/* Minimal ps4-libjbc ABI declarations. Do not include its legacy
 * defs.h here: modern ps4-payload-sdk already defines pid_t/size_t. */
struct jbc_cred {
    uint32_t uid;
    uint32_t ruid;
    uint32_t svuid;
    uint32_t rgid;
    uint32_t svgid;
    uintptr_t prison;
    uintptr_t cdir;
    uintptr_t rdir;
    uintptr_t jdir;
    uint64_t sceProcType;
    uint64_t sonyCred;
    uint64_t sceProcCap;
};
int jbc_get_cred(struct jbc_cred *cred);
int jbc_jailbreak_cred(struct jbc_cred *cred);
int jbc_set_cred(const struct jbc_cred *cred);'''
s = replace_once(s, legacy, compat, "legacy libjbc include")

s = replace_once(s, "#include <netinet/in.h>\n", "#include <netinet/in.h>\n#include <pthread.h>\n", "pthread include")
s = replace_once(s, '#define BRIDGE_VERSION "0.5.0"', '#define BRIDGE_VERSION "0.5.1"', "bridge version")

# Sony's PS4 HTTP API uses 0=OVERWRITE and 1=ADD. 0.5.0 accidentally used ADD.
s = replace_once(s, "#define HTTP_HEADER_OVERWRITE 1", "#define HTTP_HEADER_OVERWRITE 0", "HTTP overwrite mode")

# Keep numeric patch sentinels only in writable data, never folded into code.
s = replace_once(
    s,
    "static uint64_t g_package_size = UINT64_C(0x1122334455667788);",
    "static volatile uint64_t g_package_size = UINT64_C(0x1122334455667788);",
    "size sentinel declaration",
)
s = replace_once(
    s,
    "static uint64_t g_bridge_port = UINT64_C(0xA1B2C3D4E5F6BEEF);",
    "static volatile uint64_t g_bridge_port = UINT64_C(0xA1B2C3D4E5F6BEEF);",
    "port sentinel declaration",
)

sentinel_guard = re.compile(
    r"g_package_size\s*==\s*UINT64_C\(0x1122334455667788\)\s*\|\|\s*"
    r"g_bridge_port\s*==\s*UINT64_C\(0xA1B2C3D4E5F6BEEF\)"
)
s, n = sentinel_guard.subn(
    "g_package_size == 0 || g_bridge_port < 1024 || g_bridge_port > 65535",
    s,
    count=1,
)
if n != 1:
    raise SystemExit(f"numeric sentinel runtime guard replacements={n}")
if s.count("0x1122334455667788") != 1 or s.count("0xA1B2C3D4E5F6BEEF") != 1:
    raise SystemExit("numeric patch sentinels are not unique in C source")

s = replace_once(
    s,
    "static int g_http_ctx = -1;\nstatic uint8_t *g_io_buf;\n",
    "static int g_http_ctx = -1;\nstatic uint8_t *g_io_buf;\nstatic volatile int g_server_running = 1;\n",
    "server state globals",
)

# Check every outgoing header and use real overwrite semantics. This also lets
# captured Accept/Origin/Sec-Fetch headers replace defaults instead of being duplicated.
extra_re = re.compile(r"static void add_extra_headers\(int req\) \{.*?\n\}\n\nstatic int upstream_open", re.S)
match = extra_re.search(s)
if not match:
    raise SystemExit("add_extra_headers block not found")
replacement = r'''static int add_header_checked(int req, const char *name, const char *value) {
    int ret = p_http_add_header(req, name, value, HTTP_HEADER_OVERWRITE);
    if (ret < 0) {
        char msg[192];
        snprintf(msg, sizeof(msg), "Tayson Bridge header %s: 0x%08X", name, (unsigned int)ret);
        notify_text(msg);
    }
    return ret;
}

static int add_extra_headers(int req) {
    if (!g_extra_headers[0]) return 0;
    char block[sizeof(g_extra_headers)];
    memcpy(block, g_extra_headers, sizeof(block));
    block[sizeof(block)-1] = 0;
    char *save = NULL;
    for (char *line = strtok_r(block, "\n", &save); line; line = strtok_r(NULL, "\n", &save)) {
        char *colon = strchr(line, ':');
        if (!colon) continue;
        *colon = 0;
        char *name = line;
        char *value = colon + 1;
        while (*value == ' ' || *value == '\t') value++;
        if (!*name || !*value) continue;
        if (!strcasecmp(name, "Host") || !strcasecmp(name, "Content-Length") || !strcasecmp(name, "Range") ||
            !strcasecmp(name, "Connection") || !strcasecmp(name, "User-Agent") || !strcasecmp(name, "Cookie") ||
            !strcasecmp(name, "Referer") || !strcasecmp(name, "Accept-Encoding")) continue;
        int ret = add_header_checked(req, name, value);
        if (ret < 0) return ret;
    }
    return 0;
}

static int upstream_open'''
s = s[: match.start()] + replacement + s[match.end() :]

old_headers = '''    p_http_add_header(u->req, "Accept", "*/*", HTTP_HEADER_OVERWRITE);
    p_http_add_header(u->req, "Accept-Encoding", "identity", HTTP_HEADER_OVERWRITE);
    if (range && *range) p_http_add_header(u->req, "Range", range, HTTP_HEADER_OVERWRITE);
    if (g_referer[0]) p_http_add_header(u->req, "Referer", g_referer, HTTP_HEADER_OVERWRITE);
    if (g_cookie[0]) p_http_add_header(u->req, "Cookie", g_cookie, HTTP_HEADER_OVERWRITE);
    add_extra_headers(u->req);

    ret = p_http_send(u->req, NULL, 0);'''
new_headers = '''    ret = add_header_checked(u->req, "Accept", "*/*");
    if (ret < 0) { upstream_close(u); return ret; }
    ret = add_header_checked(u->req, "Accept-Encoding", "identity");
    if (ret < 0) { upstream_close(u); return ret; }
    if (range && *range) {
        ret = add_header_checked(u->req, "Range", range);
        if (ret < 0) { upstream_close(u); return ret; }
    }
    if (g_referer[0]) {
        ret = add_header_checked(u->req, "Referer", g_referer);
        if (ret < 0) { upstream_close(u); return ret; }
    }
    if (g_cookie[0]) {
        ret = add_header_checked(u->req, "Cookie", g_cookie);
        if (ret < 0) { upstream_close(u); return ret; }
    }
    ret = add_extra_headers(u->req);
    if (ret < 0) { upstream_close(u); return ret; }

    ret = p_http_send(u->req, NULL, 0);'''
s = replace_once(s, old_headers, new_headers, "upstream header sequence")

# The loopback server must be accepting connections BEFORE BGFT register/start.
thread_marker = "static int register_bgft(uint16_t port) {"
thread_fn = r'''static void *listener_thread_main(void *arg) {
    int listener = (int)(intptr_t)arg;
    while (g_server_running) {
        struct sockaddr_in peer;
        socklen_t peer_len = sizeof(peer);
        int client = accept(listener, (struct sockaddr *)&peer, &peer_len);
        if (client < 0) {
            if (errno == EINTR) continue;
            if (!g_server_running) break;
            notify_text("Tayson Bridge: accept falhou");
            break;
        }
        if (ntohl(peer.sin_addr.s_addr) != INADDR_LOOPBACK) {
            close(client);
            continue;
        }
        handle_client(client);
        close(client);
    }
    return NULL;
}

'''
s = replace_once(s, thread_marker, thread_fn + thread_marker, "listener thread insertion")

old_bgft = '''    ret = p_bgft_register(&params, &task_id);
    if (ret) return ret;
    if (task_id == BGFT_INVALID_TASK_ID) return -1;
    ret = p_bgft_start(task_id);
    return ret;'''
new_bgft = '''    ret = p_bgft_register(&params, &task_id);
    if (ret) {
        notify_code("Tayson Bridge BGFT register:", ret);
        return ret;
    }
    if (task_id == BGFT_INVALID_TASK_ID) {
        notify_text("Tayson Bridge: BGFT retornou task invalida");
        return -1;
    }
    notify_text("Tayson Bridge: BGFT registro OK");
    ret = p_bgft_start(task_id);
    if (ret) {
        notify_code("Tayson Bridge BGFT start:", ret);
        return ret;
    }
    notify_text("Tayson Bridge: BGFT start OK");
    return 0;'''
s = replace_once(s, old_bgft, new_bgft, "BGFT stage diagnostics")

old_tail = '''    int listener = start_listener();
    if (listener < 0) { notify_text("Tayson Bridge: porta local indisponivel"); return -1; }

    ret = register_bgft((uint16_t)g_bridge_port);
    if (ret) {
        notify_code("Tayson Bridge registro:", ret);
        close(listener);
        return -1;
    }

    char ready[160];
    snprintf(ready, sizeof(ready), "Tayson Native Bridge %s ativo - porta %u", BRIDGE_VERSION, (unsigned)((uint16_t)g_bridge_port));
    notify_text(ready);

    for (;;) {
        struct sockaddr_in peer;
        socklen_t peer_len = sizeof(peer);
        int client = accept(listener, (struct sockaddr *)&peer, &peer_len);
        if (client < 0) {
            if (errno == EINTR) continue;
            break;
        }
        /* Only loopback may consume the package stream. */
        if (ntohl(peer.sin_addr.s_addr) != INADDR_LOOPBACK) {
            close(client);
            continue;
        }
        handle_client(client);
        close(client);
    }

    close(listener);
    return 0;'''
new_tail = '''    int listener = start_listener();
    if (listener < 0) { notify_text("Tayson Bridge: porta local indisponivel"); return -1; }

    pthread_t server_thread;
    int thread_ret = pthread_create(&server_thread, NULL, listener_thread_main, (void *)(intptr_t)listener);
    if (thread_ret != 0) {
        notify_code("Tayson Bridge pthread:", thread_ret);
        close(listener);
        return -1;
    }
    pthread_detach(server_thread);

    char local_ready[160];
    snprintf(local_ready, sizeof(local_ready), "Tayson Bridge %s: localhost pronto - porta %u", BRIDGE_VERSION, (unsigned)((uint16_t)g_bridge_port));
    notify_text(local_ready);

    ret = register_bgft((uint16_t)g_bridge_port);
    if (ret) {
        g_server_running = 0;
        close(listener);
        return -1;
    }

    char ready[160];
    snprintf(ready, sizeof(ready), "Tayson Native Bridge %s ativo - porta %u", BRIDGE_VERSION, (unsigned)((uint16_t)g_bridge_port));
    notify_text(ready);

    /* Keep the payload alive while the detached loopback server feeds BGFT. */
    for (;;) sleep(60);
    return 0;'''
s = replace_once(s, old_tail, new_tail, "main listener/BGFT tail")
main.write_text(s, encoding="utf-8")

# Modern SDK wrapper already supplies libc/SceNet/kernel_web; remove duplicate libs
# and omit debug info so numeric sentinels stay unique in the ELF.
mk = Path("ps4bridge/Makefile")
m = mk.read_text(encoding="utf-8")
m = replace_once(m, "LDLIBS := -L$(PS4_PAYLOAD_SDK)/target/lib -lkernel -lSceLibcInternal -lSceNet", "LDLIBS :=", "Makefile LDLIBS")
m = replace_once(m, "CFLAGS := -Wall -Wextra -Wno-unused-parameter -O2 -g -I ps4-libjbc", "CFLAGS := -Wall -Wextra -Wno-unused-parameter -O2 -I ps4-libjbc", "Makefile CFLAGS")
mk.write_text(m, encoding="utf-8")

# Android 0.5.1 metadata and visible effective-mode diagnostics.
build = Path("app/build.gradle.kts")
b = build.read_text(encoding="utf-8")
b = replace_once(b, "versionCode = 7", "versionCode = 8", "versionCode")
b = replace_once(b, 'versionName = "0.5.0"', 'versionName = "0.5.1"', "versionName")
build.write_text(b, encoding="utf-8")

http = Path("app/src/main/java/com/tayson/ps4linksender/net/Http.kt")
h = http.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.0", "TaysonPS4LinkSender/0.5.1")
http.write_text(h, encoding="utf-8")

patcher = Path("app/src/main/java/com/tayson/ps4linksender/payload/NativeBridgePatcher.kt")
p = patcher.read_text(encoding="utf-8").replace("TaysonPS4LinkSender/0.5.0", "TaysonPS4LinkSender/0.5.1")
patcher.write_text(p, encoding="utf-8")

activity = Path("app/src/main/java/com/tayson/ps4linksender/MainActivity.kt")
a = activity.read_text(encoding="utf-8")
a = replace_once(a, "0.5.0 • direto + Native Bridge PS4", "0.5.1 • Native Bridge 403 fix + direto", "UI version")
a = replace_once(
    a,
    '''    private fun shouldNativeBridge(provider: Provider): Boolean = when (selectedSendMode()) {
        SendMode.NATIVE_BRIDGE -> true
        SendMode.DIRECT -> false
        SendMode.AUTO -> provider == Provider.ROOTZ || provider == Provider.VIKINGFILE || provider == Provider.AKIRABOX
    }''',
    '''    private fun shouldNativeBridge(provider: Provider, info: PkgInfo? = pkgInfo): Boolean = when (selectedSendMode()) {
        SendMode.NATIVE_BRIDGE -> true
        SendMode.DIRECT -> false
        SendMode.AUTO -> provider == Provider.ROOTZ || provider == Provider.VIKINGFILE || provider == Provider.AKIRABOX ||
            (provider != Provider.MOCHA && info?.requestContext != null)
    }''',
    "AUTO Native Bridge rule",
)
a = replace_once(a, "val useBridge = shouldNativeBridge(item.provider)", "val useBridge = shouldNativeBridge(item.provider, fresh)", "queue effective mode")
a = replace_once(
    a,
    '            append("Arquivo: ${info.fileName}\\n")',
    '            append("Arquivo: ${info.fileName}\\n")\n            append("Provedor: ${currentProvider.label}\\n")\n            append("Modo efetivo: ${if (shouldNativeBridge(currentProvider, info)) "NATIVE BRIDGE" else "DIRETO"}\\n")',
    "effective mode display",
)
activity.write_text(a, encoding="utf-8")

# Fix the original validator bytes typo, bump version checks, and add regression checks
# for the two defects found after the 0.5.0 hardware test.
validator = Path("tools/validate_project.py")
v = validator.read_text(encoding="utf-8")
v = replace_once(v, "b'TAYSON_BRIDGE_PACKAGE_TYPE_V1','TAYSON_BRIDGE_EXTRA_HEADERS_V1'", "b'TAYSON_BRIDGE_PACKAGE_TYPE_V1',b'TAYSON_BRIDGE_EXTRA_HEADERS_V1'", "validator bytes marker")
v = replace_once(v, "check('version 0.5.0','versionName = \"0.5.0\"' in build)", "check('version 0.5.1','versionName = \"0.5.1\"' in build)", "validator version")
v = replace_once(v, "check('versionCode 7','versionCode = 7' in build)", "check('versionCode 8','versionCode = 8' in build)", "validator versionCode")
v = replace_once(
    v,
    "check('AUTO leaves non-protected providers direct','SendMode.DIRECT' in main and 'provider == Provider.ROOTZ || provider == Provider.VIKINGFILE || provider == Provider.AKIRABOX' in main)",
    "check('AUTO captured protected flow uses bridge','SendMode.DIRECT' in main and 'info?.requestContext != null' in main and 'provider != Provider.MOCHA' in main)",
    "validator AUTO rule",
)
v = replace_once(
    v,
    "check('bridge uses SceHttp2','libSceHttp2.sprx' in bridge and 'sceHttpAddRequestHeader' in bridge and 'sceHttpReadData' in bridge)",
    "check('bridge uses SceHttp2','libSceHttp2.sprx' in bridge and 'sceHttpAddRequestHeader' in bridge and 'sceHttpReadData' in bridge)\ncheck('bridge header overwrite mode','HTTP_HEADER_OVERWRITE 0' in bridge)\ncheck('bridge listener concurrent before BGFT','pthread_create' in bridge and 'listener_thread_main' in bridge and bridge.find('pthread_create') < bridge.find('register_bgft((uint16_t)g_bridge_port)'))\ncheck('bridge BGFT stage diagnostics','BGFT register:' in bridge and 'BGFT start:' in bridge)",
    "validator bridge regression checks",
)
validator.write_text(v, encoding="utf-8")

print("0.5.1 preparation complete")
