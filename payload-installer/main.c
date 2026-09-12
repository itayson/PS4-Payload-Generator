#include <arpa/inet.h>
#include <errno.h>
#include <fcntl.h>
#include <netinet/in.h>
#include <pthread.h>
#include <signal.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <unistd.h>

#include <ps4/kernel.h>

#define TAYSON_PAYLOAD_VERSION "0.1.0"
#define CONTROL_PORT 9096
#define HEADER_CAP (64 * 1024)
#define IO_BUF (512 * 1024)
#define URL_CAP 2048
#define NAME_CAP 256
#define TITLE_CAP 16
#define CACHE_DIR "/data/tayson/payload-cache"
#define CACHE_FILE CACHE_DIR "/current.pkg"

#ifndef MSG_NOSIGNAL
#define MSG_NOSIGNAL 0
#endif

typedef struct notify_request {
    char useless1[45];
    char message[3075];
} notify_request_t;

__attribute__((weak))
int sceKernelSendNotificationRequest(int, notify_request_t *, size_t, int);

typedef int (*appinst_init_fn)(void);
typedef int (*appinst_install_fn)(const char *, void *);
typedef int (*appinst_title_fn)(const char *, char *, int *);

typedef enum job_state {
    JOB_IDLE = 0,
    JOB_DOWNLOADING,
    JOB_INSTALLING,
    JOB_SUBMITTED,
    JOB_ERROR,
    JOB_CANCELLED
} job_state;

typedef struct payload_job {
    job_state state;
    char url[URL_CAP];
    char name[NAME_CAP];
    char expected_title[TITLE_CAP];
    char detected_title[TITLE_CAP];
    uint64_t total;
    uint64_t downloaded;
    int error_code;
    int install_code;
    int http_status;
    bool cancel;
} payload_job;

static pthread_mutex_t g_job_lock = PTHREAD_MUTEX_INITIALIZER;
static payload_job g_job;
static volatile bool g_server_running = true;

static void notify_user(const char *text) {
    if (!text) return;
    if (sceKernelSendNotificationRequest) {
        notify_request_t req;
        memset(&req, 0, sizeof(req));
        strncpy(req.message, text, sizeof(req.message) - 1);
        if (sceKernelSendNotificationRequest(0, &req, sizeof(req), 0) == 0) return;
    }
    fprintf(stdout, "%s\n", text);
    fflush(stdout);
}

static int ensure_dir(const char *path) {
    struct stat st;
    if (mkdir(path, 0755) == 0) return 0;
    if (stat(path, &st) == 0 && S_ISDIR(st.st_mode)) return 0;
    return -1;
}

static int64_t kernel_dlsym(int64_t handle, const char *name, void **out) {
    return (int64_t)syscall(591, (void *)handle, (void *)name, out);
}

static int load_module(const char *path) {
    return sceKernelLoadStartModule(path, 0, NULL, 0, NULL, NULL);
}

static int load_appinst(appinst_init_fn *init_out, appinst_install_fn *install_out,
                        appinst_title_fn *title_out) {
    if (!init_out || !install_out || !title_out) return -1;
    *init_out = NULL;
    *install_out = NULL;
    *title_out = NULL;

    int sys = load_module("/system/common/lib/libSceSystemService.sprx");
    if (sys < 0) return sys;

    int app = load_module("/system/common/lib/libSceAppInstUtil.sprx");
    if (app < 0) return app;

    void *sym = NULL;
    int64_t r = kernel_dlsym(app, "sceAppInstUtilInitialize", &sym);
    if (r < 0 || !sym) return r < 0 ? (int)r : -2;
    *init_out = (appinst_init_fn)sym;

    sym = NULL;
    r = kernel_dlsym(app, "sceAppInstUtilAppInstallPkg", &sym);
    if (r < 0 || !sym) return r < 0 ? (int)r : -3;
    *install_out = (appinst_install_fn)sym;

    sym = NULL;
    if (kernel_dlsym(app, "sceAppInstUtilGetTitleIdFromPkg", &sym) >= 0 && sym)
        *title_out = (appinst_title_fn)sym;

    return 0;
}

static int send_all(int fd, const void *buf, size_t len) {
    const uint8_t *p = (const uint8_t *)buf;
    while (len) {
        ssize_t n = send(fd, p, len, MSG_NOSIGNAL);
        if (n < 0) {
            if (errno == EINTR) continue;
            return -1;
        }
        if (n == 0) return -1;
        p += (size_t)n;
        len -= (size_t)n;
    }
    return 0;
}

