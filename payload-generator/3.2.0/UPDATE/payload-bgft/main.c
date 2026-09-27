#include <dlfcn.h>
#include <stdint.h>
#include <stddef.h>
#include <sys/mman.h>
#include <string.h>
#include "ps4-libjbc/jailbreak.h"

#define BGFT_INVALID_TASK_ID (-1)
#define BGFT_TASK_OPTION_DISABLE_CDN_QUERY_PARAM 0x10000u
#define ORBIS_KERNEL_PRIO_FIFO_NORMAL 0x2BCu

__attribute__((used)) static char PKG_URL[2048] = "###URL_PLACEHOLDER###";
__attribute__((used)) static char PKG_CONTENT_ID[64] = "###CONTENT_ID_PLACEHOLDER###";
__attribute__((used)) static char PKG_NAME[128] = "###NAME_PLACEHOLDER###";
__attribute__((used)) static char PKG_TYPE[16] = "###TYPE###";
__attribute__((used)) static uint64_t PKG_SIZE = 0xBBBBBBBBBBBBBBBBULL;

__asm__(
    ".text\n"
    ".global clear_stack\n"
    ".type clear_stack,@function\n"
    "clear_stack:\n"
    "mov $0x800,%ecx\n"
    "xor %rax,%rax\n"
    "1:\n"
    "push %rax\n"
    "loop 1b\n"
    "add $0x4000,%rsp\n"
    "ret\n"
);
void clear_stack(void);

typedef int (*notify_fn)(int, const char*);
typedef int (*user_init_fn)(void*);
typedef int (*user_get_fn)(int*);
typedef int (*user_term_fn)(void);
typedef int (*appinst_init_fn)(void);

typedef struct {
    int32_t user_id;
    int32_t entitlement_type;
    const char* id;
    const char* content_url;
    const char* content_ex_url;
    const char* content_name;
    const char* icon_path;
    const char* sku_id;
    uint32_t option;
    uint32_t _pad;
    const char* playgo_scenario_id;
    const char* release_date;
    const char* package_type;
    const char* package_sub_type;
    uint64_t package_size;
} bgft_download_param;

typedef struct {
    void* mem;
    uint64_t size;
} bgft_init_params;

_Static_assert(sizeof(bgft_download_param) == 104, "BGFT param layout");
_Static_assert(offsetof(bgft_download_param, option) == 56, "BGFT option offset");
_Static_assert(offsetof(bgft_download_param, package_size) == 96, "BGFT size offset");

typedef int (*bgft_init_fn)(bgft_init_params*);
typedef int (*bgft_register_fn)(bgft_download_param*, int*);
typedef int (*bgft_start_fn)(int);

typedef struct { uint32_t priority; } user_init_params;

static notify_fn g_notify;

static void notify_text(const char* text) {
    if (g_notify) (void)g_notify(222, text);
}

static void hex32(uint32_t v, char out[11]) {
    static const char d[] = "0123456789ABCDEF";
    out[0] = '0'; out[1] = 'x';
    for (int i = 0; i < 8; i++) out[2 + i] = d[(v >> (28 - i * 4)) & 0xF];
    out[10] = 0;
}

static void notify_code(const char* tag, int rc) {
    char msg[128], h[11];
    size_t p = 0;
    hex32((uint32_t)rc, h);
    while (*tag && p + 1 < sizeof(msg)) msg[p++] = *tag++;
    if (p + 1 < sizeof(msg)) msg[p++] = ' ';
    for (int i = 0; h[i] && p + 1 < sizeof(msg); i++) msg[p++] = h[i];
    msg[p] = 0;
    notify_text(msg);
}

static void notify_pair(const char* tag, int rc, int task) {
    char msg[160], a[11], b[11];
    size_t p = 0;
    hex32((uint32_t)rc, a);
    hex32((uint32_t)task, b);
    while (*tag && p + 1 < sizeof(msg)) msg[p++] = *tag++;
    if (p + 1 < sizeof(msg)) msg[p++] = ' ';
    for (int i = 0; a[i] && p + 1 < sizeof(msg); i++) msg[p++] = a[i];
    if (p + 1 < sizeof(msg)) msg[p++] = ' ';
    for (int i = 0; b[i] && p + 1 < sizeof(msg); i++) msg[p++] = b[i];
    msg[p] = 0;
    notify_text(msg);
}

