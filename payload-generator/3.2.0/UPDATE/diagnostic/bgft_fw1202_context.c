#include <stddef.h>
#include <stdint.h>
#include <sys/mman.h>
#include "ps4-libjbc/jailbreak.h"
#include "struct.h"

/*
 * Tayson 3.5.2 - FW 12.02 BGFT context tracer.
 *
 * Goal: reproduce the inherited 3.4.3 native BGFT contract as closely as
 * possible while adding stage/return-code notifications only.
 *
 * Deliberately NOT changed here:
 *   - no explicit authID / sonyCred / sceProcCap override;
 *   - no AppInstUtil initialization;
 *   - no public/normal RegisterTask fallback;
 *   - no optional BGFT fields or task options;
 *   - no Worker/provider changes.
 *
 * Native flow under test:
 *   jbc_get_cred -> jbc_jailbreak_cred -> jbc_set_cred
 *   -> sceBgftServiceIntInit
 *   -> sceUserServiceInitialize(NULL)
 *   -> sceUserServiceGetInitialUser
 *   -> sceBgftServiceIntDebugDownloadRegisterPkg
 *   -> sceBgftServiceIntDownloadStartTask
 */

__attribute__((used)) char PKG_CONTENT_ID[64] = "###CONTENT_ID_PLACEHOLDER###";
__attribute__((used)) char PKG_URL[2048] = "###URL_PLACEHOLDER###";
__attribute__((used)) char PKG_NAME[128] = "###NAME_PLACEHOLDER###";
__attribute__((used)) char PKG_TYPE[16] = "###TYPE###";
__attribute__((used)) volatile unsigned long PKG_SIZE = 0xBBBBBBBBBBBBBBBBULL;

#define USER_ID_FALLBACK 0x10000000
#define BGFT_INVALID_TASK_ID (-1)

typedef int (*notify_fn)(int, const char *);
typedef int (*bgft_init_fn)(struct bgft_init_params *);
typedef int (*bgft_register_fn)(struct bgft_download_param *, int *);
typedef int (*bgft_start_fn)(int);
typedef int (*user_init_fn)(void *);
typedef int (*user_initial_fn)(int *);

static notify_fn g_notify = 0;

static void append(char *dst, int *pos, const char *src)
{
    while (*src && *pos < 190) dst[(*pos)++] = *src++;
    dst[*pos] = 0;
}

static void hex32(int32_t value, char *out)
{
    static const char digits[] = "0123456789ABCDEF";
    uint32_t v = (uint32_t)value;
    out[0] = '0'; out[1] = 'x';
    for (int i = 0; i < 8; ++i)
        out[2 + i] = digits[(v >> (28 - i * 4)) & 0x0F];
    out[10] = 0;
}

static void notify_text(const char *text)
{
    if (g_notify) g_notify(222, text);
}

static void notify_code(const char *stage, int code)
{
    char msg[192], hex[11];
    int p = 0;
    msg[0] = 0;
    hex32(code, hex);
    append(msg, &p, "Tayson 3.5.2 ");
    append(msg, &p, stage);
    append(msg, &p, " ");
    append(msg, &p, hex);
    notify_text(msg);
}

static int stable_343_setup_jbc(void)
{
    struct jbc_cred cred = {0};
    int rc;

    rc = jbc_get_cred(&cred);
    notify_code("C0 GET_CRED", rc);
    if (rc) return rc;

    rc = jbc_jailbreak_cred(&cred);
    notify_code("C1 JAILBREAK_CRED", rc);
    if (rc) return rc;

    rc = jbc_set_cred(&cred);
    notify_code("C2 SET_CRED", rc);
    return rc;
}

