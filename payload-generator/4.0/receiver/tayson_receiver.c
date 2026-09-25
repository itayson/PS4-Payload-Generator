#include <stddef.h>
#include <stdint.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <unistd.h>

struct jbc_cred
{
    uid_t uid;
    uid_t ruid;
    uid_t svuid;
    gid_t rgid;
    gid_t svgid;
    uintptr_t prison;
    uintptr_t cdir;
    uintptr_t rdir;
    uintptr_t jdir;
    uint64_t sceProcType;
    uint64_t sonyCred;
    uint64_t sceProcCap;
};

int jbc_get_cred(struct jbc_cred*);
int jbc_jailbreak_cred(struct jbc_cred*);
int jbc_set_cred(const struct jbc_cred*);

#define TAYSON_RX_PORT 9091
#define LINE_MAX 1024

void* dlopen(const char*, int);
void* dlsym(void*, const char*);

/* Freestanding payload: provide the tiny libc primitive GCC may emit for
 * aggregate zero-initialization.  Volatile prevents it being folded back into
 * an unresolved builtin memset call. */
void *memset(void *dst, int value, size_t count)
{
    volatile unsigned char *p = (volatile unsigned char*)dst;
    for (size_t i = 0; i < count; i++)
        p[i] = (unsigned char)value;
    return dst;
}

static int (*notify_fn)(int, const char*) = 0;

static size_t tlen(const char *s)
{
    size_t n = 0;
    if (!s) return 0;
    while (s[n]) n++;
    return n;
}

static int write_all(int fd, const void *buf, size_t len)
{
    const char *p = (const char*)buf;
    size_t off = 0;
    while (off < len) {
        ssize_t n = write(fd, p + off, len - off);
        if (n <= 0) return -1;
        off += (size_t)n;
    }
    return 0;
}

static void reply(int fd, const char *s)
{
    write_all(fd, s, tlen(s));
}

static void notify(const char *s)
{
    if (notify_fn) notify_fn(222, s);
}

static int starts(const char *s, const char *prefix)
{
    size_t i = 0;
    while (prefix[i]) {
        if (s[i] != prefix[i]) return 0;
        i++;
    }
    return 1;
}

static int read_line(int fd, char *out, size_t cap)
{
    size_t n = 0;
    while (n + 1 < cap) {
        char c;
        ssize_t r = read(fd, &c, 1);
        if (r <= 0) return -1;
        if (c == '\n') {
            out[n] = 0;
            return (int)n;
        }
        if (c == '\r') continue;
        out[n++] = c;
    }
    out[cap - 1] = 0;
    return -2;
}

static void hex32(int value, char *out)
{
    static const char d[] = "0123456789ABCDEF";
    uint32_t v = (uint32_t)value;
    out[0] = '0'; out[1] = 'x';
    for (int i = 0; i < 8; i++)
        out[2 + i] = d[(v >> (28 - i * 4)) & 15];
    out[10] = 0;
}

static void reply_code(int fd, const char *prefix, int code)
{
    char msg[96];
    char hx[11];
    size_t p = 0;
    hex32(code, hx);
    while (*prefix && p + 1 < sizeof(msg)) msg[p++] = *prefix++;
    if (p + 1 < sizeof(msg)) msg[p++] = ' ';
    for (int i = 0; hx[i] && p + 2 < sizeof(msg); i++) msg[p++] = hx[i];
    msg[p++] = '\n';
    msg[p] = 0;
    reply(fd, msg);
}

static void notify_code(const char *prefix, int code)
{
    char msg[96];
    char hx[11];
    size_t p = 0;
    hex32(code, hx);
    while (*prefix && p + 1 < sizeof(msg)) msg[p++] = *prefix++;
    if (p + 1 < sizeof(msg)) msg[p++] = ' ';
    for (int i = 0; hx[i] && p + 1 < sizeof(msg); i++) msg[p++] = hx[i];
    msg[p] = 0;
    notify(msg);
}

/*
 * PS4 AppInstUtil ABI probe.
 *
 * These layouts are the public reverse-engineered AppInstUtil structures.
 * This alpha intentionally does not stage the PKG and never calls BGFT.
 * It asks the native installer to consume the short-lived Worker URL.
 */
typedef char appinst_scenario_id_t[3];
typedef char appinst_language_t[8];
typedef char appinst_content_id_t[0x30];

typedef struct {
    appinst_content_id_t content_id;
    int content_type;
    int content_platform;
} appinst_pkg_info_t;

typedef struct {
    const char *uri;
    const char *ex_uri;
    const char *playgo_scenario_id;
    const char *content_id;
    const char *content_name;
    const char *icon_url;
} appinst_meta_info_t;

typedef struct {
    appinst_language_t languages[30];
    appinst_scenario_id_t playgo_scenario_ids[64];
    appinst_content_id_t content_ids[64];
    unsigned char unknown[6480];
} appinst_playgo_info_t;

typedef int (*appinst_init_fn)(void);
typedef int (*appinst_install_by_package_fn)(
    appinst_meta_info_t*,
    appinst_pkg_info_t*,
    appinst_playgo_info_t*);

