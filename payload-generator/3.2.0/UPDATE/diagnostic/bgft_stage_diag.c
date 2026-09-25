#include <stddef.h>
#include <stdint.h>
#include <sys/mman.h>
#include "ps4-libjbc/jailbreak.h"
#include "struct.h"

/*
 * Tayson 3.4.5 BGFT stage diagnostic.
 *
 * Product behavior remains the 3.4.3 flow. Only the native registration
 * backend is being exercised here:
 *   - resolve the active user like known-working PS4 payloads;
 *   - initialize AppInstUtil when available;
 *   - prefer sceBgftServiceDownloadRegisterTask;
 *   - fall back to sceBgftServiceIntDebugDownloadRegisterPkg.
 *
 * The payload reports stage names and integer return codes only. It never
 * prints the PKG URL, cookies, tokens or other credentials.
 */

__attribute__((used)) char PKG_CONTENT_ID[64] = "###CONTENT_ID_PLACEHOLDER###";
__attribute__((used)) char PKG_URL[2048] = "###URL_PLACEHOLDER###";
__attribute__((used)) char PKG_NAME[128] = "###NAME_PLACEHOLDER###";
__attribute__((used)) char PKG_TYPE[16] = "###TYPE###";
__attribute__((used)) volatile unsigned long PKG_SIZE = 0xBBBBBBBBBBBBBBBBULL;

#define BGFT_INVALID_TASK_ID (-1)
#define USER_ID_FALLBACK 0x10000000

typedef int (*notify_fn)(int, const char *);
typedef int (*bgft_init_fn)(struct bgft_init_params *);
typedef int (*bgft_register_fn)(struct bgft_download_param *, int *);
typedef int (*bgft_start_fn)(int);
typedef int (*user_service_init_fn)(OrbisUserServiceInitializeParams *);
typedef int (*user_service_get_user_fn)(int *);
typedef int (*user_service_term_fn)(void);
typedef int (*appinst_init_fn)(void);

static notify_fn g_notify = 0;

static void hex32(int32_t value, char *out)
{
    static const char digits[] = "0123456789ABCDEF";
    uint32_t v = (uint32_t)value;
    out[0] = '0'; out[1] = 'x';
    for (int i = 0; i < 8; ++i) {
        out[2 + i] = digits[(v >> (28 - i * 4)) & 0x0F];
    }
    out[10] = 0;
}

static void append(char *dst, int *pos, const char *src)
{
    while (*src && *pos < 190) dst[(*pos)++] = *src++;
    dst[*pos] = 0;
}

static void notify_text(const char *text)
{
    if (g_notify) g_notify(222, text);
}

static void notify_code(const char *stage, int code)
{
    char msg[192];
    char hex[11];
    int p = 0;
    msg[0] = 0;
    hex32(code, hex);
    append(msg, &p, "Tayson BGFT ");
    append(msg, &p, stage);
    append(msg, &p, " ");
    append(msg, &p, hex);
    notify_text(msg);
}

static int setup_cred(void)
{
    struct jbc_cred cred;
    if (jbc_get_cred(&cred) != 0) return -1;
    if (jbc_jailbreak_cred(&cred) != 0) return -2;
    if (jbc_set_cred(&cred) != 0) return -3;
    return 0;
}

static int resolve_user_id(void)
{
    int user_id = USER_ID_FALLBACK;
    void *user_service = dlopen("/system/common/lib/libSceUserService.sprx", 0);
    if (!user_service) {
        notify_text("Tayson BGFT D3 USER LOAD SKIP");
        notify_code("D3 USER", user_id);
        return user_id;
    }

    user_service_init_fn user_init =
        (user_service_init_fn)dlsym(user_service, "sceUserServiceInitialize");
    user_service_get_user_fn get_foreground_user =
        (user_service_get_user_fn)dlsym(user_service, "sceUserServiceGetForegroundUser");
    user_service_get_user_fn get_initial_user =
        (user_service_get_user_fn)dlsym(user_service, "sceUserServiceGetInitialUser");
    user_service_term_fn user_term =
        (user_service_term_fn)dlsym(user_service, "sceUserServiceTerminate");

    if (!user_init) {
        notify_text("Tayson BGFT D3 USER INIT SYM SKIP");
        notify_code("D3 USER", user_id);
        return user_id;
    }

    OrbisUserServiceInitializeParams init_params = {
        .priority = ORBIS_KERNEL_PRIO_FIFO_NORMAL
    };

    int init_rc = user_init(&init_params);
    notify_code("D3 USER INIT", init_rc);

    int get_rc = -1;
    if (get_foreground_user) {
        get_rc = get_foreground_user(&user_id);
        notify_code("D3 USER FG", get_rc);
    }

    if (get_rc != 0 && get_initial_user) {
        user_id = USER_ID_FALLBACK;
        get_rc = get_initial_user(&user_id);
        notify_code("D3 USER INITIAL", get_rc);
    }

    if (get_rc != 0) {
        user_id = USER_ID_FALLBACK;
        notify_text("Tayson BGFT D3 USER FALLBACK");
    }

    notify_code("D3 USER", user_id);
    if (user_term) {
        int term_rc = user_term();
        notify_code("D3 USER TERM", term_rc);
    }
    return user_id;
}