static int stable_343_user_id(void)
{
    int user_id = USER_ID_FALLBACK;
    void *user = dlopen("/system/common/lib/libSceUserService.sprx", 0);
    if (!user) {
        notify_text("Tayson 3.5.2 U0 USER LOAD FAIL");
        notify_code("U4 FINAL USER", user_id);
        return user_id;
    }

    user_init_fn initialize =
        (user_init_fn)dlsym(user, "sceUserServiceInitialize");
    user_initial_fn get_initial =
        (user_initial_fn)dlsym(user, "sceUserServiceGetInitialUser");

    notify_code("U1 INIT SYM", initialize ? 0 : -1);
    notify_code("U2 INITIAL SYM", get_initial ? 0 : -1);

    if (initialize) {
        int rc = initialize(NULL);
        notify_code("U1 INIT RC", rc);
    }

    if (get_initial) {
        int rc = get_initial(&user_id);
        notify_code("U2 INITIAL RC", rc);
        notify_code("U3 INITIAL ID", user_id);
    }

    /*
     * Keep the inherited 3.4.3 behavior: do not reject -1 here and do not
     * substitute GetForegroundUser. This tracer needs to show exactly what
     * the old contract hands to BGFT on FW 12.02.
     */
    notify_code("U4 FINAL USER", user_id);
    return user_id;
}

int main(void)
{
    void *sysutil = dlopen("/system/common/lib/libSceSysUtil.sprx", 0);
    if (sysutil)
        g_notify = (notify_fn)dlsym(sysutil, "sceSysUtilSendSystemNotificationWithText");
    if (!g_notify) return -100;

    notify_text("Tayson 3.5.2 FW12.02 START");

    int rc = stable_343_setup_jbc();
    if (rc) {
        notify_code("C3 CRED FAIL", rc);
        return rc;
    }
    notify_text("Tayson 3.5.2 C3 CRED OK");

    void *bgft = dlopen("/system/common/lib/libSceBgft.sprx", 0);
    if (!bgft) {
        notify_text("Tayson 3.5.2 B0 BGFT LOAD FAIL");
        return -101;
    }
    notify_text("Tayson 3.5.2 B0 BGFT LOAD OK");

    bgft_init_fn bgft_init =
        (bgft_init_fn)dlsym(bgft, "sceBgftServiceIntInit");
    bgft_register_fn bgft_register =
        (bgft_register_fn)dlsym(bgft, "sceBgftServiceIntDebugDownloadRegisterPkg");
    bgft_start_fn bgft_start =
        (bgft_start_fn)dlsym(bgft, "sceBgftServiceIntDownloadStartTask");

    notify_code("B1 INIT SYM", bgft_init ? 0 : -1);
    notify_code("B2 REG SYM", bgft_register ? 0 : -1);
    notify_code("B3 START SYM", bgft_start ? 0 : -1);
    if (!bgft_init || !bgft_register || !bgft_start) return -102;

    void *heap = mmap(NULL, 0x100000,
                      PROT_READ | PROT_WRITE,
                      MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (!heap || heap == (void *)-1) {
        notify_text("Tayson 3.5.2 B4 HEAP FAIL");
        return -103;
    }
    notify_text("Tayson 3.5.2 B4 HEAP OK");

    struct bgft_init_params init_params = {
        .mem = heap,
        .size = 0x100000
    };

    rc = bgft_init(&init_params);
    notify_code("B5 INT INIT", rc);

    int user_id = stable_343_user_id();

    /*
     * Match 3.4.3: zero the complete 104-byte struct and fill only the fields
     * that the inherited ELF populated.
     */
    struct bgft_download_param params = {0};
    params.user_id = user_id;
    params.entitlement_type = 5;
    params.id = PKG_CONTENT_ID;
    params.content_url = PKG_URL;
    params.content_name = PKG_NAME;
    params.package_type = PKG_TYPE;
    params.package_size = (unsigned long)PKG_SIZE;

    notify_code("R0 USER", params.user_id);
    notify_code("R1 ENTITLEMENT", params.entitlement_type);

    int task = BGFT_INVALID_TASK_ID;
    rc = bgft_register(&params, &task);
    notify_code("R2 REGISTER", rc);
    notify_code("R3 TASK", task);

    if (rc || task == BGFT_INVALID_TASK_ID)
        return rc ? rc : -104;

    rc = bgft_start(task);
    notify_code("R4 START", rc);
    if (rc) return rc;

    notify_text("Tayson 3.5.2 DONE");
    return 0;
}
