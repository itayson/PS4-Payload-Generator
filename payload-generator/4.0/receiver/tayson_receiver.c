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
#define LINE_MAX 192
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

static void handle_client(int fd)
{
    char line[LINE_MAX];

    if (read_line(fd, line, sizeof(line)) < 0) {
        reply(fd, "ERR protocol\n");
        return;
    }

    if (starts(line, "PING")) {
        reply(fd, "PONG TAYSON-RX 4.0.0-alpha1\n");
        return;
    }

    if (starts(line, "PUT ")) {
        char *p = line + 4;
        int ok = 0;
        uint64_t size = parse_u64(p, &ok);
        if (!ok) {
            reply(fd, "ERR size\n");
            return;
        }

        while (*p >= '0' && *p <= '9') p++;
        if (*p != ' ') {
            reply(fd, "ERR syntax\n");
            return;
        }
        p++;

        if (!safe_name(p)) {
            reply(fd, "ERR name\n");
            return;
        }

        char path[160];
        if (make_path(path, sizeof(path), p) != 0) {
            reply(fd, "ERR path\n");
            return;
        }

        int out = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0600);
        if (out < 0) {
            reply(fd, "ERR open\n");
            return;
        }

        reply(fd, "READY\n");
        int rc = receive_exact_to_file(fd, out, size);
        fsync(out);
        close(out);

        if (rc != 0) {
            unlink(path);
            reply(fd, "ERR transfer\n");
            return;
        }

        reply(fd, "STORED\n");
        notify("Tayson Receiver: arquivo recebido");
        return;
    }

    if (starts(line, "DELETE ")) {
        const char *name = line + 7;
        if (!safe_name(name)) {
            reply(fd, "ERR name\n");
            return;
        }
        char path[160];
        if (make_path(path, sizeof(path), name) != 0) {
            reply(fd, "ERR path\n");
            return;
        }
        int rc = unlink(path);
        reply(fd, rc == 0 ? "DELETED\n" : "ERR delete\n");
        return;
    }

    if (starts(line, "QUIT")) {
        reply(fd, "BYE\n");
        return;
    }

    reply(fd, "ERR command\n");
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

    notify("Tayson Receiver 4.0 alpha: pronto na porta 9091");

    for (;;) {
        struct sockaddr_in peer;
        socklen_t peer_len = sizeof(peer);
        int client = accept(server, (struct sockaddr*)&peer, &peer_len);
        if (client < 0) continue;
        handle_client(client);
        close(client);
    }

    close(server);
    return 0;
}