static void init_appinst_if_available(void)
{
    void *appinst = dlopen("/system/common/lib/libSceAppInstUtil.sprx", 0);
    if (!appinst) {
        notify_text("Tayson BGFT D4 APPINST LOAD SKIP");
        return;
    }

    appinst_init_fn appinst_init =
        (appinst_init_fn)dlsym(appinst, "sceAppInstUtilInitialize");
    if (!appinst_init) {
        notify_text("Tayson BGFT D4 APPINST SYM SKIP");
        return;
    }

    notify_code("D4 APPINST INIT", appinst_init());
}

int main(void)
{
    int rc = setup_cred();
    void *sysutil = dlopen("/system/common/lib/libSceSysUtil.sprx", 0);
    if (sysutil) {
        g_notify = (notify_fn)dlsym(sysutil, "sceSysUtilSendSystemNotificationWithText");
    }
    if (!g_notify) return -100;

    notify_text("Tayson BGFT D0 START");
    if (rc != 0) {
        notify_code("D0 CRED FAIL", rc);
        return rc;
    }

    void *bgft = dlopen("/system/common/lib/libSceBgft.sprx", 0);
    if (!bgft) {
        notify_text("Tayson BGFT D1 LOAD FAIL");
        return -101;
    }
    notify_text("Tayson BGFT D1 LOAD OK");

    bgft_init_fn bgft_init =
        (bgft_init_fn)dlsym(bgft, "sceBgftServiceIntInit");
    bgft_register_fn bgft_register_normal =
        (bgft_register_fn)dlsym(bgft, "sceBgftServiceDownloadRegisterTask");
    bgft_register_fn bgft_register_debug =
        (bgft_register_fn)dlsym(bgft, "sceBgftServiceIntDebugDownloadRegisterPkg");
    bgft_start_fn bgft_start =
        (bgft_start_fn)dlsym(bgft, "sceBgftServiceIntDownloadStartTask");

    if (!bgft_init) {
        notify_text("Tayson BGFT D2 INIT SYM FAIL");
        return -102;
    }
    if (!bgft_register_normal && !bgft_register_debug) {
        notify_text("Tayson BGFT D2 REGISTER SYM FAIL");
        return -103;
    }
    if (!bgft_start) {
        notify_text("Tayson BGFT D2 START SYM FAIL");
        return -104;
    }
    notify_text("Tayson BGFT D2 SYMBOLS OK");

    int user_id = resolve_user_id();
    init_appinst_if_available();

    void *heap = mmap(NULL, 0x100000,
                      PROT_READ | PROT_WRITE,
                      MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (!heap || heap == (void *)-1) {
        notify_text("Tayson BGFT D5 HEAP FAIL");
        return -107;
    }
    notify_text("Tayson BGFT D5 HEAP OK");

    struct bgft_init_params init_params = {
        .mem = heap,
        .size = 0x100000
    };

    rc = bgft_init(&init_params);
    notify_code("D6 INIT", rc);

    /*
     * Preserve the 104-byte 3.4.3 parameter ABI, while filling the optional
     * fields used by the known-working DPI BGFT path. In particular,
     * DISABLE_CDN_QUERY_PARAM prevents BGFT from mutating signed URLs.
     */
    struct bgft_download_param params = {0};
    params.user_id = user_id;
    params.entitlement_type = 5;
    params.id = PKG_CONTENT_ID;
    params.content_url = PKG_URL;
    params.content_name = PKG_NAME;
    params.icon_path = "";
    params.option = BGFT_TASK_OPTION_DISABLE_CDN_QUERY_PARAM;
    params.playgo_scenario_id = "0";
    params.package_type = PKG_TYPE;
    params.package_sub_type = "";
    params.package_size = (unsigned long)PKG_SIZE;

    int task = BGFT_INVALID_TASK_ID;

    if (bgft_register_normal) {
        rc = bgft_register_normal(&params, &task);
        notify_code("D7 REGISTER", rc);
        notify_code("D7 TASK", task);
    } else {
        rc = -105;
        notify_text("Tayson BGFT D7 REGISTER SYM SKIP");
    }

    if (rc != 0 || task == BGFT_INVALID_TASK_ID) {
        if (!bgft_register_debug) {
            return rc ? rc : -108;
        }

        task = BGFT_INVALID_TASK_ID;
        rc = bgft_register_debug(&params, &task);
        notify_code("D7 DEBUG", rc);
        notify_code("D7 DEBUG TASK", task);
    }

    if (rc != 0 || task == BGFT_INVALID_TASK_ID) {
        return rc ? rc : -108;
    }

    rc = bgft_start(task);
    notify_code("D8 START", rc);
    if (rc != 0) return rc;

    notify_text("Tayson BGFT D9 DONE");
    return 0;
}