static int streq(const char* a, const char* b) {
    if (!a || !b) return 0;
    while (*a && *b && *a == *b) { a++; b++; }
    return *a == 0 && *b == 0;
}

static const char* normalized_type(int* is_patch) {
    static const char ps4dp[] = "PS4DP";
    *is_patch = 0;
    if (streq(PKG_TYPE, "PS4DP") || streq(PKG_TYPE, "PS4PATCH")) {
        *is_patch = 1;
        return ps4dp;
    }
    return PKG_TYPE;
}

static int setup_dpi_credentials(void) {
    struct jbc_cred cred;
    memset(&cred, 0, sizeof(cred));

    int rc = jbc_get_cred(&cred);
    notify_code("C0 GET", rc);
    if (rc) return rc;

    rc = jbc_jailbreak_cred(&cred);
    notify_code("C1 JB", rc);
    if (rc) return rc;

    cred.jdir = 0;
    cred.sceProcType = 0x3800000000000010ULL;
    cred.sonyCred = 0x40001c0000000000ULL;
    cred.sceProcCap = 0x900000000000ff00ULL;

    rc = jbc_set_cred(&cred);
    notify_code("C2 DPI", rc);
    return rc;
}

static int get_foreground_user_exact(void) {
    void* lib = dlopen("/system/common/lib/libSceUserService.sprx", 0);
    if (!lib) {
        notify_text("U0 LOAD FAIL");
        return -1;
    }

    user_init_fn init = (user_init_fn)dlsym(lib, "sceUserServiceInitialize");
    user_get_fn foreground = (user_get_fn)dlsym(lib, "sceUserServiceGetForegroundUser");
    user_get_fn initial = (user_get_fn)dlsym(lib, "sceUserServiceGetInitialUser");
    user_term_fn terminate = (user_term_fn)dlsym(lib, "sceUserServiceTerminate");

    if (!init || !foreground) {
        notify_text("U1 SYM FAIL");
        return -1;
    }

    user_init_params p = { ORBIS_KERNEL_PRIO_FIFO_NORMAL };
    int init_rc = init(&p);
    notify_code("U2 INIT", init_rc);

    int uid = -1;
    int fg_rc = foreground(&uid);
    notify_pair("U3 FG", fg_rc, uid);

    if (terminate) {
        int term_rc = terminate();
        notify_code("U4 TERM", term_rc);
    }

    if (fg_rc == 0 && uid != -1) return uid;

    if (initial) {
        init_rc = init(&p);
        notify_code("U5 REINIT", init_rc);
        uid = -1;
        int in_rc = initial(&uid);
        notify_pair("U6 INITIAL", in_rc, uid);
        if (terminate) (void)terminate();
        if (in_rc == 0 && uid != -1) return uid;
    }

    return -1;
}

static int init_appinst_exact(void) {
    void* lib = dlopen("/system/common/lib/libSceAppInstUtil.sprx", 0);
    if (!lib) return -1;
    appinst_init_fn init = (appinst_init_fn)dlsym(lib, "sceAppInstUtilInitialize");
    if (!init) return -1;
    return init();
}

static int usable_task(int rc, int task) {
    if (task == BGFT_INVALID_TASK_ID) return 0;
    if ((uint32_t)rc == 0x80990088u) return 0;
    return 1;
}