static void respond_text(int fd, const char *text) {
    if (text) send_all(fd, text, strlen(text));
}

static const char *state_name(job_state s) {
    switch (s) {
        case JOB_IDLE: return "idle";
        case JOB_DOWNLOADING: return "downloading";
        case JOB_INSTALLING: return "installing";
        case JOB_SUBMITTED: return "submitted";
        case JOB_ERROR: return "error";
        case JOB_CANCELLED: return "cancelled";
        default: return "unknown";
    }
}

static void set_job_error(int code, int http_status) {
    pthread_mutex_lock(&g_job_lock);
    g_job.state = JOB_ERROR;
    g_job.error_code = code;
    if (http_status) g_job.http_status = http_status;
    pthread_mutex_unlock(&g_job_lock);
}

static bool job_cancelled(void) {
    bool c;
    pthread_mutex_lock(&g_job_lock);
    c = g_job.cancel;
    pthread_mutex_unlock(&g_job_lock);
    return c;
}

static void add_downloaded(uint64_t n) {
    pthread_mutex_lock(&g_job_lock);
    g_job.downloaded += n;
    pthread_mutex_unlock(&g_job_lock);
}

static int parse_http_url(const char *url, char *host, size_t host_cap,
                          uint16_t *port, char *path, size_t path_cap) {
    if (!url || strncmp(url, "http://", 7) != 0) return -1;
    const char *p = url + 7;
    const char *slash = strchr(p, '/');
    if (!slash) return -2;
    const char *colon = memchr(p, ':', (size_t)(slash - p));
    size_t host_len = colon ? (size_t)(colon - p) : (size_t)(slash - p);
    if (!host_len || host_len >= host_cap) return -3;
    memcpy(host, p, host_len);
    host[host_len] = 0;
    *port = 80;
    if (colon) {
        char tmp[8];
        size_t n = (size_t)(slash - colon - 1);
        if (!n || n >= sizeof(tmp)) return -4;
        memcpy(tmp, colon + 1, n);
        tmp[n] = 0;
        long v = strtol(tmp, NULL, 10);
        if (v <= 0 || v > 65535) return -5;
        *port = (uint16_t)v;
    }
    if (strlen(slash) >= path_cap) return -6;
    strcpy(path, slash);
    return 0;
}

static int connect_ipv4(const char *host, uint16_t port) {
    struct sockaddr_in addr;
    memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_port = htons(port);
    if (inet_pton(AF_INET, host, &addr.sin_addr) != 1) return -1;

    int fd = socket(AF_INET, SOCK_STREAM, 0);
    if (fd < 0) return -2;
    int one = 1;
    setsockopt(fd, SOL_SOCKET, SO_KEEPALIVE, &one, sizeof(one));
    if (connect(fd, (struct sockaddr *)&addr, sizeof(addr)) < 0) {
        int e = errno;
        close(fd);
        return -1000 - e;
    }
    return fd;
}

static int find_header_end(const uint8_t *buf, size_t len) {
    if (!buf || len < 4) return -1;
    for (size_t i = 3; i < len; ++i) {
        if (buf[i - 3] == '\r' && buf[i - 2] == '\n' &&
            buf[i - 1] == '\r' && buf[i] == '\n')
            return (int)(i + 1);
    }
    return -1;
}

static long long parse_content_length(const char *headers) {
    const char *p = headers;
    while (p && *p) {
        const char *e = strstr(p, "\r\n");
        size_t n = e ? (size_t)(e - p) : strlen(p);
        if (n > 15 && strncasecmp(p, "Content-Length:", 15) == 0) {
            while (15 < n && (p[15] == ' ' || p[15] == '\t')) p++;
            const char *v = strchr(p, ':');
            if (v) return strtoll(v + 1, NULL, 10);
        }
        if (!e) break;
        p = e + 2;
    }
    return -1;
}

