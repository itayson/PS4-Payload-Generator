#include <arpa/inet.h>
#include <errno.h>
#include <netinet/in.h>
#include <pthread.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <sys/mman.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/time.h>
#include <unistd.h>

#include <orbis/Http.h>
#include <orbis/libkernel.h>
#include <orbis/Net.h>
#include <orbis/Ssl.h>
#include <orbis/Sysmodule.h>

#define RECEIVER_VERSION "1.4.2"
#define CONTROL_PORT 9095
#define PROXY_PORT 32112
#define MAX_CONTROL_PAYLOAD (96 * 1024)
#define MAX_REQUEST_HEADER 16384
#define MAX_JOBS 8
#define IO_BUFFER_SIZE (512 * 1024)
/* SceHttp can reject requests from threads with too little remaining stack
 * (0x80431076 / SCE_HTTP_ERROR_INSUFFICIENT_STACKSIZE). The control-path
 * preflight runs on the main thread, but BGFT fetches are served by these
 * proxy workers, so reserve a dedicated stack only for those workers. */
#define PROXY_WORKER_STACK_SIZE (1024 * 1024)
#define NET_POOL_SIZE (2 * 1024 * 1024)
#define SSL_POOL_SIZE (1024 * 1024)
#define HTTP_POOL_SIZE (2 * 1024 * 1024)
#define BGFT_HEAP_SIZE (1024 * 1024)
#define JOB_MAGIC 0x54594A31U /* TYJ1 */
#define JOB_VERSION 1U
#define BGFT_TASK_OPTION_DISABLE_CDN_QUERY_PARAM 0x10000
#define BGFT_INVALID_TASK_ID (-1)
#define BGFT_ERROR_CONTENT_ALREADY_DOWNLOADING 0x80990086U
#define BGFT_SUBTYPE_GAME 6
#define BGFT_SUBTYPE_GAME_AC 7
#define BGFT_SUBTYPE_GAME_PATCH 8
#define BGFT_SUBTYPE_PACKAGE 12
#define ORBIS_KERNEL_PRIO_FIFO_NORMAL 0x2BC
/* PS4 SceHttps verification flags. 0x3F matches the six checks disabled by
 * the reference Remote Package Installer before talking to external CDNs:
 * server/client verify, CN, validity dates and known-CA validation. */
#define RECEIVER_HTTPS_VERIFY_FLAGS 0x3FU

/* PS4 libSceNet socket ABI. */
#define RECEIVER_NET_SOL_SOCKET 0xFFFF
#define RECEIVER_NET_SO_REUSEADDR 0x00000004
#define RECEIVER_NET_SO_SNDTIMEO 0x1105
#define RECEIVER_NET_SO_RCVTIMEO 0x1106
#define RECEIVER_NET_SO_SNDBUF 0x1001
#define RECEIVER_NET_SO_RCVBUF 0x1002
#define RECEIVER_SOCKET_SNDBUF (2 * 1024 * 1024)
#define RECEIVER_SOCKET_RCVBUF (512 * 1024)

/* Adaptive multi-range accelerator. Large BGFT reads are prefetched from the
 * upstream CDN through several independent Range requests and delivered to
 * BGFT in strict byte order. A global gate caps concurrent upstream fetchers
 * so RAM/SSL usage stays bounded even when BGFT opens more than one request. */
#define SEGMENT_SIZE (32 * 1024 * 1024)
#define SEGMENT_WINDOW 2
#define SEGMENT_MIN_BODY (64 * 1024 * 1024)
#define MAX_SEGMENT_FETCHERS 2
#define RATE_LIMIT_RETRIES 2
#define RATE_LIMIT_BACKOFF_BASE_SEC 4

/* Persistent job registry. Only incomplete jobs are stored. The URL/header
 * context is required because BGFT resumes through the Receiver's localhost
 * proxy after the Receiver process itself has been restarted. */
#define PERSIST_DIR "/data/tayson_receiver"
#define PERSIST_PATH "/data/tayson_receiver/jobs-v1.bin"
#define PERSIST_TMP_PATH "/data/tayson_receiver/jobs-v1.tmp"
#define PERSIST_MAGIC 0x54595031U /* TYP1 */
#define PERSIST_VERSION 1U

typedef struct {
    uint8_t sin_len;
    uint8_t sin_family;
    uint16_t sin_port;
    uint32_t sin_addr;
    uint16_t sin_vport;
    char sin_zero[6];
} receiver_net_sockaddr_in;

/* OpenOrbis' public bgft type still exposes packageSize as uint32_t. The
 * internal debug registration ABI used by Remote Package Installer uses an
 * unsigned long on x86_64, so keep the proven layout here. */
typedef struct {
    void *mem;
    unsigned long size;
} receiver_bgft_init_params;

typedef struct {
    int user_id;
    int entitlement_type;
    const char *id;
    const char *content_url;
    const char *content_ex_url;
    const char *content_name;
    const char *icon_path;
    const char *sku_id;
    int option;
    const char *playgo_scenario_id;
    const char *release_date;
    const char *package_type;
    const char *package_sub_type;
    unsigned long package_size;
} receiver_bgft_download_param;

typedef int32_t (*receiver_appinst_init_fn)(void);
typedef int32_t (*receiver_appinst_exists_fn)(const char *title_id, int *exists);
typedef int32_t (*receiver_user_init_fn)(void *params);
typedef int32_t (*receiver_user_get_fg_fn)(int *user_id);
typedef int32_t (*receiver_bgft_init_fn)(receiver_bgft_init_params *params);
typedef int32_t (*receiver_bgft_register_fn)(receiver_bgft_download_param *params, int32_t *task_id);
typedef int32_t (*receiver_bgft_start_fn)(int32_t task_id);
typedef int32_t (*receiver_bgft_find_fn)(const char *content_id, int sub_type, int32_t *task_id);
typedef int32_t (*receiver_bgft_stop_fn)(int32_t task_id);
typedef int32_t (*receiver_bgft_unregister_fn)(int32_t task_id);
typedef void (*receiver_notify_fn)(int type, const char *text);
typedef int32_t (*receiver_daemon_register_fn)(void);

/* libjbc credential bridge. The implementation is linked as a separate PS4
 * object and is only invoked lazily when an install job arrives, preserving
 * the safe-start behavior introduced in 1.0.3. */
