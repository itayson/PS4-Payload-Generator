#include <stddef.h>
#include <stdint.h>
#include <sys/mman.h>
#include "ps4-libjbc/jailbreak.h"
#include "struct.h"

/*
 * Tayson 3.4.6 BGFT service-API laboratory payload.
 *
 * Purpose: isolate the BGFT API family from the Android product flow.
 * The Android app still supplies the validated URL/metadata through the same
 * fixed markers, but this native payload deliberately uses one BGFT path only:
 *
 *   sceBgftServiceInit
 *   sceBgftServiceDownloadRegisterTask
 *   sceBgftServiceIntDownloadStartTask
 *
 * No IntDebug registration fallback is used. A failure therefore identifies
 * this service-API path directly.
 */

__attribute__((used)) char PKG_CONTENT_ID[64] = "###CONTENT_ID_PLACEHOLDER###";
__attribute__((used)) char PKG_URL[2048] = "###URL_PLACEHOLDER###";
__attribute__((used)) char PKG_NAME[128] = "###NAME_PLACEHOLDER###";
__attribute__((used)) char PKG_TYPE[16] = "###TYPE###";
__attribute__((used)) volatile unsigned long PKG_SIZE = 0xBBBBBBBBBBBBBBBBULL;

#define BGFT_INVALID_TASK_ID (-1)
#define USER_ID_FALLBACK 0x10000000
#define BGFT_ALREADY_INITIALIZED ((uint32_t)0x80990001u)

typedef int (*notify_fn)(int, const char *);
typedef int (*bgft_init_fn)(struct bgft_init_params *);
typedef int (*bgft_register_fn)(struct bgft_download_param *, int *);
typedef int (*bgft_start_fn)(int);
typedef int (*user_service_init_fn)(void *);
typedef int (*user_service_get_user_fn)(int *);
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
    append(msg, &p, "Tayson LAB ");
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
        notify_text("Tayson LAB L3 USER LOAD SKIP");
        notify_code("L3 USER", user_id);
        return user_id;
    }

    user_service_init_fn user_init =
        (user_service_init_fn)dlsym(user_service, "sceUserServiceInitialize");
    user_service_get_user_fn get_initial_user =
        (user_service_get_user_fn)dlsym(user_service, "sceUserServiceGetInitialUser");
    user_service_get_user_fn get_foreground_user =
        (user_service_get_user_fn)dlsym(user_service, "sceUserServiceGetForegroundUser");

    if (!user_init || !get_initial_user) {
        notify_text("Tayson LAB L3 USER SYM SKIP");
        notify_code("L3 USER", user_id);
        return user_id;
    }

    int init_rc = user_init(NULL);
    notify_code("L3 USER INIT", init_rc);

    int get_rc = get_initial_user(&user_id);
    notify_code("L3 USER INITIAL", get_rc);
    notify_code("L3 INITIAL ID", user_id);

    if (get_rc != 0 || user_id == -1) {
        if (get_foreground_user) {
            int foreground_user = -1;
            int fg_rc = get_foreground_user(&foreground_user);
            notify_code("L3 USER FG", fg_rc);
            notify_code("L3 USER FG ID", foreground_user);
            if (fg_rc == 0 && foreground_user != -1) {
                user_id = foreground_user;
            }
        }
    }

    if (user_id == -1) {
        user_id = USER_ID_FALLBACK;
        notify_text("Tayson LAB L3 USER FALLBACK");
    }

    notify_code("L3 USER", user_id);
    return user_id;
}

static int init_appinst(void)
{
    void *appinst = dlopen("/system/common/lib/libSceAppInstUtil.sprx", 0);
    if (!appinst) {
        notify_text("Tayson LAB L4 APPINST LOAD FAIL");
        return -105;
    }

    appinst_init_fn appinst_init =
        (appinst_init_fn)dlsym(appinst, "sceAppInstUtilInitialize");
    if (!appinst_init) {
        notify_text("Tayson LAB L4 APPINST SYM FAIL");
        return -106;
    }

    int rc = appinst_init();
    notify_code("L4 APPINST INIT", rc);
    return rc;
}

int main(void)
{
    int rc = setup_cred();

    void *sysutil = dlopen("/system/common/lib/libSceSysUtil.sprx", 0);
    if (sysutil) {
        g_notify = (notify_fn)dlsym(sysutil, "sceSysUtilSendSystemNotificationWithText");
    }
    if (!g_notify) return -100;

    notify_text("Tayson LAB L0 START");
    if (rc != 0) {
        notify_code("L0 CRED FAIL", rc);
        return rc;
    }

    void *bgft = dlopen("/system/common/lib/libSceBgft.sprx", 0);
    if (!bgft) {
        notify_text("Tayson LAB L1 BGFT LOAD FAIL");
        return -101;
    }
    notify_text("Tayson LAB L1 BGFT LOAD OK");

    bgft_init_fn bgft_init =
        (bgft_init_fn)dlsym(bgft, "sceBgftServiceInit");
    bgft_register_fn bgft_register =
        (bgft_register_fn)dlsym(bgft, "sceBgftServiceDownloadRegisterTask");
    bgft_start_fn bgft_start =
        (bgft_start_fn)dlsym(bgft, "sceBgftServiceIntDownloadStartTask");

    if (!bgft_init) {
        notify_text("Tayson LAB L2 SERVICE INIT SYM FAIL");
        return -102;
    }
    if (!bgft_register) {
        notify_text("Tayson LAB L2 SERVICE REG SYM FAIL");
        return -103;
    }
    if (!bgft_start) {
        notify_text("Tayson LAB L2 START SYM FAIL");
        return -104;
    }
    notify_text("Tayson LAB L2 SYMBOLS OK");

    int user_id = resolve_user_id();

    rc = init_appinst();
    if (rc != 0) return rc;

    void *heap = mmap(NULL, 0x100000,
                      PROT_READ | PROT_WRITE,
                      MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (!heap || heap == (void *)-1) {
        notify_text("Tayson LAB L5 HEAP FAIL");
        return -107;
    }
    notify_text("Tayson LAB L5 HEAP OK");

    struct bgft_init_params init_params = {
        .mem = heap,
        .size = 0x100000
    };

    rc = bgft_init(&init_params);
    notify_code("L6 SERVICE INIT", rc);
    if (rc != 0 && (uint32_t)rc != BGFT_ALREADY_INITIALIZED) {
        return rc;
    }

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
    rc = bgft_register(&params, &task);
    notify_code("L7 SERVICE REGISTER", rc);
    notify_code("L7 TASK", task);

    if (rc != 0 || task == BGFT_INVALID_TASK_ID) {
        return rc ? rc : -108;
    }

    rc = bgft_start(task);
    notify_code("L8 START", rc);
    if (rc != 0) return rc;

    notify_text("Tayson LAB L9 DONE");
    return 0;
}