static int download_from_relay(const char *url, uint64_t expected_size) {
    char host[64], path[URL_CAP];
    uint16_t port;
    int r = parse_http_url(url, host, sizeof(host), &port, path, sizeof(path));
    if (r) return -200 + r;

    if (ensure_dir("/data/tayson") != 0 || ensure_dir(CACHE_DIR) != 0) return -210;

    struct stat st;
    uint64_t resume = 0;
    if (stat(CACHE_FILE, &st) == 0 && st.st_size > 0) {
        resume = (uint64_t)st.st_size;
        if (expected_size && resume > expected_size) {
            unlink(CACHE_FILE);
            resume = 0;
        }
    }

    int fd = connect_ipv4(host, port);
    if (fd < 0) return fd;

    char req[4096];
    int reqn;
    if (resume) {
        reqn = snprintf(req, sizeof(req),
            "GET %s HTTP/1.1\r\nHost: %s:%u\r\nConnection: close\r\n"
            "Accept: */*\r\nAccept-Encoding: identity\r\nRange: bytes=%llu-\r\n\r\n",
            path, host, (unsigned)port, (unsigned long long)resume);
    } else {
        reqn = snprintf(req, sizeof(req),
            "GET %s HTTP/1.1\r\nHost: %s:%u\r\nConnection: close\r\n"
            "Accept: */*\r\nAccept-Encoding: identity\r\n\r\n",
            path, host, (unsigned)port);
    }
    if (reqn <= 0 || (size_t)reqn >= sizeof(req) || send_all(fd, req, (size_t)reqn)) {
        close(fd);
        return -220;
    }

    uint8_t *head = malloc(HEADER_CAP);
    uint8_t *io = malloc(IO_BUF);
    if (!head || !io) {
        free(head); free(io); close(fd); return -221;
    }

    size_t used = 0;
    int hend = -1;
    while (used < HEADER_CAP && hend < 0) {
        ssize_t n = recv(fd, head + used, HEADER_CAP - used, 0);
        if (n <= 0) { free(head); free(io); close(fd); return -222; }
        used += (size_t)n;
        hend = find_header_end(head, used);
    }
    if (hend < 0) { free(head); free(io); close(fd); return -223; }

    if ((size_t)hend >= HEADER_CAP) { free(head); free(io); close(fd); return -224; }
    head[hend - 2] = 0;
    int status = 0;
    sscanf((char *)head, "HTTP/%*s %d", &status);
    pthread_mutex_lock(&g_job_lock);
    g_job.http_status = status;
    pthread_mutex_unlock(&g_job_lock);

    if (status != 200 && status != 206) {
        free(head); free(io); close(fd); return -3000 - status;
    }

    int flags = O_WRONLY | O_CREAT;
    if (resume && status == 206) flags |= O_APPEND;
    else { flags |= O_TRUNC; resume = 0; }
    int out = open(CACHE_FILE, flags, 0644);
    if (out < 0) { int e = errno; free(head); free(io); close(fd); return -4000 - e; }

    pthread_mutex_lock(&g_job_lock);
    g_job.downloaded = resume;
    if (!g_job.total) {
        long long cl = parse_content_length((char *)head);
        if (cl > 0) g_job.total = resume + (uint64_t)cl;
    }
    pthread_mutex_unlock(&g_job_lock);

    size_t body_off = (size_t)hend;
    if (used > body_off) {
        size_t n = used - body_off;
        if (write(out, head + body_off, n) != (ssize_t)n) {
            close(out); free(head); free(io); close(fd); return -4010;
        }
        add_downloaded(n);
    }
    free(head);

    while (!job_cancelled()) {
        ssize_t n = recv(fd, io, IO_BUF, 0);
        if (n < 0) {
            if (errno == EINTR) continue;
            int e = errno;
            close(out); free(io); close(fd); return -5000 - e;
        }
        if (n == 0) break;
        uint8_t *p = io;
        ssize_t left = n;
        while (left > 0) {
            ssize_t w = write(out, p, (size_t)left);
            if (w < 0) {
                if (errno == EINTR) continue;
                int e = errno;
                close(out); free(io); close(fd); return -6000 - e;
            }
            p += w;
            left -= w;
        }
        add_downloaded((uint64_t)n);
    }

    fsync(out);
    close(out);
    free(io);
    close(fd);

    if (job_cancelled()) return -7000;

    struct stat done;
    if (stat(CACHE_FILE, &done) != 0) return -7001;
    if (expected_size && (uint64_t)done.st_size != expected_size) return -7002;
    return 0;
}

