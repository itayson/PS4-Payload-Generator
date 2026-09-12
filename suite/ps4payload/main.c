/* Tayson PS4 Suite: standalone diagnostic backend, loaded by ps4-payload-dev/elfldr.
 * No BGFT registration, filesystem writes, kernel patches or installation here.
 * The control protocol and HTTP context come from the existing Receiver project.
 */
#include <arpa/inet.h>
#include <errno.h>
#include <netinet/in.h>
#include <pthread.h>
#include <signal.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>
#include "job_wire.h"

#define VERSION "1.5.0"
#define PORT 9096
#define SAMPLE_SIZE 16

/* Public SCE entrypoints. HTTP2 ABI follows ps4-payload-dev/elfldr uri.c.
 * Load lazily so PING and per-stage diagnostics work even if a module fails.
 */
int sceKernelLoadStartModule(const char *, size_t, const void *, unsigned, void *, int *);
int sceKernelDlsym(int, const char *, void **);
int sceNetInit(void);
int sceNetPoolCreate(const char *, int, int);
int sceNetPoolDestroy(int);

static int (*p_sceSslInit)(size_t);
static int (*p_sceSslTerm)(int);
static int (*p_sceHttp2Init)(int, int, size_t, int);
static int (*p_sceHttp2Term)(int);
static int (*p_sceHttp2CreateTemplate)(int, const char *, int, int);
static int (*p_sceHttp2DeleteTemplate)(int);
static int (*p_sceHttp2CreateRequestWithURL)(int, const char *, const char *, uint64_t);
static int (*p_sceHttp2DeleteRequest)(int);
static int (*p_sceHttp2AddRequestHeader)(int, const char *, const char *, unsigned);
static int (*p_sceHttp2SetAutoRedirect)(int, int);
static int (*p_sceHttp2SetConnectTimeOut)(int, unsigned);
static int (*p_sceHttp2SetResolveTimeOut)(int, unsigned);
static int (*p_sceHttp2SetSendTimeOut)(int, unsigned);
static int (*p_sceHttp2SetRecvTimeOut)(int, unsigned);
static int (*p_sceHttp2SendRequest)(int, const void *, size_t);
static int (*p_sceHttp2GetStatusCode)(int, int *);
static int (*p_sceHttp2ReadData)(int, void *, size_t);
static int g_ssl_module = -1, g_http_module = -1;
static pthread_mutex_t g_lock = PTHREAD_MUTEX_INITIALIZER;
static int g_busy = 0;
static char g_stage[48] = "ready";

static int send_text(int fd, const char *format, ...) {
    char buf[384];
    va_list args; va_start(args, format);
    int n = vsnprintf(buf, sizeof(buf), format, args);
    va_end(args);
    if (n < 0 || (size_t)n >= sizeof(buf)) return -1;
    size_t done = 0;
    while (done < (size_t)n) {
        ssize_t r = send(fd, buf + done, (size_t)n - done, 0);
        if (r < 0 && errno == EINTR) continue;
        if (r <= 0) return -1;
        done += (size_t)r;
    }
    return 0;
}

static void step(int fd, const char *stage) {
    pthread_mutex_lock(&g_lock);
    snprintf(g_stage, sizeof(g_stage), "%s", stage);
    pthread_mutex_unlock(&g_lock);
    send_text(fd, "TAYSON/1 STEP stage=%s\n", stage);
}

static int receive_exact(int fd, unsigned char *p, size_t n) {
    while (n) {
        ssize_t r = recv(fd, p, n, 0);
        if (r < 0 && errno == EINTR) continue;
        if (r <= 0) return -1;
        p += r; n -= (size_t)r;
    }
    return 0;
}

static int read_line(int fd, char *out, size_t cap) {
    for (size_t i = 0; i + 1 < cap; ++i) {
        unsigned char c;
        if (receive_exact(fd, &c, 1)) return -1;
        if (c == '\n') { out[i] = 0; return 0; }
        if (c < 32 || c > 126) return -1;
        out[i] = (char)c;
    }
    return -1;
}

