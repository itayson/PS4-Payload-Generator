#include <stddef.h>
#include <stdint.h>
#include <sys/mman.h>
#include "ps4-libjbc/jailbreak.h"
#include "struct.h"

/*
 * Tayson 3.4.5 BGFT stage diagnostic.
 *
 * This payload intentionally reports only stage names and integer return
 * codes. It never prints the PKG URL, cookies, tokens or other credentials.
 *
 * The four metadata buffers and the 0xBB size marker are patched by the
 * Android app before the ELF is sent to GoldHEN BinLoader.
 */

__attribute__((used)) char PKG_CONTENT_ID[64] = "###CONTENT_ID_PLACEHOLDER###";
__attribute__((used)) char PKG_URL[2048] = "###URL_PLACEHOLDER###";
__attribute__((used)) char PKG_NAME[128] = "###NAME_PLACEHOLDER###";
__attribute__((used)) char PKG_TYPE[16] = "###TYPE###";
__attribute__((used)) volatile unsigned long PKG_SIZE = 0xBBBBBBBBBBBBBBBBULL;

#define BGFT_INVALID_TASK_ID (-1)
#define BGFT_TASK_OPTION_DISABLE_CDN_QUERY_PARAM 0x10000

typedef int (*notify_fn)(int, const char *);
typedef int (*bgft_init_fn)(struct bgft_init_params *);
typedef int (*bgft_register_fn)(struct bgft_download_param *, int *);
typedef int (*bgft_start_fn)(int);
typedef int (*appinst_init_fn)(void);

static notify_fn g_notify = 0;

asm("clear_stack:\n"
    "mov $0x800,%ecx\n"
    "xor %rax,%rax\n"
    ".Ldiag_clear:\n"
    "push %rax\n"
    "loop .Ldiag_clear\n"
    "add $0x4000,%rsp\n"
    "ret");
void clear_stack(void);

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

    cred.jdir = 0;
    cred.sceProcType = 0x3800000000000010ULL;
    cred.sonyCred = 0x40001c0000000000ULL;
    cred.sceProcCap = 0x900000000000ff00ULL;
    if (jbc_set_cred(&cred) != 0) return -3;
    return 0;
}

int main(void)
{
    int rc = setup_cred();
    clear_stack();

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
    bgft_register_fn bgft_register =
        (bgft_register_fn)dlsym(bgft, "sceBgftServiceIntDebugDownloadRegisterPkg");
    bgft_start_fn bgft_start =
        (bgft_start_fn)dlsym(bgft, "sceBgftServiceIntDownloadStartTask");

    if (!bgft_init) {
        notify_text("Tayson BGFT D2 INIT SYM FAIL");
        return -102;
    }
    if (!bgft_register) {
        notify_text("Tayson BGFT D2 REGISTER SYM FAIL");
        return -103;
    }
    if (!bgft_start) {
        notify_text("Tayson BGFT D2 START SYM FAIL");
        return -104;
    }
    notify_text("Tayson BGFT D2 SYMBOLS OK");

    void *appinst = dlopen("/system/common/lib/libSceAppInstUtil.sprx", 0);
    if (!appinst) {
        notify_text("Tayson BGFT D3 APPINST LOAD FAIL");
        return -105;
    }
    appinst_init_fn appinst_init =
        (appinst_init_fn)dlsym(appinst, "sceAppInstUtilInitialize");
    if (!appinst_init) {
        notify_text("Tayson BGFT D3 APPINST SYM FAIL");
        return -106;
    }
    rc = appinst_init();
    notify_code("D3 APPINST", rc);
    if (rc < 0) return rc;

    void *heap = mmap(NULL, 0x100000,
                      PROT_READ | PROT_WRITE,
                      MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (!heap || heap == (void *)-1) {
        notify_text("Tayson BGFT D4 HEAP FAIL");
        return -107;
    }
    notify_text("Tayson BGFT D4 HEAP OK");

    struct bgft_init_params init_params = {
        .mem = heap,
        .size = 0x100000
    };

    rc = bgft_init(&init_params);
    notify_code("D5 INIT", rc);
    /* 0x80990001 is the known already-initialized result. */
    if (rc != 0 && (uint32_t)rc != 0x80990001U) return rc;

    struct bgft_download_param params = {
        .user_id = 0x10000000,
        .entitlement_type = 5,
        .id = PKG_CONTENT_ID,
        .content_url = PKG_URL,
        .content_ex_url = 0,
        .content_name = PKG_NAME,
        .icon_path = "",
        .sku_id = "",
        .option = BGFT_TASK_OPTION_DISABLE_CDN_QUERY_PARAM,
        .playgo_scenario_id = "0",
        .release_date = "",
        .package_type = PKG_TYPE,
        .package_sub_type = "",
        .package_size = 0
    };

    params.package_size = (unsigned long)PKG_SIZE;

    int task = BGFT_INVALID_TASK_ID;
    rc = bgft_register(&params, &task);
    notify_code("D6 REGISTER", rc);
    notify_code("D7 TASK", task);
    if (rc < 0 || task == BGFT_INVALID_TASK_ID) return rc ? rc : -108;

    rc = bgft_start(task);
    notify_code("D8 START", rc);
    if (rc < 0) return rc;

    notify_text("Tayson BGFT D9 DONE");
    return 0;
}