static void *job_worker(void *unused) {
    (void)unused;
    char url[URL_CAP];
    char expected[TITLE_CAP];
    uint64_t total;

    pthread_mutex_lock(&g_job_lock);
    strncpy(url, g_job.url, sizeof(url) - 1); url[sizeof(url) - 1] = 0;
    strncpy(expected, g_job.expected_title, sizeof(expected) - 1); expected[sizeof(expected) - 1] = 0;
    total = g_job.total;
    pthread_mutex_unlock(&g_job_lock);

    int r = download_from_relay(url, total);
    if (r) {
        if (r == -7000) {
            pthread_mutex_lock(&g_job_lock); g_job.state = JOB_CANCELLED; g_job.error_code = r; pthread_mutex_unlock(&g_job_lock);
            notify_user("Tayson Payload: download cancelado");
        } else {
            set_job_error(r, 0);
            notify_user("Tayson Payload: falha no download");
        }
        return NULL;
    }

    appinst_init_fn app_init;
    appinst_install_fn app_install;
    appinst_title_fn app_title;
    r = load_appinst(&app_init, &app_install, &app_title);
    if (r) { set_job_error(r, 0); notify_user("Tayson Payload: AppInstUtil indisponivel"); return NULL; }

    r = app_init();
    if (r < 0) {
        /* Some firmwares report an already-initialized condition; installation is the decisive test. */
        pthread_mutex_lock(&g_job_lock); g_job.error_code = r; pthread_mutex_unlock(&g_job_lock);
    }

    if (app_title) {
        char tid[TITLE_CAP] = {0};
        int is_app = 0;
        int tr = app_title(CACHE_FILE, tid, &is_app);
        if (tr == 0 && tid[0]) {
            pthread_mutex_lock(&g_job_lock);
            strncpy(g_job.detected_title, tid, sizeof(g_job.detected_title) - 1);
            pthread_mutex_unlock(&g_job_lock);
            if (expected[0] && strcasecmp(expected, tid) != 0) {
                set_job_error(-8001, 0);
                notify_user("Tayson Payload: Title ID nao confere");
                return NULL;
            }
        }
    }

    pthread_mutex_lock(&g_job_lock); g_job.state = JOB_INSTALLING; pthread_mutex_unlock(&g_job_lock);
    notify_user("Tayson Payload: enviando PKG ao instalador local");
    int ir = app_install(CACHE_FILE, NULL);

    pthread_mutex_lock(&g_job_lock);
    g_job.install_code = ir;
    if (ir == 0) g_job.state = JOB_SUBMITTED;
    else { g_job.state = JOB_ERROR; g_job.error_code = ir; }
    pthread_mutex_unlock(&g_job_lock);

    if (ir == 0) notify_user("Tayson Payload: PKG aceito pelo instalador local");
    else notify_user("Tayson Payload: instalador local recusou o PKG");
    return NULL;
}

static const char *header_value(const char *req, const char *key, char *out, size_t cap) {
    if (!req || !key || !out || cap < 2) return NULL;
    size_t klen = strlen(key);
    const char *p = strstr(req, "\r\n");
    if (!p) return NULL;
    p += 2;
    while (*p) {
        const char *e = strstr(p, "\r\n");
        if (!e || e == p) break;
        size_t len = (size_t)(e - p);
        if (len > klen + 1 && strncasecmp(p, key, klen) == 0 && p[klen] == ':') {
            const char *v = p + klen + 1;
            while (v < e && (*v == ' ' || *v == '\t')) v++;
            size_t n = (size_t)(e - v);
            if (n >= cap) n = cap - 1;
            memcpy(out, v, n); out[n] = 0;
            return out;
        }
        p = e + 2;
    }
    return NULL;
}

static void handle_status(int fd) {
    payload_job j;
    pthread_mutex_lock(&g_job_lock); j = g_job; pthread_mutex_unlock(&g_job_lock);
    char resp[1024];
    snprintf(resp, sizeof(resp),
        "TAYSONP/1 STATUS state=%s bytes=%llu total=%llu http=%d error=0x%08X install=0x%08X title=%s file=%s\n",
        state_name(j.state), (unsigned long long)j.downloaded, (unsigned long long)j.total,
        j.http_status, (unsigned)j.error_code, (unsigned)j.install_code,
        j.detected_title[0] ? j.detected_title : "-", j.name[0] ? j.name : "-");
    respond_text(fd, resp);
}