static int private_ip(uint32_t net) {
    uint32_t ip = ntohl(net);
    return (ip >> 24) == 10 || (ip >> 20) == 0xac1 || (ip >> 16) == 0xc0a8 || (ip >> 24) == 127;
}

static int load_http(int fd) {
    int r;
    if (g_ssl_module < 0) {
        step(fd, "ssl2-module");
        g_ssl_module = sceKernelLoadStartModule("/system/common/lib/libSceSsl2.sprx", 0, NULL, 0, NULL, NULL);
        if (g_ssl_module < 0) return g_ssl_module;
    }
    if (g_http_module < 0) {
        step(fd, "http2-module");
        g_http_module = sceKernelLoadStartModule("/system/common/lib/libSceHttp2.sprx", 0, NULL, 0, NULL, NULL);
        if (g_http_module < 0) return g_http_module;
    }
    step(fd, "http2-symbols");
#define RESOLVE(mod, sym) do { void *p = NULL; r = sceKernelDlsym(mod, #sym, &p); if (r < 0 || !p) return r < 0 ? r : -1; p_##sym = (__typeof__(p_##sym))p; } while (0)
    RESOLVE(g_ssl_module, sceSslInit); RESOLVE(g_ssl_module, sceSslTerm);
    RESOLVE(g_http_module, sceHttp2Init); RESOLVE(g_http_module, sceHttp2Term);
    RESOLVE(g_http_module, sceHttp2CreateTemplate); RESOLVE(g_http_module, sceHttp2DeleteTemplate);
    RESOLVE(g_http_module, sceHttp2CreateRequestWithURL); RESOLVE(g_http_module, sceHttp2DeleteRequest);
    RESOLVE(g_http_module, sceHttp2AddRequestHeader); RESOLVE(g_http_module, sceHttp2SetAutoRedirect);
    RESOLVE(g_http_module, sceHttp2SetConnectTimeOut); RESOLVE(g_http_module, sceHttp2SetResolveTimeOut);
    RESOLVE(g_http_module, sceHttp2SetSendTimeOut); RESOLVE(g_http_module, sceHttp2SetRecvTimeOut);
    RESOLVE(g_http_module, sceHttp2SendRequest); RESOLVE(g_http_module, sceHttp2GetStatusCode);
    RESOLVE(g_http_module, sceHttp2ReadData);
#undef RESOLVE
    return 0;
}

static int blocked_header(const char *s) {
    const char *names[] = {"host", "content-length", "range", "connection", "transfer-encoding", "te", "trailer", "upgrade", "proxy-authorization", "proxy-connection", "user-agent", "referer", "cookie", "accept-encoding"};
    for (size_t i = 0; i < sizeof(names)/sizeof(names[0]); ++i) if (!strcasecmp(s, names[i])) return 1;
    return 0;
}

static int add_headers(int req, tayson_job *j) {
    int r = p_sceHttp2AddRequestHeader(req, "Range", "bytes=0-15", 0);
    if (r < 0) return r;
    if ((r = p_sceHttp2AddRequestHeader(req, "Accept-Encoding", "identity", 0)) < 0) return r;
    if (j->referer[0] && (r = p_sceHttp2AddRequestHeader(req, "Referer", j->referer, 0)) < 0) return r;
    if (j->cookie[0] && (r = p_sceHttp2AddRequestHeader(req, "Cookie", j->cookie, 0)) < 0) return r;
    char *save = NULL;
    for (char *s = strtok_r(j->headers, "\n", &save); s; s = strtok_r(NULL, "\n", &save)) {
        char *colon = strchr(s, ':');
        if (!colon) return -1;
        *colon++ = 0;
        if (!*s) return -1;
        for (const char *k = s; *k; ++k) if (!(isalnum((unsigned char)*k) || *k == '-' || *k == '_')) return -1;
        while (*colon == ' ' || *colon == '\t') ++colon;
        if (blocked_header(s)) continue;
        if ((r = p_sceHttp2AddRequestHeader(req, s, colon, 0)) < 0) return r;
    }
    return 0;
}

