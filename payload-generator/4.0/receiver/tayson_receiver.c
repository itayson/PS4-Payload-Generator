#include <stddef.h>
#include <stdint.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <unistd.h>
#include <fcntl.h>
#include <sys/stat.h>
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
#define CHUNK_SIZE 65536

void* dlopen(const char*, int);
void* dlsym(void*, const char*);

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

static uint64_t parse_u64(const char *s, int *ok)
{
    uint64_t value = 0;
    int any = 0;
    *ok = 0;
    while (*s >= '0' && *s <= '9') {
        uint64_t digit = (uint64_t)(*s - '0');
        if (value > (UINT64_MAX - digit) / 10) return 0;
        value = value * 10 + digit;
        any = 1;
        s++;
    }
    if (!any) return 0;
    *ok = 1;
    return value;
}

static int safe_name(const char *name)
{
    size_t n = 0;
    if (!name || !*name) return 0;
    while (name[n]) {
        char c = name[n];
        int ok =
            (c >= 'a' && c <= 'z') ||
            (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') ||
            c == '.' || c == '_' || c == '-';
        if (!ok) return 0;
        n++;
        if (n > 96) return 0;
    }
    if (name[0] == '.') return 0;
    return 1;
}

static int make_path(char *out, size_t cap, const char *name)
{
    const char *prefix = "/user/data/tayson/inbox/";
    size_t p = tlen(prefix), n = tlen(name);
    if (p + n + 1 > cap) return -1;
    for (size_t i = 0; i < p; i++) out[i] = prefix[i];
    for (size_t i = 0; i < n; i++) out[p + i] = name[i];
    out[p + n] = 0;
    return 0;
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

static int receive_exact_to_file(int sock, int file, uint64_t total)
{
    char buffer[CHUNK_SIZE];
    uint64_t got = 0;

    while (got < total) {
        size_t want = CHUNK_SIZE;
        uint64_t left = total - got;
        if (left < want) want = (size_t)left;

        ssize_t n = read(sock, buffer, want);
        if (n <= 0) return -1;

        if (write_all(file, buffer, (size_t)n) != 0) return -2;
        got += (uint64_t)n;
    }
    return 0;
}

typedef int (*net_init_fn)(void);
typedef int (*net_pool_create_fn)(const char*, int, int);
typedef int (*ssl_init_fn)(size_t);
typedef int (*http_init_fn)(int, int, size_t);
typedef int (*http_template_fn)(int, const char*, int, int);
typedef int (*http_connection_fn)(int, const char*, int);
typedef int (*http_request_fn)(int, int, const char*, uint64_t);
typedef int (*http_header_fn)(int, const char*, const char*, int);
typedef int (*http_send_fn)(int, const void*, size_t);
typedef int (*http_status_fn)(int, int*);
typedef int (*http_read_fn)(int, void*, uint32_t);
typedef int (*http_delete_fn)(int);

static int network_ready = 0;
static int http_ctx = -1;
static http_template_fn http_template = 0;
static http_connection_fn http_connection = 0;
static http_request_fn http_request = 0;
static http_header_fn http_header = 0;
static http_send_fn http_send = 0;
static http_status_fn http_status = 0;
static http_read_fn http_read = 0;
static http_delete_fn http_delete_request = 0;
static http_delete_fn http_delete_connection = 0;
static http_delete_fn http_delete_template = 0;

static void hex32(int value, char *out)
{
    static const char d[] = "0123456789ABCDEF";
    uint32_t v = (uint32_t)value;
    out[0] = '0'; out[1] = 'x';
    for (int i = 0; i < 8; i++)
        out[2 + i] = d[(v >> (28 - i * 4)) & 15];
    out[10] = 0;
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

static int init_native_http(void)
{
    if (network_ready) return 0;

    void *net = dlopen("/system/common/lib/libSceNet.sprx", 0);
    void *ssl = dlopen("/system/common/lib/libSceSsl.sprx", 0);
    void *http = dlopen("/system/common/lib/libSceHttp.sprx", 0);
    if (!net || !ssl || !http) return -200;

    net_init_fn net_init = (net_init_fn)dlsym(net, "sceNetInit");
    net_pool_create_fn pool_create =
        (net_pool_create_fn)dlsym(net, "sceNetPoolCreate");
    ssl_init_fn ssl_init = (ssl_init_fn)dlsym(ssl, "sceSslInit");
    http_init_fn http_init = (http_init_fn)dlsym(http, "sceHttpInit");

    http_template = (http_template_fn)dlsym(http, "sceHttpCreateTemplate");
    http_connection =
        (http_connection_fn)dlsym(http, "sceHttpCreateConnectionWithURL");
    http_request =
        (http_request_fn)dlsym(http, "sceHttpCreateRequestWithURL");
    http_header =
        (http_header_fn)dlsym(http, "sceHttpAddRequestHeader");
    http_send = (http_send_fn)dlsym(http, "sceHttpSendRequest");
    http_status = (http_status_fn)dlsym(http, "sceHttpGetStatusCode");
    http_read = (http_read_fn)dlsym(http, "sceHttpReadData");
    http_delete_request =
        (http_delete_fn)dlsym(http, "sceHttpDeleteRequest");
    http_delete_connection =
        (http_delete_fn)dlsym(http, "sceHttpDeleteConnection");
    http_delete_template =
        (http_delete_fn)dlsym(http, "sceHttpDeleteTemplate");

    if (!pool_create || !ssl_init || !http_init ||
        !http_template || !http_connection || !http_request ||
        !http_header || !http_send || !http_status || !http_read ||
        !http_delete_request || !http_delete_connection ||
        !http_delete_template)
        return -201;

    if (net_init) net_init();

    int pool = pool_create("TaysonPull", 4 * 1024 * 1024, 0);
    if (pool < 0) return pool;

    int ssl_ctx = ssl_init(8 * 1024 * 1024);
    if (ssl_ctx < 0) return ssl_ctx;

    http_ctx = http_init(pool, ssl_ctx, 8 * 1024 * 1024);
    if (http_ctx < 0) return http_ctx;

    network_ready = 1;
    return 0;
}

static int make_part_path(char *out, size_t cap, const char *name)
{
    char base[160];
    if (make_path(base, sizeof(base), name) != 0) return -1;
    size_t n = tlen(base);
    if (n + 6 > cap) return -1;
    for (size_t i = 0; i < n; i++) out[i] = base[i];
    out[n++] = '.';
    out[n++] = 'p';
    out[n++] = 'a';
    out[n++] = 'r';
    out[n++] = 't';
    out[n] = 0;
    return 0;
}

int rename(const char*, const char*);

static int pull_worker_url(const char *url, const char *name, uint64_t expected)
{
    int rc = init_native_http();
    if (rc != 0) return rc;

    char part[176], final[160];
    if (make_part_path(part, sizeof(part), name) != 0 ||
        make_path(final, sizeof(final), name) != 0)
        return -210;

    int out = open(part, O_WRONLY | O_CREAT | O_TRUNC, 0600);
    if (out < 0) return -211;

    int tmpl = http_template(http_ctx, "TaysonPull/4.0", 2, 1);
    if (tmpl < 0) { close(out); unlink(part); return tmpl; }

    int conn = http_connection(tmpl, url, 1);
    if (conn < 0) {
        http_delete_template(tmpl);
        close(out); unlink(part); return conn;
    }

    int req = http_request(conn, 0, url, 0);
    if (req < 0) {
        http_delete_connection(conn); http_delete_template(tmpl);
        close(out); unlink(part); return req;
    }

    http_header(req, "Accept", "*/*", 0);
    http_header(req, "Accept-Encoding", "identity", 0);

    rc = http_send(req, 0, 0);
    if (rc < 0) goto fail;

    int status = 0;
    rc = http_status(req, &status);
    if (rc < 0) goto fail;
    if (status != 200 && status != 206) {
        rc = -10000 - status;
        goto fail;
    }

    char buffer[CHUNK_SIZE];
    uint64_t got = 0;
    for (;;) {
        int n = http_read(req, buffer, sizeof(buffer));
        if (n < 0) { rc = n; goto fail; }
        if (n == 0) break;
        if (got + (uint64_t)n > expected) {
            rc = -212;
            goto fail;
        }
        if (write_all(out, buffer, (size_t)n) != 0) {
            rc = -213;
            goto fail;
        }
        got += (uint64_t)n;
    }

    fsync(out);
    close(out);
    out = -1;

    http_delete_request(req);
    http_delete_connection(conn);
    http_delete_template(tmpl);

    if (got != expected) {
        unlink(part);
        return -214;
    }

    int in = open(part, O_RDONLY);
    if (in < 0) { unlink(part); return -215; }
    unsigned char magic[4];
    ssize_t mn = read(in, magic, 4);
    close(in);
    if (mn != 4 || magic[0] != 0x7f || magic[1] != 0x43 ||
        magic[2] != 0x4e || magic[3] != 0x54) {
        unlink(part);
        return -216;
    }

    unlink(final);
    if (rename(part, final) != 0) {
        unlink(part);
        return -217;
    }

    return 0;

fail:
    if (out >= 0) close(out);
    http_delete_request(req);
    http_delete_connection(conn);
    http_delete_template(tmpl);
    unlink(part);
    return rc;
}

static int handle_pull(int fd, char *line)
{
    char *p = line + 5;
    int ok = 0;
    uint64_t size = parse_u64(p, &ok);
    if (!ok) { reply(fd, "ERR size\n"); return 0; }

    while (*p >= '0' && *p <= '9') p++;
    if (*p++ != ' ') { reply(fd, "ERR syntax\n"); return 0; }

    char *name = p;
    while (*p && *p != ' ') p++;
    if (*p != ' ') { reply(fd, "ERR syntax\n"); return 0; }
    *p++ = 0;

    char *url = p;
    if (!safe_name(name) ||
        !starts(url, "https://") ||
        tlen(url) > 700) {
        reply(fd, "ERR job\n");
        return 0;
    }

    reply(fd, "QUEUED\n");
    notify("Tayson Pull: job recebido; o app pode ser fechado");

    /* The Android client is no longer part of the transfer after this ACK. */
    close(fd);

    int rc = pull_worker_url(url, name, size);
    if (rc == 0)
        notify("Tayson Pull: PKG baixado e verificado no PS4");
    else
        notify_code("Tayson Pull falhou", rc);

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
        reply(fd, "PONG TAYSON-RX 4.0.0-pull1\n");
        return 0;
    }

    if (starts(line, "PULL ")) {
        handle_pull(fd, line);
        return 0;
    }

    if (starts(line, "PUT ")) {
        char *p = line + 4;
        int ok = 0;
        uint64_t size = parse_u64(p, &ok);
        if (!ok) {
            reply(fd, "ERR size\n");
            return 0;
        }

        while (*p >= '0' && *p <= '9') p++;
        if (*p != ' ') {
            reply(fd, "ERR syntax\n");
            return 0;
        }
        p++;

        if (!safe_name(p)) {
            reply(fd, "ERR name\n");
            return 0;
        }

        char path[160];
        if (make_path(path, sizeof(path), p) != 0) {
            reply(fd, "ERR path\n");
            return 0;
        }

        int out = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0600);
        if (out < 0) {
            reply(fd, "ERR open\n");
            return 0;
        }

        reply(fd, "READY\n");
        int rc = receive_exact_to_file(fd, out, size);
        fsync(out);
        close(out);

        if (rc != 0) {
            unlink(path);
            reply(fd, "ERR transfer\n");
            return 0;
        }

        reply(fd, "STORED\n");
        notify("Tayson Receiver: arquivo recebido");
        return 0;
    }

    if (starts(line, "DELETE ")) {
        const char *name = line + 7;
        if (!safe_name(name)) {
            reply(fd, "ERR name\n");
            return 0;
        }
        char path[160];
        if (make_path(path, sizeof(path), name) != 0) {
            reply(fd, "ERR path\n");
            return 0;
        }
        int rc = unlink(path);
        reply(fd, rc == 0 ? "DELETED\n" : "ERR delete\n");
        return 0;
    }

    if (starts(line, "QUIT")) {
        reply(fd, "BYE\n");
        return 0;
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

    mkdir("/user/data/tayson", 0700);
    mkdir("/user/data/tayson/inbox", 0700);

    int server = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (server < 0) {
        notify("Tayson Receiver: socket falhou");
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
        notify("Tayson Receiver: porta 9091 indisponivel");
        close(server);
        return -2;
    }

    if (listen(server, 2) < 0) {
        notify("Tayson Receiver: listen falhou");
        close(server);
        return -3;
    }

    notify("Tayson Pull Engine: pronto na porta 9091");

    for (;;) {
        struct sockaddr_in peer;
        socklen_t peer_len = sizeof(peer);
        int client = accept(server, (struct sockaddr*)&peer, &peer_len);
        if (client < 0) continue;
        int consumed = handle_client(client);
        if (!consumed) close(client);
    }

    close(server);
    return 0;
}