static void handle_client(int fd) {
    char req[16 * 1024 + 1];
    size_t used = 0;
    while (used < sizeof(req) - 1) {
        ssize_t n = recv(fd, req + used, sizeof(req) - 1 - used, 0);
        if (n <= 0) break;
        used += (size_t)n;
        req[used] = 0;
        if (strstr(req, "\r\n\r\n") || strstr(req, "\n\n")) break;
    }
    req[used] = 0;

    if (strncmp(req, "TAYSONP/1 PING", 14) == 0) {
        char out[96];
        snprintf(out, sizeof(out), "TAYSONP/1 OK payload=%s port=%d\n", TAYSON_PAYLOAD_VERSION, CONTROL_PORT);
        respond_text(fd, out);
        return;
    }
    if (strncmp(req, "TAYSONP/1 STATUS", 16) == 0) { handle_status(fd); return; }
    if (strncmp(req, "TAYSONP/1 STOP", 14) == 0) {
        pthread_mutex_lock(&g_job_lock); g_job.cancel = true; pthread_mutex_unlock(&g_job_lock);
        respond_text(fd, "TAYSONP/1 OK stopping\n");
        return;
    }
    if (strncmp(req, "TAYSONP/1 RESET", 15) == 0) {
        pthread_mutex_lock(&g_job_lock);
        if (g_job.state == JOB_DOWNLOADING || g_job.state == JOB_INSTALLING) {
            pthread_mutex_unlock(&g_job_lock);
            respond_text(fd, "TAYSONP/1 ERR busy\n");
            return;
        }
        memset(&g_job, 0, sizeof(g_job));
        g_job.state = JOB_IDLE;
        pthread_mutex_unlock(&g_job_lock);
        unlink(CACHE_FILE);
        respond_text(fd, "TAYSONP/1 OK reset\n");
        return;
    }
    if (strncmp(req, "TAYSONP/1 EXIT", 14) == 0) {
        pthread_mutex_lock(&g_job_lock);
        bool busy = g_job.state == JOB_DOWNLOADING || g_job.state == JOB_INSTALLING;
        pthread_mutex_unlock(&g_job_lock);
        if (busy) respond_text(fd, "TAYSONP/1 ERR busy\n");
        else { g_server_running = false; respond_text(fd, "TAYSONP/1 OK exiting\n"); }
        return;
    }
    if (strncmp(req, "TAYSONP/1 START", 15) == 0) {
        char url[URL_CAP] = {0}, sizebuf[32] = {0}, name[NAME_CAP] = {0}, title[TITLE_CAP] = {0};
        if (!header_value(req, "Url", url, sizeof(url))) { respond_text(fd, "TAYSONP/1 ERR missing-url\n"); return; }
        header_value(req, "Size", sizebuf, sizeof(sizebuf));
        header_value(req, "Name", name, sizeof(name));
        header_value(req, "Title", title, sizeof(title));
        if (strncmp(url, "http://", 7) != 0) { respond_text(fd, "TAYSONP/1 ERR relay-http-required\n"); return; }

        pthread_mutex_lock(&g_job_lock);
        if (g_job.state == JOB_DOWNLOADING || g_job.state == JOB_INSTALLING) {
            pthread_mutex_unlock(&g_job_lock);
            respond_text(fd, "TAYSONP/1 ERR busy\n");
            return;
        }
        memset(&g_job, 0, sizeof(g_job));
        g_job.state = JOB_DOWNLOADING;
        strncpy(g_job.url, url, sizeof(g_job.url) - 1);
        strncpy(g_job.name, name, sizeof(g_job.name) - 1);
        strncpy(g_job.expected_title, title, sizeof(g_job.expected_title) - 1);
        g_job.total = sizebuf[0] ? strtoull(sizebuf, NULL, 10) : 0;
        pthread_mutex_unlock(&g_job_lock);

        pthread_t th;
        if (pthread_create(&th, NULL, job_worker, NULL) != 0) {
            set_job_error(-9001, 0);
            respond_text(fd, "TAYSONP/1 ERR thread\n");
            return;
        }
        pthread_detach(th);
        respond_text(fd, "TAYSONP/1 ACCEPTED job=1 path=" CACHE_FILE "\n");
        return;
    }

    respond_text(fd, "TAYSONP/1 ERR bad-command\n");
}

int main(int argc, char **argv) {
    (void)argc; (void)argv;
    signal(SIGPIPE, SIG_IGN);
    memset(&g_job, 0, sizeof(g_job));
    g_job.state = JOB_IDLE;

    ensure_dir("/data/tayson");
    ensure_dir(CACHE_DIR);

    int s = socket(AF_INET, SOCK_STREAM, 0);
    if (s < 0) return 1;
    int one = 1;
    setsockopt(s, SOL_SOCKET, SO_REUSEADDR, &one, sizeof(one));

    struct sockaddr_in addr;
    memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    addr.sin_port = htons(CONTROL_PORT);
    if (bind(s, (struct sockaddr *)&addr, sizeof(addr)) < 0) { close(s); return 2; }
    if (listen(s, 8) < 0) { close(s); return 3; }

    notify_user("Tayson Payload Installer 0.1.0 ativo - porta 9096");
    while (g_server_running) {
        int c = accept(s, NULL, NULL);
        if (c < 0) {
            if (errno == EINTR) continue;
            usleep(100000);
            continue;
        }
        handle_client(c);
        close(c);
    }
    close(s);
    notify_user("Tayson Payload Installer encerrado");
    return 0;
}