typedef struct { int fd; tayson_job job; } probe_work;

static void *run_probe(void *ptr) {
    probe_work *w = ptr;
    int fd = w->fd, net = -1, ssl = -1, http = -1, tpl = -1, req = -1;
    int r = 0, status = 0, count = 0;
    unsigned char sample[SAMPLE_SIZE] = {0};
    r = load_http(fd); if (r < 0) goto done;
    step(fd, "net-init");
    r = sceNetInit(); if (r < 0 && (uint32_t)r != 0x80410004U) goto done;
    step(fd, "net-pool");
    r = net = sceNetPoolCreate("TaysonProbe", 1024 * 1024, 0); if (r < 0) goto done;
    step(fd, "ssl2-init");
    r = ssl = p_sceSslInit(1024 * 1024); if (r < 0) goto done;
    step(fd, "http2-init");
    r = http = p_sceHttp2Init(net, ssl, 1024 * 1024, 1); if (r < 0) goto done;
    step(fd, "http2-template");
    r = tpl = p_sceHttp2CreateTemplate(http, w->job.agent, 3, 1); if (r < 0) goto done;
    /* Default certificate verification stays enabled. Report failures as data.
     * Do not follow redirects with cookies/Authorization to another host. */
    if ((r = p_sceHttp2SetAutoRedirect(tpl, 0)) < 0) goto done;
    if ((r = p_sceHttp2SetConnectTimeOut(tpl, 10000000)) < 0) goto done;
    if ((r = p_sceHttp2SetResolveTimeOut(tpl, 10000000)) < 0) goto done;
    if ((r = p_sceHttp2SetSendTimeOut(tpl, 10000000)) < 0) goto done;
    if ((r = p_sceHttp2SetRecvTimeOut(tpl, 10000000)) < 0) goto done;
    step(fd, "request-create");
    r = req = p_sceHttp2CreateRequestWithURL(tpl, "GET", w->job.url, 0); if (r < 0) goto done;
    step(fd, "request-headers");
    r = add_headers(req, &w->job); if (r < 0) goto done;
    step(fd, "request-send");
    r = p_sceHttp2SendRequest(req, NULL, 0); if (r < 0) goto done;
    step(fd, "response-status");
    r = p_sceHttp2GetStatusCode(req, &status); if (r < 0) goto done;
    if (status != 200 && status != 206) { r = -2; goto done; }
    step(fd, "read-16-bytes");
    while (count < SAMPLE_SIZE) {
        r = p_sceHttp2ReadData(req, sample + count, SAMPLE_SIZE - count);
        if (r <= 0) { if (!r) r = -3; goto done; }
        if (r > SAMPLE_SIZE - count) { r = -4; goto done; }
        count += r;
    }
    r = 0;
done:
    if (!r) {
        int magic = sample[0] == 0x7f && sample[1] == 'C' && sample[2] == 'N' && sample[3] == 'T';
        send_text(fd, "TAYSON/1 PROBE payload=%s http=%d bytes=%d pkg=%d scheme=%s\n", VERSION, status, count, magic, !strncmp(w->job.url, "https://", 8) ? "https" : "http");
    } else {
        char stage[48];
        pthread_mutex_lock(&g_lock); strcpy(stage, g_stage); pthread_mutex_unlock(&g_lock);
        send_text(fd, "TAYSON/1 ERR stage=%s code=0x%08X http=%d bytes=%d\n", stage, (unsigned)r, status, count);
    }
    /* Always release request contexts; one test never allocates a full PKG. */
    if (req >= 0) p_sceHttp2DeleteRequest(req);
    if (tpl >= 0) p_sceHttp2DeleteTemplate(tpl);
    if (http >= 0) p_sceHttp2Term(http);
    if (ssl >= 0) p_sceSslTerm(ssl);
    if (net >= 0) sceNetPoolDestroy(net);
    close(fd);
    memset(w, 0, sizeof(*w)); free(w);
    pthread_mutex_lock(&g_lock); g_busy = 0; strcpy(g_stage, "ready"); pthread_mutex_unlock(&g_lock);
    return NULL;
}