struct receiver_jbc_cred {
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

extern int jbc_get_cred(struct receiver_jbc_cred *cred);
extern int jbc_jailbreak_cred(struct receiver_jbc_cred *cred);
extern int jbc_set_cred(const struct receiver_jbc_cred *cred);

static receiver_appinst_init_fn p_sceAppInstUtilInitialize = NULL;
static receiver_appinst_exists_fn p_sceAppInstUtilAppExists = NULL;
static receiver_user_init_fn p_sceUserServiceInitialize = NULL;
static receiver_user_get_fg_fn p_sceUserServiceGetForegroundUser = NULL;
static receiver_bgft_init_fn p_sceBgftServiceIntInit = NULL;
static receiver_bgft_register_fn p_sceBgftServiceIntDebugDownloadRegisterPkg = NULL;
static receiver_bgft_start_fn p_sceBgftServiceIntDownloadStartTask = NULL;
static receiver_bgft_find_fn p_sceBgftServiceDownloadFindTaskByContentId = NULL;
static receiver_bgft_stop_fn p_sceBgftServiceIntDownloadStopTask = NULL;
static receiver_bgft_unregister_fn p_sceBgftServiceIntDownloadUnregisterTask = NULL;
static receiver_notify_fn p_sceSysUtilSendSystemNotificationWithText = NULL;
static receiver_daemon_register_fn p_sceSystemServiceRegisterDaemon = NULL;

typedef struct {
    uint32_t id;
    int used;
    int task_id;
    int upstream_status;
    int last_error;
    int active_connections;
    uint64_t frontier_bytes;
    uint64_t total_served;
    uint64_t created_us;
    uint64_t last_activity_us;
    uint64_t package_size;
    char upstream_url[8192];
    char user_agent[1024];
    char referer[4096];
    char cookie[8192];
    char extra_headers[8192];
    char content_id[128];
    char file_name[512];
    char package_type[32];
    int parallel_disabled;
    int rate_limit_hits;
} receiver_job;

typedef struct {
    uint32_t magic;
    uint32_t version;
    uint32_t record_size;
    uint32_t max_jobs;
    uint32_t next_job_id;
    uint32_t checksum;
} receiver_persist_header;

static receiver_job g_jobs[MAX_JOBS];
static pthread_mutex_t g_jobs_mutex = PTHREAD_MUTEX_INITIALIZER;
static uint32_t g_next_job_id = 1;
static int g_net_pool = -1;
static int g_ssl_ctx = -1;
static int g_http_ctx = -1;
static void *g_bgft_heap = NULL;
static int g_transfer_services_ready = 0;
static pthread_mutex_t g_service_init_mutex = PTHREAD_MUTEX_INITIALIZER;
static char g_service_stage[40] = "idle";
static int32_t g_mod_system_service = -1;
static int32_t g_mod_user_service = -1;
static int32_t g_mod_appinst = -1;
static int32_t g_mod_sysutil = -1;
static int32_t g_mod_bgft = -1;
static int g_privileged_credentials_ready = 0;
static int g_daemon_registered = 0;
static int g_daemon_register_error = 0;
static pthread_mutex_t g_segment_gate_mutex = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t g_segment_gate_cond = PTHREAD_COND_INITIALIZER;
static int g_segment_fetchers = 0;
static int g_restored_jobs = 0;
static pthread_mutex_t g_persist_mutex = PTHREAD_MUTEX_INITIALIZER;

static uint32_t persist_checksum(const void *data, size_t len) {
    const uint8_t *p = (const uint8_t *)data;
    uint32_t h = 2166136261U;
    for (size_t i = 0; i < len; i++) {
        h ^= p[i];
        h *= 16777619U;
    }
    return h;
}

static int save_persisted_jobs(void) {
    pthread_mutex_lock(&g_persist_mutex);
    int result = -1;
    receiver_job *snapshot = (receiver_job *)calloc(MAX_JOBS, sizeof(receiver_job));
    if (!snapshot) goto done;
    uint32_t next_id = 1;
    pthread_mutex_lock(&g_jobs_mutex);
    memcpy(snapshot, g_jobs, sizeof(g_jobs));
    next_id = g_next_job_id;
    pthread_mutex_unlock(&g_jobs_mutex);

    /* Completed jobs stay visible in RAM for status, but their signed URLs and
     * cookies do not need to survive a restart. */
    for (int i = 0; i < MAX_JOBS; i++) {
        if (!snapshot[i].used) continue;
        if (snapshot[i].package_size > 0 && snapshot[i].total_served >= snapshot[i].package_size) {
            memset(&snapshot[i], 0, sizeof(snapshot[i]));
            continue;
        }
        snapshot[i].active_connections = 0;
        snapshot[i].created_us = 0;
        snapshot[i].last_activity_us = 0;
    }

    receiver_persist_header hdr;
    memset(&hdr, 0, sizeof(hdr));
    hdr.magic = PERSIST_MAGIC;
    hdr.version = PERSIST_VERSION;
    hdr.record_size = (uint32_t)sizeof(receiver_job);
    hdr.max_jobs = MAX_JOBS;
    hdr.next_job_id = next_id ? next_id : 1;
    hdr.checksum = persist_checksum(snapshot, sizeof(g_jobs));

    mkdir(PERSIST_DIR, 0700);
    FILE *f = fopen(PERSIST_TMP_PATH, "wb");
    if (!f) { free(snapshot); goto done; }
    int ok = fwrite(&hdr, 1, sizeof(hdr), f) == sizeof(hdr) &&
             fwrite(snapshot, 1, sizeof(g_jobs), f) == sizeof(g_jobs);
    if (ok && fflush(f) == 0) fsync(fileno(f));
    fclose(f);
    free(snapshot);
    if (!ok) { unlink(PERSIST_TMP_PATH); goto done; }
    chmod(PERSIST_TMP_PATH, 0600);
    if (rename(PERSIST_TMP_PATH, PERSIST_PATH) != 0) { unlink(PERSIST_TMP_PATH); goto done; }
    chmod(PERSIST_PATH, 0600);
    result = 0;
done:
    pthread_mutex_unlock(&g_persist_mutex);
    return result;
}

static int load_persisted_jobs(void) {
    pthread_mutex_lock(&g_persist_mutex);
    int result = 0;
    FILE *f = fopen(PERSIST_PATH, "rb");
    if (!f) goto done;
    receiver_persist_header hdr;
    memset(&hdr, 0, sizeof(hdr));
    if (fread(&hdr, 1, sizeof(hdr), f) != sizeof(hdr) ||
        hdr.magic != PERSIST_MAGIC || hdr.version != PERSIST_VERSION ||
        hdr.record_size != sizeof(receiver_job) || hdr.max_jobs != MAX_JOBS) {
        fclose(f);
        result = -1;
        goto done;
    }
    receiver_job *snapshot = (receiver_job *)calloc(MAX_JOBS, sizeof(receiver_job));
    if (!snapshot) { fclose(f); result = -1; goto done; }
    int ok = fread(snapshot, 1, sizeof(g_jobs), f) == sizeof(g_jobs);
    fclose(f);
    if (!ok || persist_checksum(snapshot, sizeof(g_jobs)) != hdr.checksum) {
        free(snapshot);
        result = -1;
        goto done;
    }

    uint32_t max_id = 0;
    int restored = 0;
    uint64_t now = sceKernelGetProcessTime();
    pthread_mutex_lock(&g_jobs_mutex);
    memset(g_jobs, 0, sizeof(g_jobs));
    for (int i = 0; i < MAX_JOBS; i++) {
        receiver_job *j = &snapshot[i];
        if (!j->used || j->id == 0 || j->package_size == 0 ||
            !j->content_id[0] || !j->file_name[0] || !j->package_type[0] ||
            (strncmp(j->upstream_url, "http://", 7) != 0 && strncmp(j->upstream_url, "https://", 8) != 0))
            continue;
        j->active_connections = 0;
        j->last_error = 0;
        j->created_us = now;
        j->last_activity_us = now;
        g_jobs[i] = *j;
        if (j->id > max_id) max_id = j->id;
        restored++;
    }
    uint32_t candidate = hdr.next_job_id;
    if (candidate == 0 || candidate <= max_id) candidate = max_id + 1;
    if (candidate == 0) candidate = 1;
    g_next_job_id = candidate;
    pthread_mutex_unlock(&g_jobs_mutex);
    free(snapshot);
    g_restored_jobs = restored;
    result = restored;
done:
    pthread_mutex_unlock(&g_persist_mutex);
    return result;
}

static void notify_text(const char *text) {
    if (p_sceSysUtilSendSystemNotificationWithText) p_sceSysUtilSendSystemNotificationWithText(222, text);
}

static void set_service_stage(const char *stage) {
    snprintf(g_service_stage, sizeof(g_service_stage), "%s", stage ? stage : "unknown");
}

/* GoldHEN/HEN homebrew may expose system libraries either directly under
 * /system/common/lib or through the process sandbox alias. AppInstUtil also
 * depends on SystemService being loaded first. */
static int load_system_module(const char *name, int32_t *handle_out) {
    char path[256];
    snprintf(path, sizeof(path), "/system/common/lib/%s", name);
    int32_t h = (int32_t)sceKernelLoadStartModule(path, 0, NULL, 0, NULL, NULL);
    if (h >= 0) {
        if (handle_out) *handle_out = h;
        return 0;
    }

    const char *word = sceKernelGetFsSandboxRandomWord();
    if (word && word[0]) {
        snprintf(path, sizeof(path), "/%s/common/lib/%s", word, name);
        int32_t alt = (int32_t)sceKernelLoadStartModule(path, 0, NULL, 0, NULL, NULL);
        if (alt >= 0) {
            if (handle_out) *handle_out = alt;
            return 0;
        }
        return alt;
    }
    return h;
}

static int resolve_symbol(int32_t handle, const char *symbol, void **out) {
    if (handle < 0 || !out) return -1;
    *out = NULL;
    int32_t r = sceKernelDlsym(handle, symbol, out);
    return (r < 0 || !*out) ? (r < 0 ? r : -1) : 0;
}

static void notify_code(const char *prefix, int code) {
    char msg[192];
    snprintf(msg, sizeof(msg), "%s 0x%08X", prefix, (unsigned int)code);
    notify_text(msg);
}

static int is_private_ipv4(uint32_t network_order_addr) {
    uint32_t a = sceNetNtohl(network_order_addr);
    uint8_t o1 = (uint8_t)(a >> 24);
    uint8_t o2 = (uint8_t)(a >> 16);
    if (o1 == 10 || o1 == 127) return 1;
    if (o1 == 172 && o2 >= 16 && o2 <= 31) return 1;
    if (o1 == 192 && o2 == 168) return 1;
    return 0;
}

static int send_all(int fd, const void *buf, size_t len) {
    const uint8_t *p = (const uint8_t *)buf;
    while (len > 0) {
        ssize_t n = sceNetSend(fd, p, len, 0);
        if (n < 0) return -1;
        if (n == 0) return -1;
        p += (size_t)n;
        len -= (size_t)n;
    }
    return 0;
}

static int recv_all(int fd, void *buf, size_t len) {
    uint8_t *p = (uint8_t *)buf;
    while (len > 0) {
        ssize_t n = sceNetRecv(fd, p, len, 0);
        if (n < 0) return -1;
        if (n == 0) return -1;
        p += (size_t)n;
        len -= (size_t)n;
    }
    return 0;
}

static int recv_line(int fd, char *buf, size_t cap) {
    if (!buf || cap < 2) return -1;
    size_t used = 0;
    while (used + 1 < cap) {
        char c;
        ssize_t n = sceNetRecv(fd, &c, 1, 0);
        if (n <= 0) return -1;
        if (c == '\n') {
            if (used > 0 && buf[used - 1] == '\r') used--;
            buf[used] = 0;
            return (int)used;
        }
        buf[used++] = c;
    }
    buf[cap - 1] = 0;
    return -1;
}

static uint32_t read_u32_be(const uint8_t **cursor, const uint8_t *end, int *ok) {
    if (!*ok || (size_t)(end - *cursor) < 4) { *ok = 0; return 0; }
    const uint8_t *p = *cursor;
    *cursor += 4;
    return ((uint32_t)p[0] << 24) | ((uint32_t)p[1] << 16) | ((uint32_t)p[2] << 8) | (uint32_t)p[3];
}

static uint64_t read_u64_be(const uint8_t **cursor, const uint8_t *end, int *ok) {
    uint64_t hi = read_u32_be(cursor, end, ok);
    uint64_t lo = read_u32_be(cursor, end, ok);
    return (hi << 32) | lo;
}

static int read_wire_string(const uint8_t **cursor, const uint8_t *end, char *dst, size_t dst_cap, int *ok) {
    uint32_t len = read_u32_be(cursor, end, ok);
    if (!*ok || len >= dst_cap || (size_t)(end - *cursor) < len) { *ok = 0; return -1; }
    memcpy(dst, *cursor, len);
    dst[len] = 0;
    *cursor += len;
    return 0;
}

static int parse_job_packet(const uint8_t *buf, size_t len, receiver_job *out) {
    const uint8_t *p = buf;
    const uint8_t *end = buf + len;
    int ok = 1;
    uint32_t magic = read_u32_be(&p, end, &ok);
    uint32_t version = read_u32_be(&p, end, &ok);
    uint64_t package_size = read_u64_be(&p, end, &ok);
    if (!ok || magic != JOB_MAGIC || version != JOB_VERSION || package_size == 0) return -1;

    memset(out, 0, sizeof(*out));
    out->task_id = BGFT_INVALID_TASK_ID;
    out->package_size = package_size;
    if (read_wire_string(&p, end, out->upstream_url, sizeof(out->upstream_url), &ok) ||
        read_wire_string(&p, end, out->user_agent, sizeof(out->user_agent), &ok) ||
        read_wire_string(&p, end, out->referer, sizeof(out->referer), &ok) ||
        read_wire_string(&p, end, out->cookie, sizeof(out->cookie), &ok) ||
        read_wire_string(&p, end, out->extra_headers, sizeof(out->extra_headers), &ok) ||
        read_wire_string(&p, end, out->content_id, sizeof(out->content_id), &ok) ||
        read_wire_string(&p, end, out->file_name, sizeof(out->file_name), &ok) ||
        read_wire_string(&p, end, out->package_type, sizeof(out->package_type), &ok)) return -1;

    if (!ok || p != end) return -1;
    if (strncmp(out->upstream_url, "http://", 7) != 0 && strncmp(out->upstream_url, "https://", 8) != 0) return -1;
    if (!out->content_id[0] || !out->file_name[0] || !out->package_type[0]) return -1;
    if (!out->user_agent[0]) snprintf(out->user_agent, sizeof(out->user_agent), "TaysonPS4Receiver/%s", RECEIVER_VERSION);
    /* Mocha has demonstrated HTTP 429 throttling under parallel Range fetches.
     * Keep the proven large-buffer single-stream path for that provider. */
    if (strstr(out->upstream_url, "mocha.my")) out->parallel_disabled = 1;
    return 0;
}

static receiver_job *find_job_locked(uint32_t id) {
    for (int i = 0; i < MAX_JOBS; i++) {
        if (g_jobs[i].used && g_jobs[i].id == id) return &g_jobs[i];
    }
    return NULL;
}

static int find_duplicate_active_job(const receiver_job *source, uint32_t *id_out, int *task_out) {
    int found = 0;
    pthread_mutex_lock(&g_jobs_mutex);
    for (int i = 0; i < MAX_JOBS; i++) {
        receiver_job *j = &g_jobs[i];
        if (!j->used || j->task_id == BGFT_INVALID_TASK_ID || j->last_error != 0) continue;
        if (j->package_size != source->package_size) continue;
        if (strcmp(j->content_id, source->content_id) != 0) continue;
        if (j->total_served >= j->package_size) continue;
        if (id_out) *id_out = j->id;
        if (task_out) *task_out = j->task_id;
        found = 1;
        break;
    }
    pthread_mutex_unlock(&g_jobs_mutex);
    return found;
}

static int allocate_job(const receiver_job *source, uint32_t *id_out) {
    pthread_mutex_lock(&g_jobs_mutex);
    int index = -1;
    for (int i = 0; i < MAX_JOBS; i++) {
        if (!g_jobs[i].used) { index = i; break; }
    }
    /* Reuse a completed, inactive slot before rejecting a new job. Active or
     * paused incomplete BGFT jobs are never reclaimed. */
    if (index < 0) {
        for (int i = 0; i < MAX_JOBS; i++) {
            if (g_jobs[i].used && g_jobs[i].active_connections == 0 &&
                g_jobs[i].package_size > 0 && g_jobs[i].total_served >= g_jobs[i].package_size) {
                index = i;
                break;
            }
        }
    }
    if (index < 0) {
        pthread_mutex_unlock(&g_jobs_mutex);
        return -1;
    }
    g_jobs[index] = *source;
    g_jobs[index].used = 1;
    g_jobs[index].created_us = sceKernelGetProcessTime();
    g_jobs[index].last_activity_us = g_jobs[index].created_us;
    g_jobs[index].id = g_next_job_id++;
    if (g_next_job_id == 0) g_next_job_id = 1;
    *id_out = g_jobs[index].id;
    pthread_mutex_unlock(&g_jobs_mutex);
    return 0;
}

static void drop_job(uint32_t id) {
    pthread_mutex_lock(&g_jobs_mutex);
    receiver_job *j = find_job_locked(id);
    if (j) memset(j, 0, sizeof(*j));
    pthread_mutex_unlock(&g_jobs_mutex);
    save_persisted_jobs();
}

static int copy_job(uint32_t id, receiver_job *out) {
    int found = 0;
    pthread_mutex_lock(&g_jobs_mutex);
    receiver_job *j = find_job_locked(id);
    if (j) { *out = *j; found = 1; }
    pthread_mutex_unlock(&g_jobs_mutex);
    return found ? 0 : -1;
}

static void set_job_task(uint32_t id, int task_id, int upstream_status) {
    pthread_mutex_lock(&g_jobs_mutex);
    receiver_job *j = find_job_locked(id);
    if (j) { j->task_id = task_id; j->upstream_status = upstream_status; }
    pthread_mutex_unlock(&g_jobs_mutex);
    save_persisted_jobs();
}

static void job_disable_segmented(uint32_t id) {
    pthread_mutex_lock(&g_jobs_mutex);
    receiver_job *j = find_job_locked(id);
    if (j) { j->parallel_disabled = 1; j->rate_limit_hits++; }
    pthread_mutex_unlock(&g_jobs_mutex);
}

static void job_transfer_begin(uint32_t id) {
    pthread_mutex_lock(&g_jobs_mutex);
    receiver_job *j = find_job_locked(id);
    if (j) {
        j->active_connections++;
        j->last_error = 0;
        j->last_activity_us = sceKernelGetProcessTime();
    }
    pthread_mutex_unlock(&g_jobs_mutex);
}

static void job_transfer_progress(uint32_t id, uint64_t absolute_end, uint64_t bytes) {
    pthread_mutex_lock(&g_jobs_mutex);
    receiver_job *j = find_job_locked(id);
    if (j) {
        j->total_served += bytes;
        if (absolute_end > j->frontier_bytes) j->frontier_bytes = absolute_end;
        if (j->frontier_bytes > j->package_size) j->frontier_bytes = j->package_size;
        j->last_activity_us = sceKernelGetProcessTime();
    }
    pthread_mutex_unlock(&g_jobs_mutex);
}

static void job_transfer_end(uint32_t id, int error, int upstream_status) {
    pthread_mutex_lock(&g_jobs_mutex);
    receiver_job *j = find_job_locked(id);
    if (j) {
        if (j->active_connections > 0) j->active_connections--;
        if (upstream_status > 0) j->upstream_status = upstream_status;
        j->last_error = error;
        j->last_activity_us = sceKernelGetProcessTime();
    }
    pthread_mutex_unlock(&g_jobs_mutex);
    save_persisted_jobs();
}

static const char *job_state(const receiver_job *j) {
    if (j->package_size > 0 && j->total_served >= j->package_size) return "complete";
    if (j->active_connections > 0) return "downloading";
    if (j->last_error != 0) return "error";
    return "queued";
}

static int respond_line(int fd, const char *line);

static void respond_status(int client) {
    receiver_job snapshot[MAX_JOBS];
    int count = 0;
    pthread_mutex_lock(&g_jobs_mutex);
    for (int i = 0; i < MAX_JOBS; i++) {
        if (g_jobs[i].used) snapshot[count++] = g_jobs[i];
    }
    pthread_mutex_unlock(&g_jobs_mutex);

    char line[768];
    int n = snprintf(line, sizeof(line), "TAYSON/1 STATUS receiver=%s count=%d restored=%d persist=1 daemon=%d daemonerr=%d\n", RECEIVER_VERSION, count, g_restored_jobs, g_daemon_registered, g_daemon_register_error);
    send_all(client, line, (size_t)n);
    for (int i = 0; i < count; i++) {
        receiver_job *j = &snapshot[i];
        n = snprintf(line, sizeof(line),
            "TAYSON/1 JOB id=%u task=%d size=%llu frontier=%llu served=%llu active=%d upstream=%d error=%d state=%s mode=%s content=%s\n",
            j->id, j->task_id,
            (unsigned long long)j->package_size,
            (unsigned long long)j->frontier_bytes,
            (unsigned long long)j->total_served,
            j->active_connections, j->upstream_status, j->last_error,
            job_state(j), j->parallel_disabled ? "single" : "adaptive", j->content_id);
        send_all(client, line, (size_t)n);
    }
    respond_line(client, "TAYSON/1 END\n");
}

static int load_control_network(void) {
    if (sceSysmoduleLoadModuleInternal(ORBIS_SYSMODULE_INTERNAL_NET) < 0) return -1;
    int ret = sceNetInit();
    if (ret < 0 && (unsigned int)ret != 0x80410004U) return ret;
    ret = sceNetPoolCreate("TaysonReceiver", NET_POOL_SIZE, 0);
    if (ret < 0) return ret;
    g_net_pool = ret;
    return 0;
}

static int load_http_stack(void) {
    if (g_http_ctx >= 0) return 0;
    if (sceSysmoduleLoadModuleInternal(ORBIS_SYSMODULE_INTERNAL_HTTP) < 0) return -1;
    if (sceSysmoduleLoadModuleInternal(ORBIS_SYSMODULE_INTERNAL_SSL) < 0) return -1;
    int ret = sceSslInit(SSL_POOL_SIZE);
    if (ret < 0) return ret;
    g_ssl_ctx = ret;
    ret = sceHttpInit(g_net_pool, g_ssl_ctx, HTTP_POOL_SIZE);
    if (ret < 0) return ret;
    g_http_ctx = ret;
    return 0;
}

static int ensure_http_stack_only(void) {
    pthread_mutex_lock(&g_service_init_mutex);
    int ret = load_http_stack();
    pthread_mutex_unlock(&g_service_init_mutex);
    return ret;
}

static int elevate_receiver_credentials(void) {
    if (g_privileged_credentials_ready) return 0;

    struct receiver_jbc_cred original;
    memset(&original, 0, sizeof(original));
    set_service_stage("jbc-get-cred");
    int ret = jbc_get_cred(&original);
    if (ret < 0) return ret;

    struct receiver_jbc_cred elevated = original;
    set_service_stage("jbc-jailbreak-cred");
    ret = jbc_jailbreak_cred(&elevated);
    if (ret < 0) return ret;

    /* Match the known-working AirPSX PS4 path: preserve the original root
     * directory as cwd while applying the elevated prison and capabilities. */
    elevated.cdir = original.rdir;
    set_service_stage("jbc-set-cred");
    ret = jbc_set_cred(&elevated);
    if (ret < 0) return ret;

    g_privileged_credentials_ready = 1;
    set_service_stage("jbc-ready");
    return 0;
}

static int enable_background_daemon_mode(void) {
    if (g_daemon_registered) return 0;
    if (!g_privileged_credentials_ready) {
        g_daemon_register_error = -1;
        return -1;
    }
    if (g_mod_system_service < 0) {
        set_service_stage("daemon-systemservice-load");
        int ret = load_system_module("libSceSystemService.sprx", &g_mod_system_service);
        if (ret < 0) { g_daemon_register_error = ret; return ret; }
    }
    if (!p_sceSystemServiceRegisterDaemon) {
        void *s = NULL;
        set_service_stage("daemon-symbol");
        int ret = resolve_symbol(g_mod_system_service, "sceSystemServiceRegisterDaemon", &s);
        if (ret < 0) { g_daemon_register_error = ret; return ret; }
        p_sceSystemServiceRegisterDaemon = (receiver_daemon_register_fn)s;
    }
    set_service_stage("daemon-register");
    int ret = p_sceSystemServiceRegisterDaemon();
    g_daemon_register_error = ret;
    if (ret >= 0) {
        g_daemon_registered = 1;
        g_daemon_register_error = 0;
        return 0;
    }
    return ret;
}

static int load_install_symbols(void) {
    void *s = NULL;
    int ret;

    /* SystemService must precede AppInstUtil. Without it AppInstUtil can fail
     * to load with SCE_KERNEL_ERROR_ENOENT (0x80020002). */
    set_service_stage("systemservice-load");
    if (g_mod_system_service < 0) {
        ret = load_system_module("libSceSystemService.sprx", &g_mod_system_service);
        if (ret < 0) return ret;
    }

    set_service_stage("userservice-load");
    ret = load_system_module("libSceUserService.sprx", &g_mod_user_service);
    if (ret < 0) return ret;
    set_service_stage("userservice-init-symbol");
    ret = resolve_symbol(g_mod_user_service, "sceUserServiceInitialize", &s);
    if (ret < 0) return ret;
    p_sceUserServiceInitialize = (receiver_user_init_fn)s;
    set_service_stage("userservice-user-symbol");
    ret = resolve_symbol(g_mod_user_service, "sceUserServiceGetForegroundUser", &s);
    if (ret < 0) return ret;
    p_sceUserServiceGetForegroundUser = (receiver_user_get_fg_fn)s;

    set_service_stage("appinst-load");
    ret = load_system_module("libSceAppInstUtil.sprx", &g_mod_appinst);
    if (ret < 0) return ret;
    set_service_stage("appinst-symbol");
    ret = resolve_symbol(g_mod_appinst, "sceAppInstUtilInitialize", &s);
    if (ret < 0) return ret;
    p_sceAppInstUtilInitialize = (receiver_appinst_init_fn)s;
    /* Optional but useful for patch/update diagnostics. */
    if (resolve_symbol(g_mod_appinst, "sceAppInstUtilAppExists", &s) == 0)
        p_sceAppInstUtilAppExists = (receiver_appinst_exists_fn)s;

    /* Notifications are optional and must never block transfer setup. */
    set_service_stage("sysutil-load");
    ret = load_system_module("libSceSysUtil.sprx", &g_mod_sysutil);
    if (ret >= 0 && resolve_symbol(g_mod_sysutil, "sceSysUtilSendSystemNotificationWithText", &s) == 0)
        p_sceSysUtilSendSystemNotificationWithText = (receiver_notify_fn)s;

    set_service_stage("bgft-load");
    ret = load_system_module("libSceBgft.sprx", &g_mod_bgft);
    if (ret < 0) return ret;
    set_service_stage("bgft-init-symbol");
    ret = resolve_symbol(g_mod_bgft, "sceBgftServiceIntInit", &s);
    if (ret < 0) return ret;
    p_sceBgftServiceIntInit = (receiver_bgft_init_fn)s;
    set_service_stage("bgft-register-symbol");
    ret = resolve_symbol(g_mod_bgft, "sceBgftServiceIntDebugDownloadRegisterPkg", &s);
    if (ret < 0) return ret;
    p_sceBgftServiceIntDebugDownloadRegisterPkg = (receiver_bgft_register_fn)s;
    set_service_stage("bgft-start-symbol");
    ret = resolve_symbol(g_mod_bgft, "sceBgftServiceIntDownloadStartTask", &s);
    if (ret < 0) return ret;
    p_sceBgftServiceIntDownloadStartTask = (receiver_bgft_start_fn)s;

    /* Recovery helpers are optional: older firmwares may expose a smaller
     * symbol set. Normal installs keep working even if one is unavailable. */
    if (resolve_symbol(g_mod_bgft, "sceBgftServiceDownloadFindTaskByContentId", &s) == 0)
        p_sceBgftServiceDownloadFindTaskByContentId = (receiver_bgft_find_fn)s;
    if (resolve_symbol(g_mod_bgft, "sceBgftServiceIntDownloadStopTask", &s) == 0)
        p_sceBgftServiceIntDownloadStopTask = (receiver_bgft_stop_fn)s;
    if (resolve_symbol(g_mod_bgft, "sceBgftServiceIntDownloadUnregisterTask", &s) == 0)
        p_sceBgftServiceIntDownloadUnregisterTask = (receiver_bgft_unregister_fn)s;
    return 0;
}

static int init_bgft(void) {
    struct { uint32_t priority; } user_params = { ORBIS_KERNEL_PRIO_FIFO_NORMAL };
    set_service_stage("userservice-init");
    int ret = p_sceUserServiceInitialize ? p_sceUserServiceInitialize(&user_params) : -1;
    if (ret && (unsigned int)ret != 0x80960003U) {
        notify_code("Tayson Receiver: UserService", ret);
        return ret;
    }

    set_service_stage("appinst-init");
    ret = p_sceAppInstUtilInitialize ? p_sceAppInstUtilInitialize() : -1;
    if (ret && (unsigned int)ret != 0x80990001U) {
        notify_code("Tayson Receiver: AppInst", ret);
        return ret;
    }

    g_bgft_heap = mmap(NULL, BGFT_HEAP_SIZE, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (g_bgft_heap == MAP_FAILED) { g_bgft_heap = NULL; return -1; }
    receiver_bgft_init_params params;
    params.mem = g_bgft_heap;
    params.size = BGFT_HEAP_SIZE;
    set_service_stage("bgft-init");
    ret = p_sceBgftServiceIntInit ? p_sceBgftServiceIntInit(&params) : -1;
    if (ret && (unsigned int)ret != 0x80990001U) {
        notify_code("Tayson Receiver: BGFT init", ret);
        return ret;
    }
    return 0;
}

static int ensure_transfer_services(void) {
    pthread_mutex_lock(&g_service_init_mutex);
    if (g_transfer_services_ready) { pthread_mutex_unlock(&g_service_init_mutex); return 0; }
    set_service_stage("http-stack");
    int ret = load_http_stack();
    if (!ret) ret = elevate_receiver_credentials();
    if (!ret) {
        int daemon_ret = enable_background_daemon_mode();
        if (daemon_ret < 0) g_daemon_register_error = daemon_ret;
    }
    if (!ret) ret = load_install_symbols();
    if (!ret) ret = init_bgft();
    if (!ret) {
        g_transfer_services_ready = 1;
        set_service_stage("ready");
    }
    pthread_mutex_unlock(&g_service_init_mutex);
    return ret;
}

static int find_header(const char *headers, const char *name, char *out, size_t out_size) {
    size_t name_len = strlen(name);
    const char *p = headers;
    while (*p) {
        const char *line_end = strstr(p, "\r\n");
        if (!line_end) break;
        size_t line_len = (size_t)(line_end - p);
        if (line_len > name_len + 1 && strncasecmp(p, name, name_len) == 0 && p[name_len] == ':') {
            const char *v = p + name_len + 1;
            while (v < line_end && (*v == ' ' || *v == '\t')) v++;
            size_t n = (size_t)(line_end - v);
            if (n >= out_size) n = out_size - 1;
            memcpy(out, v, n);
            out[n] = 0;
            return 0;
        }
        p = line_end + 2;
    }
    return -1;
}

static int parse_range(const char *value, uint64_t *start, uint64_t *end, int *has_end) {
    if (!value || strncasecmp(value, "bytes=", 6) != 0) return -1;
    const char *p = value + 6;
    char *ep = NULL;
    unsigned long long a = strtoull(p, &ep, 10);
    if (!ep || ep == p || *ep != '-') return -1;
    *start = (uint64_t)a;
    ep++;
    if (*ep >= '0' && *ep <= '9') {
        char *ep2 = NULL;
        unsigned long long b = strtoull(ep, &ep2, 10);
        if (!ep2 || ep2 == ep || b < a) return -1;
        *end = (uint64_t)b;
        *has_end = 1;
    } else {
        *end = 0;
        *has_end = 0;
    }
    return 0;
}

typedef struct {
    int tpl;
    int conn;
    int req;
    int status;
    int length_type;
    size_t content_length;
} upstream_request;

static int32_t receiver_https_accept_callback(int32_t libssl_ctx_id, uint32_t verify_err,
                                             void * const ssl_cert[], int32_t cert_num,
                                             void *user_arg) {
    (void)libssl_ctx_id;
    (void)verify_err;
    (void)ssl_cert;
    (void)cert_num;
    (void)user_arg;
    /* SceHttps callback contract used by PS4 homebrew: non-zero accepts the
     * certificate verification result. This is scoped per Receiver template. */
    return 1;
}

static void upstream_close(upstream_request *u) {
    if (u->req >= 0) sceHttpDeleteRequest(u->req);
    if (u->conn >= 0) sceHttpDeleteConnection(u->conn);
    if (u->tpl >= 0) sceHttpDeleteTemplate(u->tpl);
    u->req = u->conn = u->tpl = -1;
}

static void add_extra_headers(int req, const char *block) {
    if (!block || !*block) return;
    char temp[8192];
    snprintf(temp, sizeof(temp), "%s", block);
    char *save = NULL;
    for (char *line = strtok_r(temp, "\n", &save); line; line = strtok_r(NULL, "\n", &save)) {
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
        sceHttpAddRequestHeader(req, name, value, 0);
    }
}

static int upstream_open(const receiver_job *job, const char *range, upstream_request *u) {
    memset(u, 0, sizeof(*u));
    u->tpl = u->conn = u->req = -1;

    int ret = sceHttpCreateTemplate(g_http_ctx, job->user_agent, ORBIS_HTTP_VERSION_1_1, 1);
    if (ret < 0) return ret;
    u->tpl = ret;

    /* Direct-download CDNs increasingly present certificate chains that the
     * PS4 trust store does not know. VikingFile/Cloudflare currently reaches
     * SceSsl and fails with 0x8095F00C (UNKNOWN_CA). Match the proven PS4
     * Remote Package Installer approach and disable certificate validation
     * for the Receiver's upstream fetch only. The signed/opaque provider URL
     * is still used unchanged and HTTP status/range behavior is validated. */
    int is_https = (strncasecmp(job->upstream_url, "https://", 8) == 0);
    if (is_https) {
        /* Some newer CDN chains still trigger SCE_SSL_ERROR_UNKNOWN_CA even
         * when the verification flags are disabled. Install a template-scoped
         * verification callback as used by mature PS4 homebrew HTTP clients. */
        ret = sceHttpsSetSslCallback(u->tpl, receiver_https_accept_callback, NULL);
        if (ret < 0) { upstream_close(u); return ret; }
        (void)sceHttpsDisableOption(u->tpl, RECEIVER_HTTPS_VERIFY_FLAGS);
    }

    sceHttpSetAutoRedirect(u->tpl, 1);
    sceHttpSetConnectTimeOut(u->tpl, 30U * 1000U * 1000U);
    sceHttpSetSendTimeOut(u->tpl, 30U * 1000U * 1000U);
    sceHttpSetResolveTimeOut(u->tpl, 30U * 1000U * 1000U);

    ret = sceHttpCreateConnectionWithURL(u->tpl, job->upstream_url, true);
    if (ret < 0) { upstream_close(u); return ret; }
    u->conn = ret;

    ret = sceHttpCreateRequestWithURL(u->conn, ORBIS_METHOD_GET, job->upstream_url, 0);
    if (ret < 0) { upstream_close(u); return ret; }
    u->req = ret;

    /* Mirror the reference RPI ordering as well: re-apply verification flags
     * after request creation so both template and active request path inherit
     * the relaxed policy on older/newer firmware implementations. */
    if (is_https) {
        (void)sceHttpsDisableOption(u->tpl, RECEIVER_HTTPS_VERIFY_FLAGS);
    }

    sceHttpAddRequestHeader(u->req, "Accept", "*/*", 0);
    sceHttpAddRequestHeader(u->req, "Accept-Encoding", "identity", 0);
    if (range && *range) sceHttpAddRequestHeader(u->req, "Range", range, 0);
    if (job->referer[0]) sceHttpAddRequestHeader(u->req, "Referer", job->referer, 0);
    if (job->cookie[0]) sceHttpAddRequestHeader(u->req, "Cookie", job->cookie, 0);
    add_extra_headers(u->req, job->extra_headers);

    ret = sceHttpSendRequest(u->req, NULL, 0);
    if (ret < 0) { upstream_close(u); return ret; }
    int32_t status = 0;
    ret = sceHttpGetStatusCode(u->req, &status);
    if (ret < 0) { upstream_close(u); return ret; }
    u->status = status;

    u->length_type = -1;
    u->content_length = 0;
    if (u->status == 200 || u->status == 206) {
        ret = sceHttpGetResponseContentLength(u->req, &u->length_type, &u->content_length);
        if (ret < 0) { upstream_close(u); return ret; }
    }
    return 0;
}

static int upstream_open_with_backoff(const receiver_job *job, const char *range, upstream_request *u) {
    for (int attempt = 0; attempt <= RATE_LIMIT_RETRIES; attempt++) {
        int ret = upstream_open(job, range, u);
        if (ret < 0) return ret;
        if (u->status != 429) return 0;
        upstream_close(u);
        if (attempt == RATE_LIMIT_RETRIES) return -429;
        unsigned int delay = (unsigned int)(RATE_LIMIT_BACKOFF_BASE_SEC << attempt);
        sleep(delay);
    }
    return -429;
}

static int upstream_preflight(receiver_job *job) {
    upstream_request u;
    int ret = upstream_open_with_backoff(job, "bytes=0-0", &u);
    if (ret < 0) return ret;
    job->upstream_status = u.status;
    int status = u.status;
    upstream_close(&u);
    return (status == 200 || status == 206) ? 0 : -status;
}

static int find_existing_bgft_task(const char *content_id, int32_t *task_id) {
    if (!p_sceBgftServiceDownloadFindTaskByContentId || !content_id || !task_id) return -1;
    const int subtypes[] = { BGFT_SUBTYPE_GAME, BGFT_SUBTYPE_GAME_PATCH,
                             BGFT_SUBTYPE_GAME_AC, BGFT_SUBTYPE_PACKAGE };
    int last = -1;
    for (size_t i = 0; i < sizeof(subtypes) / sizeof(subtypes[0]); i++) {
        int32_t candidate = BGFT_INVALID_TASK_ID;
        int ret = p_sceBgftServiceDownloadFindTaskByContentId(content_id, subtypes[i], &candidate);
        if (ret == 0 && candidate != BGFT_INVALID_TASK_ID) {
            *task_id = candidate;
            return 0;
        }
        last = ret;
    }
    return last;
}

static int cleanup_stale_bgft_task(const char *content_id, int32_t *cleaned_task) {
    if (!p_sceBgftServiceIntDownloadUnregisterTask) return -1;
    int32_t old_task = BGFT_INVALID_TASK_ID;
    int ret = find_existing_bgft_task(content_id, &old_task);
    if (ret != 0 || old_task == BGFT_INVALID_TASK_ID) return ret ? ret : -1;

    /* If the Receiver was restarted, the old BGFT task still points to a
     * proxy job table that no longer exists. Stop/unregister that stale task
     * before registering the same Content ID against the current Receiver. */
    if (p_sceBgftServiceIntDownloadStopTask)
        p_sceBgftServiceIntDownloadStopTask(old_task);
    ret = p_sceBgftServiceIntDownloadUnregisterTask(old_task);
    if (ret == 0 && cleaned_task) *cleaned_task = old_task;
    return ret;
}

static int extract_title_id_from_content_id(const char *content_id, char out[10]) {
    if (!content_id || !out) return -1;
    const char *dash = strchr(content_id, '-');
    if (!dash || strlen(dash + 1) < 9) return -1;
    const char *p = dash + 1;
    if (strncmp(p, "CUSA", 4) != 0) return -1;
    memcpy(out, p, 9);
    out[9] = 0;
    return 0;
}

static uint16_t read_le16(const uint8_t *p) {
    return (uint16_t)p[0] | ((uint16_t)p[1] << 8);
}

static uint32_t read_le32(const uint8_t *p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

/* Minimal PARAM.SFO reader used only for installed-title diagnostics. */
static int read_sfo_text_value(const char *path, const char *wanted_key, char *out, size_t out_cap) {
    if (!path || !wanted_key || !out || out_cap < 2) return -1;
    out[0] = 0;
    FILE *fp = fopen(path, "rb");
    if (!fp) return -2;
    if (fseek(fp, 0, SEEK_END) != 0) { fclose(fp); return -3; }
    long sz = ftell(fp);
    if (sz < 20 || sz > (1024 * 1024)) { fclose(fp); return -4; }
    rewind(fp);
    uint8_t *buf = (uint8_t *)malloc((size_t)sz);
    if (!buf) { fclose(fp); return -5; }
    size_t got = fread(buf, 1, (size_t)sz, fp);
    fclose(fp);
    if (got != (size_t)sz || read_le32(buf) != 0x46535000U) { free(buf); return -6; }

    uint32_t key_table = read_le32(buf + 8);
    uint32_t data_table = read_le32(buf + 12);
    uint32_t count = read_le32(buf + 16);
    if (count > 512 || 20ULL + (uint64_t)count * 16ULL > (uint64_t)sz ||
        key_table >= (uint32_t)sz || data_table >= (uint32_t)sz) {
        free(buf); return -7;
    }

    for (uint32_t i = 0; i < count; ++i) {
        const uint8_t *idx = buf + 20 + i * 16;
        uint16_t key_off = read_le16(idx);
        uint32_t value_len = read_le32(idx + 4);
        uint32_t data_off = read_le32(idx + 12);
        uint64_t key_pos = (uint64_t)key_table + key_off;
        uint64_t data_pos = (uint64_t)data_table + data_off;
        if (key_pos >= (uint64_t)sz || data_pos >= (uint64_t)sz ||
            data_pos + value_len > (uint64_t)sz) continue;
        const char *key = (const char *)(buf + key_pos);
        size_t key_room = (size_t)sz - (size_t)key_pos;
        if (!memchr(key, 0, key_room) || strcmp(key, wanted_key) != 0) continue;
        size_t n = value_len;
        if (n >= out_cap) n = out_cap - 1;
        memcpy(out, buf + data_pos, n);
        out[n] = 0;
        while (n > 0 && (out[n - 1] == 0 || out[n - 1] == '\r' || out[n - 1] == '\n' || out[n - 1] == ' '))
            out[--n] = 0;
        free(buf);
        return out[0] ? 0 : -8;
    }
    free(buf);
    return -9;
}

static int read_installed_sfo_value(const char *title_id, int patch, const char *key, char *out, size_t out_cap) {
    char path[192];
    if (patch) {
        snprintf(path, sizeof(path), "/user/patch/%s/sce_sys/param.sfo", title_id);
        return read_sfo_text_value(path, key, out, out_cap);
    }
    const char *patterns[] = {
        "/user/appmeta/%s/param.sfo",
        "/user/app/%s/sce_sys/param.sfo",
        "/system_data/priv/appmeta/%s/param.sfo"
    };
    for (size_t i = 0; i < sizeof(patterns) / sizeof(patterns[0]); ++i) {
        snprintf(path, sizeof(path), patterns[i], title_id);
        if (read_sfo_text_value(path, key, out, out_cap) == 0) return 0;
    }
    return -1;
}

static int extract_version_hint(const char *name, char out[16]) {
    if (!name || !out) return -1;
    out[0] = 0;
    for (const char *p = name; *p; ++p) {
        if ((*p != 'v' && *p != 'V') || p[1] < '0' || p[1] > '9') continue;
        const char *q = p + 1;
        size_t n = 0;
        while (*q && n < 15 && ((*q >= '0' && *q <= '9') || *q == '.')) {
            out[n++] = *q++;
        }
        out[n] = 0;
        if (strchr(out, '.')) return 0;
        out[0] = 0;
    }
    return -1;
}

static int diagnose_patch_rejection(const receiver_job *job, char *detail, size_t detail_cap) {
    if (!detail || detail_cap == 0) return -1;
    detail[0] = 0;
    char title_id[10];
    if (extract_title_id_from_content_id(job->content_id, title_id) != 0) return -1;
    if (!p_sceAppInstUtilAppExists) {
        snprintf(detail, detail_cap, "patch-rejected-%s-0x80990117", title_id);
        return 0;
    }
    int exists = 0;
    int ret = p_sceAppInstUtilAppExists(title_id, &exists);
    if (ret == 0 && !exists) {
        snprintf(detail, detail_cap, "patch-base-missing-%s", title_id);
        return 0;
    }
    if (ret == 0 && exists) {
        char installed_patch_ver[24] = {0};
        char requested_ver[16] = {0};
        char base_content_id[80] = {0};
        int have_patch = read_installed_sfo_value(title_id, 1, "APP_VER", installed_patch_ver, sizeof(installed_patch_ver)) == 0;
        extract_version_hint(job->file_name, requested_ver);
        if (have_patch) {
            if (requested_ver[0])
                snprintf(detail, detail_cap, "patch-existing-%s-v%s-request-v%s", title_id, installed_patch_ver, requested_ver);
            else
                snprintf(detail, detail_cap, "patch-existing-%s-v%s", title_id, installed_patch_ver);
            return 0;
        }
        if (read_installed_sfo_value(title_id, 0, "CONTENT_ID", base_content_id, sizeof(base_content_id)) == 0 &&
            strcmp(base_content_id, job->content_id) != 0) {
            snprintf(detail, detail_cap, "patch-base-content-mismatch-%s", title_id);
            return 0;
        }
        snprintf(detail, detail_cap, "patch-base-present-%s-compatibility-0x80990117", title_id);
        return 0;
    }
    snprintf(detail, detail_cap, "patch-check-0x%08X", (unsigned int)ret);
    return ret;
}

static int register_bgft(receiver_job *job, char *detail, size_t detail_cap) {
    if (detail && detail_cap) detail[0] = 0;
    int user_id = 0;
    int ret = p_sceUserServiceGetForegroundUser ? p_sceUserServiceGetForegroundUser(&user_id) : -1;
    if (ret) return ret;

    char local_url[192];
    snprintf(local_url, sizeof(local_url), "http://127.0.0.1:%d/pkg/%u.pkg", PROXY_PORT, job->id);

    receiver_bgft_download_param params;
    memset(&params, 0, sizeof(params));
    params.user_id = user_id;
    params.entitlement_type = 5;
    params.id = job->content_id;
    params.content_url = local_url;
    params.content_name = job->file_name;
    params.icon_path = "";
    params.option = BGFT_TASK_OPTION_DISABLE_CDN_QUERY_PARAM;
    params.playgo_scenario_id = "0";
    params.package_type = job->package_type;
    params.package_sub_type = "";
    params.package_size = (unsigned long)job->package_size;

    int32_t task_id = BGFT_INVALID_TASK_ID;
    ret = p_sceBgftServiceIntDebugDownloadRegisterPkg ? p_sceBgftServiceIntDebugDownloadRegisterPkg(&params, &task_id) : -1;
    if ((uint32_t)ret == BGFT_ERROR_CONTENT_ALREADY_DOWNLOADING) {
        int32_t stale_task = BGFT_INVALID_TASK_ID;
        int cleanup = cleanup_stale_bgft_task(job->content_id, &stale_task);
        if (cleanup == 0) {
            char note[192];
            snprintf(note, sizeof(note), "Tayson Receiver: removendo tarefa BGFT antiga %d", stale_task);
            notify_text(note);
            sleep(1);
            task_id = BGFT_INVALID_TASK_ID;
            ret = p_sceBgftServiceIntDebugDownloadRegisterPkg ? p_sceBgftServiceIntDebugDownloadRegisterPkg(&params, &task_id) : -1;
        }
    }
    if ((uint32_t)ret == 0x80990117U)
        diagnose_patch_rejection(job, detail, detail_cap);
    if (ret) return ret;
    if (task_id == BGFT_INVALID_TASK_ID) return -1;
    ret = p_sceBgftServiceIntDownloadStartTask ? p_sceBgftServiceIntDownloadStartTask(task_id) : -1;
    if (ret) return ret;
    job->task_id = task_id;
    return 0;
}

static void reply_error_http(int client, int code, const char *reason) {
    char body[192];
    int body_len = snprintf(body, sizeof(body), "%d %s\n", code, reason);
    char head[512];
    int head_len = snprintf(head, sizeof(head),
        "HTTP/1.1 %d %s\r\nContent-Type: text/plain\r\nContent-Length: %d\r\nConnection: close\r\n\r\n",
        code, reason, body_len);
    send_all(client, head, (size_t)head_len);
    send_all(client, body, (size_t)body_len);
}

static int parse_job_id_from_target(const char *target, uint32_t *id_out) {
    if (strncmp(target, "/pkg/", 5) != 0) return -1;
    const char *p = target + 5;
    char *end = NULL;
    unsigned long id = strtoul(p, &end, 10);
    if (!end || end == p || id == 0 || id > 0xffffffffUL) return -1;
    if (strncmp(end, ".pkg", 4) != 0) return -1;
    end += 4;
    if (*end && *end != '?') return -1;
    *id_out = (uint32_t)id;
    return 0;
}

typedef struct {
    const receiver_job *job;
    uint64_t start;
    uint64_t end;
    uint8_t *data;
    size_t length;
    int result;
    int status;
} segment_task;

static void segment_gate_acquire(void) {
    pthread_mutex_lock(&g_segment_gate_mutex);
    while (g_segment_fetchers >= MAX_SEGMENT_FETCHERS) {
        pthread_cond_wait(&g_segment_gate_cond, &g_segment_gate_mutex);
    }
    g_segment_fetchers++;
    pthread_mutex_unlock(&g_segment_gate_mutex);
}

static void segment_gate_release(void) {
    pthread_mutex_lock(&g_segment_gate_mutex);
    if (g_segment_fetchers > 0) g_segment_fetchers--;
    pthread_cond_signal(&g_segment_gate_cond);
    pthread_mutex_unlock(&g_segment_gate_mutex);
}

static void *segment_fetch_thread(void *arg) {
    segment_task *task = (segment_task *)arg;
    task->result = -1;
    task->status = 0;
    task->length = 0;
    task->data = NULL;

    segment_gate_acquire();
    size_t expected = (size_t)(task->end - task->start + 1);
    task->data = (uint8_t *)malloc(expected);
    if (!task->data) {
        task->result = -12;
        segment_gate_release();
        return NULL;
    }

    char range[96];
    snprintf(range, sizeof(range), "bytes=%llu-%llu",
             (unsigned long long)task->start,
             (unsigned long long)task->end);

    upstream_request u;
    int ret = upstream_open(task->job, range, &u);
    if (ret < 0) {
        task->result = ret;
        free(task->data);
        task->data = NULL;
        segment_gate_release();
        return NULL;
    }
    task->status = u.status;
    if (u.status != 206 || u.length_type != ORBIS_HTTP_CONTENTLEN_EXIST ||
        (size_t)u.content_length != expected) {
        task->result = -502;
        upstream_close(&u);
        free(task->data);
        task->data = NULL;
        segment_gate_release();
        return NULL;
    }

    size_t got = 0;
    while (got < expected) {
        uint32_t want = (uint32_t)(expected - got);
        if (want > IO_BUFFER_SIZE) want = IO_BUFFER_SIZE;
        ret = sceHttpReadData(u.req, task->data + got, want);
        if (ret < 0) {
            task->result = ret;
            break;
        }
        if (ret == 0) break;
        got += (size_t)ret;
    }
    upstream_close(&u);
    task->length = got;
    task->result = (got == expected) ? 0 : (task->result < 0 ? task->result : -503);
    if (task->result != 0) {
        free(task->data);
        task->data = NULL;
    }
    segment_gate_release();
    return NULL;
}

static int launch_segment(segment_task *task, pthread_t *thread, const receiver_job *job,
                          uint64_t start, uint64_t end) {
    memset(task, 0, sizeof(*task));
    task->job = job;
    task->start = start;
    task->end = end;
    task->result = -1;

    pthread_attr_t attr;
    int ret = pthread_attr_init(&attr);
    if (ret != 0) return ret;
    ret = pthread_attr_setstacksize(&attr, PROXY_WORKER_STACK_SIZE);
    if (ret == 0) ret = pthread_create(thread, &attr, segment_fetch_thread, task);
    pthread_attr_destroy(&attr);
    return ret;
}

static int stream_upstream_range(int client, const receiver_job *job, uint32_t job_id,
                                 uint64_t start, uint64_t end, uint64_t *sent_total) {
    if (start > end) return 0;
    char range[96];
    snprintf(range, sizeof(range), "bytes=%llu-%llu",
             (unsigned long long)start, (unsigned long long)end);
    upstream_request u;
    int ret = upstream_open_with_backoff(job, range, &u);
    if (ret < 0) return ret;
    if (u.status != 206 || u.length_type != ORBIS_HTTP_CONTENTLEN_EXIST) {
        int status = u.status;
        upstream_close(&u);
        return status > 0 ? -status : -502;
    }
    uint64_t expected = end - start + 1;
    if ((uint64_t)u.content_length != expected) {
        upstream_close(&u);
        return -502;
    }
    uint8_t *buf = (uint8_t *)malloc(IO_BUFFER_SIZE);
    if (!buf) { upstream_close(&u); return -12; }
    uint64_t done = 0;
    while (done < expected) {
        uint32_t want = IO_BUFFER_SIZE;
        uint64_t remain = expected - done;
        if (remain < want) want = (uint32_t)remain;
        ret = sceHttpReadData(u.req, buf, want);
        if (ret < 0) break;
        if (ret == 0) { ret = -503; break; }
        if (send_all(client, buf, (size_t)ret)) { ret = -1; break; }
        done += (uint64_t)ret;
        *sent_total += (uint64_t)ret;
        job_transfer_progress(job_id, start + done, (uint64_t)ret);
        ret = 0;
    }
    free(buf);
    upstream_close(&u);
    return (done == expected) ? 0 : (ret ? ret : -503);
}

static int stream_segmented(int client, const receiver_job *job, uint32_t job_id,
                            uint64_t start, uint64_t end, uint64_t *sent_total) {
    segment_task tasks[SEGMENT_WINDOW];
    memset(tasks, 0, sizeof(tasks));
    pthread_t threads[SEGMENT_WINDOW];
    int active[SEGMENT_WINDOW] = {0};
    uint64_t next = start;

    for (int i = 0; i < SEGMENT_WINDOW && next <= end; i++) {
        uint64_t seg_end = next + SEGMENT_SIZE - 1;
        if (seg_end > end) seg_end = end;
        if (launch_segment(&tasks[i], &threads[i], job, next, seg_end) != 0) break;
        active[i] = 1;
        next = seg_end + 1;
    }

    int failed = 0;
    int client_failed = 0;
    int fail_status = 0;
    uint64_t current = start;
    for (int turn = 0; current <= end; turn++) {
        int idx = turn % SEGMENT_WINDOW;
        if (!active[idx]) { failed = 1; break; }
        pthread_join(threads[idx], NULL);
        active[idx] = 0;
        if (tasks[idx].result != 0 || !tasks[idx].data) {
            fail_status = tasks[idx].status;
            failed = 1;
            break;
        }
        if (tasks[idx].start != current) {
            free(tasks[idx].data);
            tasks[idx].data = NULL;
            failed = 1;
            break;
        }
        if (send_all(client, tasks[idx].data, tasks[idx].length)) {
            free(tasks[idx].data);
            tasks[idx].data = NULL;
            failed = 1;
            client_failed = 1;
            break;
        }
        current = tasks[idx].end + 1;
        *sent_total += (uint64_t)tasks[idx].length;
        job_transfer_progress(job_id, current, (uint64_t)tasks[idx].length);
        free(tasks[idx].data);
        tasks[idx].data = NULL;

        if (next <= end) {
            uint64_t seg_end = next + SEGMENT_SIZE - 1;
            if (seg_end > end) seg_end = end;
            if (launch_segment(&tasks[idx], &threads[idx], job, next, seg_end) == 0) {
                active[idx] = 1;
                next = seg_end + 1;
            } else {
                failed = 1;
                break;
            }
        }
    }

    for (int i = 0; i < SEGMENT_WINDOW; i++) {
        if (active[i]) {
            pthread_join(threads[i], NULL);
            active[i] = 0;
        }
        if (tasks[i].data) {
            free(tasks[i].data);
            tasks[i].data = NULL;
        }
    }

    if (client_failed) return -1;
    if (!failed && current > end) return 0;
    /* A provider may throttle parallel Range requests with HTTP 429. Let the
     * CDN cool down, then finish the exact byte interval through the stable
     * single-range path. This avoids turning a provider throttle into a local
     * 502 error for BGFT. */
    if (fail_status == 429) {
        job_disable_segmented(job_id);
        sleep(RATE_LIMIT_BACKOFF_BASE_SEC);
    }
    if (current <= end) return stream_upstream_range(client, job, job_id, current, end, sent_total);
    return 0;
}

static int handle_proxy_client(int client) {
    char header[MAX_REQUEST_HEADER + 1];
    size_t used = 0;
    while (used < MAX_REQUEST_HEADER) {
        ssize_t n = sceNetRecv(client, header + used, MAX_REQUEST_HEADER - used, 0);
        if (n <= 0) return -1;
        used += (size_t)n;
        header[used] = 0;
        if (strstr(header, "\r\n\r\n")) break;
    }
    if (!strstr(header, "\r\n\r\n")) { reply_error_http(client, 431, "Headers Too Large"); return -1; }

    char method[8] = {0};
    char target[512] = {0};
    if (sscanf(header, "%7s %511s", method, target) != 2) { reply_error_http(client, 400, "Bad Request"); return -1; }
    uint32_t job_id = 0;
    if (parse_job_id_from_target(target, &job_id)) { reply_error_http(client, 404, "Not Found"); return -1; }

    receiver_job job;
    if (copy_job(job_id, &job)) { reply_error_http(client, 404, "Unknown Job"); return -1; }

    if (!strcmp(method, "HEAD")) {
        char head[512];
        int n = snprintf(head, sizeof(head),
            "HTTP/1.1 200 OK\r\nContent-Length: %llu\r\nAccept-Ranges: bytes\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n",
            (unsigned long long)job.package_size);
        return send_all(client, head, (size_t)n);
    }
    if (strcmp(method, "GET")) { reply_error_http(client, 405, "Method Not Allowed"); return -1; }

    char range_value[128] = {0};
    int have_range = find_header(header, "Range", range_value, sizeof(range_value)) == 0;
    uint64_t range_start = 0, range_end = 0;
    int range_has_end = 0;
    if (have_range && parse_range(range_value, &range_start, &range_end, &range_has_end)) {
        reply_error_http(client, 416, "Range Not Satisfiable"); return -1;
    }
    if (have_range && range_start >= job.package_size) {
        reply_error_http(client, 416, "Range Not Satisfiable"); return -1;
    }

    if (ensure_http_stack_only() < 0) {
        reply_error_http(client, 503, "HTTP Stack Unavailable");
        return -1;
    }

    job_transfer_begin(job_id);
    upstream_request u;
    int ret = upstream_open_with_backoff(&job, have_range ? range_value : NULL, &u);
    if (ret < 0) {
        job_transfer_end(job_id, ret, 0);
        notify_code("Tayson Receiver upstream", ret);
        reply_error_http(client, 502, "Upstream Error");
        return -1;
    }
    if (u.status != 200 && u.status != 206) {
        char msg[160];
        snprintf(msg, sizeof(msg), "Tayson Receiver upstream HTTP %d", u.status);
        notify_text(msg);
        job_transfer_end(job_id, -u.status, u.status);
        upstream_close(&u);
        reply_error_http(client, 502, "Upstream Rejected");
        return -1;
    }
    if (have_range && range_start > 0 && u.status != 206) {
        job_transfer_end(job_id, -502, u.status);
        upstream_close(&u);
        reply_error_http(client, 502, "Upstream Ignored Range");
        return -1;
    }
    if (u.length_type != ORBIS_HTTP_CONTENTLEN_EXIST) {
        job_transfer_end(job_id, -502, u.status);
        upstream_close(&u);
        reply_error_http(client, 502, "Missing Content-Length");
        return -1;
    }

    uint64_t body_len = (uint64_t)u.content_length;
    int local_status = have_range ? 206 : 200;
    if (have_range && u.status == 200 && range_start == 0) local_status = 200;

    char head[768];
    int head_len;
    if (local_status == 206) {
        uint64_t actual_end = body_len ? range_start + body_len - 1 : range_start;
        if (actual_end >= job.package_size) actual_end = job.package_size - 1;
        head_len = snprintf(head, sizeof(head),
            "HTTP/1.1 206 Partial Content\r\nContent-Length: %llu\r\nContent-Range: bytes %llu-%llu/%llu\r\nAccept-Ranges: bytes\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n",
            (unsigned long long)body_len,
            (unsigned long long)range_start,
            (unsigned long long)actual_end,
            (unsigned long long)job.package_size);
    } else {
        head_len = snprintf(head, sizeof(head),
            "HTTP/1.1 200 OK\r\nContent-Length: %llu\r\nAccept-Ranges: bytes\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n",
            (unsigned long long)body_len);
    }
    if (send_all(client, head, (size_t)head_len)) {
        job_transfer_end(job_id, -1, u.status);
        upstream_close(&u);
        return -1;
    }

    uint64_t sent = 0;
    uint64_t absolute_end = body_len ? range_start + body_len - 1 : range_start;
    int use_segmented = body_len >= SEGMENT_MIN_BODY && job.upstream_status == 206 && !job.parallel_disabled;
    if (use_segmented) {
        /* The first request was used only to validate the exact BGFT response
         * length/status. Multi-range workers now fetch that same byte interval
         * concurrently and feed it back to BGFT in strict order. */
        upstream_close(&u);
        ret = stream_segmented(client, &job, job_id, range_start, absolute_end, &sent);
        if (ret < 0) {
            notify_code("Tayson Receiver segmented", ret);
            job_transfer_end(job_id, ret, 206);
            return -1;
        }
    } else {
        uint8_t *io_buf = (uint8_t *)malloc(IO_BUFFER_SIZE);
        if (!io_buf) {
            job_transfer_end(job_id, -1, u.status);
            upstream_close(&u);
            return -1;
        }
        while (sent < body_len) {
            uint32_t want = IO_BUFFER_SIZE;
            uint64_t remain = body_len - sent;
            if (remain < want) want = (uint32_t)remain;
            ret = sceHttpReadData(u.req, io_buf, want);
            if (ret < 0) {
                notify_code("Tayson Receiver read", ret);
                job_transfer_end(job_id, ret, u.status);
                free(io_buf);
                upstream_close(&u);
                return -1;
            }
            if (ret == 0) break;
            if (send_all(client, io_buf, (size_t)ret)) {
                job_transfer_end(job_id, -1, u.status);
                free(io_buf);
                upstream_close(&u);
                return -1;
            }
            sent += (uint64_t)ret;
            job_transfer_progress(job_id, range_start + sent, (uint64_t)ret);
        }
        free(io_buf);
        upstream_close(&u);
    }
    int complete = sent == body_len;
    job_transfer_end(job_id, complete ? 0 : -1, 206);
    return complete ? 0 : -1;
}

static int create_listener(uint16_t port, uint32_t bind_addr) {
    int fd = sceNetSocket("tayson_receiver", ORBIS_NET_AF_INET, ORBIS_NET_SOCK_STREAM, 0);
    if (fd < 0) return -1;
    int yes = 1;
    sceNetSetsockopt(fd, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_REUSEADDR, &yes, sizeof(yes));
    int sndbuf = RECEIVER_SOCKET_SNDBUF;
    int rcvbuf = RECEIVER_SOCKET_RCVBUF;
    sceNetSetsockopt(fd, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_SNDBUF, &sndbuf, sizeof(sndbuf));
    sceNetSetsockopt(fd, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_RCVBUF, &rcvbuf, sizeof(rcvbuf));
    receiver_net_sockaddr_in addr;
    memset(&addr, 0, sizeof(addr));
    addr.sin_len = (uint8_t)sizeof(addr);
    addr.sin_family = ORBIS_NET_AF_INET;
    addr.sin_port = sceNetHtons(port);
    addr.sin_addr = sceNetHtonl(bind_addr);
    if (sceNetBind(fd, (const OrbisNetSockaddr *)&addr, sizeof(addr)) < 0) { sceNetSocketClose(fd); return -1; }
    if (sceNetListen(fd, 16) < 0) { sceNetSocketClose(fd); return -1; }
    return fd;
}

static void *proxy_client_thread(void *arg) {
    int client = *(int *)arg;
    free(arg);
    handle_proxy_client(client);
    sceNetSocketClose(client);
    return NULL;
}

static void *proxy_thread(void *arg) {
    (void)arg;
    int listener = create_listener(PROXY_PORT, INADDR_LOOPBACK);
    if (listener < 0) {
        notify_text("Tayson Receiver: falha na porta local 32112");
        return NULL;
    }
    for (;;) {
        receiver_net_sockaddr_in peer;
        OrbisNetSocklen_t peer_len = sizeof(peer);
        int client = sceNetAccept(listener, (OrbisNetSockaddr *)&peer, &peer_len);
        if (client < 0) break;
        int sndbuf = RECEIVER_SOCKET_SNDBUF;
        int rcvbuf = RECEIVER_SOCKET_RCVBUF;
        sceNetSetsockopt(client, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_SNDBUF, &sndbuf, sizeof(sndbuf));
        sceNetSetsockopt(client, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_RCVBUF, &rcvbuf, sizeof(rcvbuf));
        if (sceNetNtohl(peer.sin_addr) != INADDR_LOOPBACK) {
            sceNetSocketClose(client);
            continue;
        }
        int *fd_arg = (int *)malloc(sizeof(int));
        if (!fd_arg) { sceNetSocketClose(client); continue; }
        *fd_arg = client;
        pthread_t worker;
        pthread_attr_t worker_attr;
        int attr_ok = pthread_attr_init(&worker_attr);
        if (attr_ok == 0) attr_ok = pthread_attr_setstacksize(&worker_attr, PROXY_WORKER_STACK_SIZE);
        if (attr_ok != 0 || pthread_create(&worker, &worker_attr, proxy_client_thread, fd_arg) != 0) {
            if (attr_ok == 0) pthread_attr_destroy(&worker_attr);
            free(fd_arg);
            sceNetSocketClose(client);
            notify_text("Tayson Receiver: falha ao criar worker HTTP");
            continue;
        }
        pthread_attr_destroy(&worker_attr);
        pthread_detach(worker);
    }
    sceNetSocketClose(listener);
    return NULL;
}

static int respond_line(int fd, const char *line) {
    return send_all(fd, line, strlen(line));
}

static void handle_control_client(int client, const receiver_net_sockaddr_in *peer) {
    if (!is_private_ipv4(peer->sin_addr)) {
        respond_line(client, "TAYSON/1 ERR private-network-only\n");
        return;
    }

    int timeout_us = 15 * 1000 * 1000;
    sceNetSetsockopt(client, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_RCVTIMEO, &timeout_us, sizeof(timeout_us));
    sceNetSetsockopt(client, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_SNDTIMEO, &timeout_us, sizeof(timeout_us));

    char line[160];
    if (recv_line(client, line, sizeof(line)) < 0) return;
    if (!strcmp(line, "TAYSON/1 PING")) {
        char pong[96];
        snprintf(pong, sizeof(pong), "TAYSON/1 OK receiver=%s\n", RECEIVER_VERSION);
        respond_line(client, pong);
        return;
    }
    if (!strcmp(line, "TAYSON/1 STATUS")) {
        respond_status(client);
        return;
    }

    int service_ret = ensure_transfer_services();
    if (service_ret) {
        char errline[96];
        snprintf(errline, sizeof(errline), "TAYSON/1 ERR services-%s-0x%08X\n", g_service_stage, (unsigned int)service_ret);
        respond_line(client, errline);
        return;
    }

    size_t payload_len = 0;
    if (sscanf(line, "TAYSON/1 JOB %zu", &payload_len) != 1 || payload_len == 0 || payload_len > MAX_CONTROL_PAYLOAD) {
        respond_line(client, "TAYSON/1 ERR bad-command\n");
        return;
    }

    uint8_t *packet = (uint8_t *)malloc(payload_len);
    if (!packet) { respond_line(client, "TAYSON/1 ERR no-memory\n"); return; }
    if (recv_all(client, packet, payload_len)) {
        free(packet);
        respond_line(client, "TAYSON/1 ERR incomplete-payload\n");
        return;
    }

    receiver_job parsed;
    if (parse_job_packet(packet, payload_len, &parsed)) {
        free(packet);
        respond_line(client, "TAYSON/1 ERR invalid-job\n");
        return;
    }
    free(packet);

    uint32_t job_id = 0;
    int existing_task = BGFT_INVALID_TASK_ID;
    if (find_duplicate_active_job(&parsed, &job_id, &existing_task)) {
        int32_t actual_task = BGFT_INVALID_TASK_ID;
        int lookup = find_existing_bgft_task(parsed.content_id, &actual_task);
        if (lookup == 0 && actual_task != BGFT_INVALID_TASK_ID) {
            if (actual_task != existing_task) set_job_task(job_id, actual_task, 206);
            char existing[192];
            snprintf(existing, sizeof(existing),
                     "TAYSON/1 OK job=%u task=%d upstream=206 duplicate=1 recovered=1\n",
                     job_id, actual_task);
            respond_line(client, existing);
            return;
        }
        /* Persisted metadata outlived its BGFT task. Remove only our local
         * registry entry and let normal registration create a fresh task. */
        drop_job(job_id);
        job_id = 0;
        existing_task = BGFT_INVALID_TASK_ID;
    }
    if (allocate_job(&parsed, &job_id)) {
        respond_line(client, "TAYSON/1 ERR job-table-full\n");
        return;
    }

    receiver_job job;
    if (copy_job(job_id, &job)) {
        drop_job(job_id);
        respond_line(client, "TAYSON/1 ERR internal-job\n");
        return;
    }

    char msg[192];
    snprintf(msg, sizeof(msg), "Tayson Receiver: testando CDN do job %u", job_id);
    notify_text(msg);
    int ret = upstream_preflight(&job);
    if (ret) {
        int http = job.upstream_status;
        drop_job(job_id);
        if (http > 0) {
            snprintf(msg, sizeof(msg), "TAYSON/1 ERR upstream-http-%d\n", http);
        } else {
            snprintf(msg, sizeof(msg), "TAYSON/1 ERR upstream-code-0x%08X\n", (unsigned int)ret);
        }
        respond_line(client, msg);
        return;
    }

    if (copy_job(job_id, &job)) {
        drop_job(job_id);
        respond_line(client, "TAYSON/1 ERR internal-job\n");
        return;
    }
    job.upstream_status = 206;
    char bgft_detail[128];
    bgft_detail[0] = 0;
    ret = register_bgft(&job, bgft_detail, sizeof(bgft_detail));
    if (ret) {
        drop_job(job_id);
        if (bgft_detail[0])
            snprintf(msg, sizeof(msg), "TAYSON/1 ERR %s\n", bgft_detail);
        else
            snprintf(msg, sizeof(msg), "TAYSON/1 ERR bgft-0x%08X\n", (unsigned int)ret);
        respond_line(client, msg);
        notify_code("Tayson Receiver: BGFT", ret);
        return;
    }
    set_job_task(job_id, job.task_id, 206);

    snprintf(msg, sizeof(msg), "TAYSON/1 OK job=%u task=%d upstream=%d\n", job_id, job.task_id, job.upstream_status);
    respond_line(client, msg);

    char note[192];
    snprintf(note, sizeof(note), "Tayson Receiver: %s enviado para Downloads", job.content_id);
    notify_text(note);
}

int main(void) {
    if (load_control_network()) {
        for (;;) sleep(10);
    }

    int restored = load_persisted_jobs();
    if (restored < 0) {
        unlink(PERSIST_PATH);
        g_restored_jobs = 0;
    }
    if (restored > 0) {
        int warm = ensure_transfer_services();
        if (warm < 0) notify_code("Tayson Receiver: restore services", warm);
    }

    pthread_t proxy;
    if (pthread_create(&proxy, NULL, proxy_thread, NULL) != 0) {
        notify_text("Tayson Receiver: thread do proxy falhou");
        for (;;) sleep(10);
    }
    pthread_detach(proxy);

    int listener = create_listener(CONTROL_PORT, INADDR_ANY);
    if (listener < 0) {
        notify_text("Tayson Receiver: porta 9095 indisponivel");
        for (;;) sleep(10);
    }

    notify_text(g_daemon_registered ? "Tayson Receiver 1.4.0 daemon ativo - porta 9095" : "Tayson Receiver 1.4.0 ativo - porta 9095");
    printf("Tayson PS4 Receiver %s listening on TCP %d\n", RECEIVER_VERSION, CONTROL_PORT);

    for (;;) {
        receiver_net_sockaddr_in peer;
        OrbisNetSocklen_t peer_len = sizeof(peer);
        int client = sceNetAccept(listener, (OrbisNetSockaddr *)&peer, &peer_len);
        if (client < 0) {
            sleep(1);
            continue;
        }
        handle_control_client(client, &peer);
        sceNetSocketClose(client);
    }

    return 0;
}