static appinst_install_by_package_fn appinst_install_by_package = 0;
static int appinst_ready = 0;

static int init_appinst(void)
{
    if (appinst_ready) return 0;

    void *mod = dlopen("/system/common/lib/libSceAppInstUtil.sprx", 0);
    if (!mod) return -501;

    appinst_init_fn init =
        (appinst_init_fn)dlsym(mod, "sceAppInstUtilInitialize");
    appinst_install_by_package =
        (appinst_install_by_package_fn)dlsym(
            mod, "sceAppInstUtilInstallByPackage");

    if (!init) return -502;
    if (!appinst_install_by_package) return -503;

    int rc = init();
    if (rc != 0) return rc;

    appinst_ready = 1;
    return 0;
}

static int valid_worker_url(const char *url)
{
    if (!starts(url, "https://")) return 0;

    size_t n = 0;
    int saw_pkg = 0;
    while (url[n]) {
        unsigned char c = (unsigned char)url[n];
        if (c == '\r' || c == '\n' || c == 0) return 0;
        if (n >= 900) return 0;
        if (n >= 3 &&
            url[n - 3] == '.' &&
            url[n - 2] == 'p' &&
            url[n - 1] == 'k' &&
            url[n] == 'g')
            saw_pkg = 1;
        n++;
    }
    return n > 12 && saw_pkg;
}

static int native_install_url(int fd, const char *url)
{
    if (!valid_worker_url(url)) {
        reply(fd, "ERR URL\n");
        return 0;
    }

    int rc = init_appinst();
    if (rc != 0) {
        reply_code(fd, "ERR APPINST_INIT", rc);
        notify_code("Tayson AppInst init falhou", rc);
        return 0;
    }

    appinst_meta_info_t meta = {0};
    appinst_pkg_info_t pkg = {0};
    appinst_playgo_info_t playgo = {0};

    meta.uri = url;
    meta.ex_uri = "";
    meta.playgo_scenario_id = "";
    meta.content_id = "";
    meta.content_name = "Tayson";
    meta.icon_url = "";

    rc = appinst_install_by_package(&meta, &pkg, &playgo);
    if (rc != 0) {
        reply_code(fd, "ERR APPINST", rc);
        notify_code("Tayson AppInst recusou", rc);
        return 0;
    }

    reply(fd, "ACCEPTED APPINST\n");
    notify("Tayson: instalacao nativa aceita; app pode fechar");

    /*
     * Success is deliberately one-shot.  The native service now owns the
     * operation, so this temporary BinLoader payload exits instead of
     * remaining installed/resident on the console.
     */
    return 1;
}

static int handle_client(int fd)
{
    char line[LINE_MAX];

    if (read_line(fd, line, sizeof(line)) < 0) {
        reply(fd, "ERR protocol\n");
        return 0;
    }

    if (starts(line, "PING")) {
        reply(fd, "PONG TAYSON-RX 4.0.0-native1\n");
        return 0;
    }

    if (starts(line, "INSTALLURL "))
        return native_install_url(fd, line + 11);

    if (starts(line, "QUIT")) {
        reply(fd, "BYE\n");
        return 1;
    }

    reply(fd, "ERR command\n");
    return 0;
}

int main(void)
{
    struct jbc_cred cred;
    if (jbc_get_cred(&cred) == 0) {
        jbc_jailbreak_cred(&cred);
        cred.jdir = 0;
        cred.sceProcType = 0x3800000000000010ULL;
        cred.sonyCred = 0x40001c0000000000ULL;
        cred.sceProcCap = 0x900000000000ff00ULL;
        jbc_set_cred(&cred);
    }

    void *sysutil = dlopen("/system/common/lib/libSceSysUtil.sprx", 0);
    if (sysutil)
        notify_fn = (int(*)(int,const char*))dlsym(
            sysutil, "sceSysUtilSendSystemNotificationWithText");

    int server = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (server < 0) {
        notify("Tayson Native: socket falhou");
        return -1;
    }

    struct sockaddr_in addr;
    addr.sin_len = sizeof(addr);
    addr.sin_family = AF_INET;
    addr.sin_port = (uint16_t)(((TAYSON_RX_PORT & 0xff) << 8) |
                              ((TAYSON_RX_PORT >> 8) & 0xff));
    addr.sin_addr.s_addr = 0;
    for (int i = 0; i < 8; i++) addr.sin_zero[i] = 0;

    if (bind(server, (struct sockaddr*)&addr, sizeof(addr)) < 0) {
        notify("Tayson Native: porta 9091 indisponivel");
        close(server);
        return -2;
    }

    if (listen(server, 2) < 0) {
        notify("Tayson Native: listen falhou");
        close(server);
        return -3;
    }

    notify("Tayson Native: pronto para handoff");

    int stop = 0;
    while (!stop) {
        struct sockaddr_in peer;
        socklen_t peer_len = sizeof(peer);
        int client = accept(server, (struct sockaddr*)&peer, &peer_len);
        if (client < 0) continue;
        stop = handle_client(client);
        close(client);
    }

    close(server);
    return 0;
}