int main(void) {
    int rc = setup_dpi_credentials();
    if (rc) return rc;

    clear_stack();

    void* sysutil = dlopen("/system/common/lib/libSceSysUtil.sprx", 0);
    if (sysutil)
        g_notify = (notify_fn)dlsym(sysutil, "sceSysUtilSendSystemNotificationWithText");

    notify_text("Tayson 3.5.0 BGFT CANONICAL");

    int uid = get_foreground_user_exact();
    if (uid == -1) {
        notify_text("U9 NO FOREGROUND USER");
        return -110;
    }
    notify_code("UA USER", uid);

    void* bgft = dlopen("/system/common/lib/libSceBgft.sprx", 0);
    if (!bgft) {
        notify_text("B0 BGFT LOAD FAIL");
        return -101;
    }

    bgft_init_fn bgft_init = (bgft_init_fn)dlsym(bgft, "sceBgftServiceIntInit");
    bgft_register_fn reg_public = (bgft_register_fn)dlsym(bgft, "sceBgftServiceDownloadRegisterTask");
    bgft_register_fn reg_debug = (bgft_register_fn)dlsym(bgft, "sceBgftServiceIntDebugDownloadRegisterPkg");
    bgft_register_fn reg_internal = (bgft_register_fn)dlsym(bgft, "sceBgftServiceIntDownloadRegisterTask");
    bgft_start_fn start_internal = (bgft_start_fn)dlsym(bgft, "sceBgftServiceIntDownloadStartTask");
    bgft_start_fn start_public = (bgft_start_fn)dlsym(bgft, "sceBgftServiceDownloadStartTask");

    if (!bgft_init || (!reg_public && !reg_debug && !reg_internal) ||
        (!start_internal && !start_public)) {
        notify_text("B1 BGFT SYMBOL FAIL");
        return -102;
    }

    clear_stack();

    rc = init_appinst_exact();
    notify_code("A0 APPINST", rc);
    if (rc) return rc;

    void* heap = mmap(NULL, 0x100000, PROT_READ | PROT_WRITE,
                      MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (!heap || heap == MAP_FAILED) {
        notify_text("B2 HEAP FAIL");
        return -103;
    }

    bgft_init_params ip = { heap, 0x100000 };
    rc = bgft_init(&ip);
    notify_code("B3 INIT", rc);
    if (rc && (uint32_t)rc != 0x80990001u) return rc;

    static const char empty[] = "";
    static const char playgo_zero[] = "0";
    int is_patch = 0;
    const char* pkg_type = normalized_type(&is_patch);

    bgft_download_param p;
    memset(&p, 0, sizeof(p));
    p.user_id = uid;
    p.entitlement_type = 5;
    p.id = PKG_CONTENT_ID;
    p.content_url = PKG_URL;
    p.content_name = PKG_NAME;
    p.icon_path = empty;
    p.option = BGFT_TASK_OPTION_DISABLE_CDN_QUERY_PARAM;
    p.playgo_scenario_id = playgo_zero;
    p.package_type = pkg_type;
    p.package_sub_type = empty;
    p.package_size = PKG_SIZE;

    notify_code("R0 USER", p.user_id);
    notify_text(is_patch ? "R1 TYPE PS4DP" : "R1 TYPE NONPATCH");

    int task = BGFT_INVALID_TASK_ID;
    int reg_rc = -1;

    if (is_patch && reg_debug) {
        reg_rc = reg_debug(&p, &task);
        notify_pair("R2 DEBUG", reg_rc, task);
    } else if (!is_patch && reg_public) {
        reg_rc = reg_public(&p, &task);
        notify_pair("R2 PUBLIC", reg_rc, task);
    }

    if (!usable_task(reg_rc, task)) {
        task = BGFT_INVALID_TASK_ID;
        if (is_patch && reg_public) {
            reg_rc = reg_public(&p, &task);
            notify_pair("R3 PUBLIC", reg_rc, task);
        } else if (!is_patch && reg_debug) {
            reg_rc = reg_debug(&p, &task);
            notify_pair("R3 DEBUG", reg_rc, task);
        }
    }

    if (!usable_task(reg_rc, task) && reg_internal) {
        task = BGFT_INVALID_TASK_ID;
        reg_rc = reg_internal(&p, &task);
        notify_pair("R4 INT", reg_rc, task);
    }

    if (!usable_task(reg_rc, task)) {
        notify_code("R9 FAILED", reg_rc);
        return reg_rc;
    }

    notify_code("RA TASK", task);

    int start_rc = -1;
    if (start_internal) {
        start_rc = start_internal(task);
        notify_code("S0 INT", start_rc);
    }
    if (start_rc != 0 && start_public) {
        start_rc = start_public(task);
        notify_code("S1 PUBLIC", start_rc);
    }
    if (start_rc) return start_rc;

    notify_code("S2 TASK OK", task);
    notify_text("Tayson 3.5.0 BGFT STARTED");
    return 0;
}