static void handle(int fd, uint32_t peer) {
    if (!private_ip(peer)) { send_text(fd, "TAYSON/1 ERR private-network-only\n"); close(fd); return; }
    struct timeval timeout = {10, 0};
    setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &timeout, sizeof(timeout));
    setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &timeout, sizeof(timeout));
    char line[96];
    if (read_line(fd, line, sizeof(line))) { close(fd); return; }
    if (!strcmp(line, "TAYSON/1 PING")) {
        char stage[48]; int busy;
        pthread_mutex_lock(&g_lock); busy = g_busy; strcpy(stage, g_stage); pthread_mutex_unlock(&g_lock);
        send_text(fd, "TAYSON/1 OK payload=%s caps=probe pid=%d busy=%d stage=%s\n", VERSION, (int)getpid(), busy, stage);
        close(fd); return;
    }
    if (strncmp(line, "TAYSON/1 PROBE ", 15)) { send_text(fd, "TAYSON/1 ERR probe-only\n"); close(fd); return; }
    const char *digits = line + 15;
    char *end; unsigned long length = strtoul(digits, &end, 10);
    if (*digits < '0' || *digits > '9' || *end || !length || length > TAYSON_MAX_PACKET) {
        send_text(fd, "TAYSON/1 ERR invalid-length\n"); close(fd); return;
    }
    pthread_mutex_lock(&g_lock);
    if (g_busy) { pthread_mutex_unlock(&g_lock); send_text(fd, "TAYSON/1 ERR probe-busy\n"); close(fd); return; }
    g_busy = 1; pthread_mutex_unlock(&g_lock);
    unsigned char *packet = malloc(length);
    probe_work *work = calloc(1, sizeof(*work));
    const char *error = "no-memory";
    if (!packet || !work) goto fail;
    error = "incomplete-packet";
    if (receive_exact(fd, packet, length)) goto fail;
    error = "invalid-packet";
    if (wire_parse(packet, length, &work->job)) goto fail;
    memset(packet, 0, length); free(packet); packet = NULL;
    work->fd = fd;
    pthread_attr_t attr; pthread_t thread;
    error = "thread-init";
    if (pthread_attr_init(&attr)) goto fail;
    int r = pthread_attr_setstacksize(&attr, 1024 * 1024);
    if (!r) r = pthread_create(&thread, &attr, run_probe, work);
    pthread_attr_destroy(&attr);
    if (r) goto fail;
    pthread_detach(thread); return;
fail:
    if (packet) { memset(packet, 0, length); free(packet); }
    if (work) { memset(work, 0, sizeof(*work)); free(work); }
    send_text(fd, "TAYSON/1 ERR %s\n", error); close(fd);
    pthread_mutex_lock(&g_lock); g_busy = 0; pthread_mutex_unlock(&g_lock);
}

int main(void) {
    signal(SIGPIPE, SIG_IGN);
    int listener = socket(AF_INET, SOCK_STREAM, 0);
    if (listener < 0) return 1;
    int yes = 1;
    setsockopt(listener, SOL_SOCKET, SO_REUSEADDR, &yes, sizeof(yes));
    struct sockaddr_in addr; memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET; addr.sin_addr.s_addr = htonl(INADDR_ANY); addr.sin_port = htons(PORT);
    if (bind(listener, (struct sockaddr *)&addr, sizeof(addr)) || listen(listener, 4)) { close(listener); return 2; }
    printf("TAYSON/1 READY payload=%s port=%d\n", VERSION, PORT); fflush(stdout);
    for (;;) {
        struct sockaddr_in peer; socklen_t n = sizeof(peer);
        int client = accept(listener, (struct sockaddr *)&peer, &n);
        if (client < 0) { if (errno == EINTR) continue; break; }
        handle(client, peer.sin_addr.s_addr);
    }
    close(listener); return 0;
}
