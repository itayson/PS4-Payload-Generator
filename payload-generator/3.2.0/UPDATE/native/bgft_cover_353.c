#include <stddef.h>
#include <stdint.h>
#include <sys/mman.h>
#include <sys/types.h>
#include <fcntl.h>
#include <unistd.h>
#include "ps4-libjbc/jailbreak.h"
#include "struct.h"

__attribute__((used)) char PKG_CONTENT_ID[64] = "###CONTENT_ID_PLACEHOLDER###";
__attribute__((used)) char PKG_URL[2048] = "###URL_PLACEHOLDER###";
__attribute__((used)) char PKG_NAME[128] = "###NAME_PLACEHOLDER###";
__attribute__((used)) char PKG_TYPE[16] = "###TYPE###";
__attribute__((used)) volatile unsigned long PKG_SIZE = 0xBBBBBBBBBBBBBBBBULL;

#define ICON_CAPACITY 1048576
#define ICON_SIZE_MARKER 0xCCCCCCCCu
#define ICON_PATH "/user/data/tayson_bgft_icon.png"

__attribute__((used, aligned(16)))
unsigned char PKG_ICON[ICON_CAPACITY] = "###ICON_PLACEHOLDER###";
__attribute__((used))
volatile uint32_t PKG_ICON_SIZE = ICON_SIZE_MARKER;

typedef int (*notify_fn)(int, const char *);
typedef int (*bgft_init_fn)(struct bgft_init_params *);
typedef int (*bgft_register_fn)(struct bgft_download_param *, int *);
typedef int (*bgft_start_fn)(int);
typedef int (*user_service_init_fn)(void *);
typedef int (*user_service_get_user_fn)(int *);

static notify_fn g_notify = 0;

static void notify_text(const char *text)
{
    if (g_notify) g_notify(222, text);
}

static int setup_jbc_stable(void)
{
    struct jbc_cred cred = {0};
    if (jbc_get_cred(&cred) != 0) return -1;
    if (jbc_jailbreak_cred(&cred) != 0) return -2;
    if (jbc_set_cred(&cred) != 0) return -3;
    return 0;
}

static int write_all(int fd, const unsigned char *data, uint32_t size)
{
    uint32_t done = 0;
    while (done < size) {
        ssize_t n = write(fd, data + done, size - done);
        if (n <= 0) return -1;
        done += (uint32_t)n;
    }
    return 0;
}

static const char *prepare_icon(void)
{
    uint32_t size = PKG_ICON_SIZE;
    if (size == 0 || size == ICON_SIZE_MARKER || size > ICON_CAPACITY)
        return NULL;

    int fd = open(ICON_PATH, O_WRONLY | O_CREAT | O_TRUNC, 0777);
    if (fd < 0) return NULL;

    int rc = write_all(fd, PKG_ICON, size);
    close(fd);
    if (rc != 0) {
        unlink(ICON_PATH);
        return NULL;
    }

    return ICON_PATH;
}

static int initial_user_id(void)
{
    int user_id = 0x10000000;
    void *user = dlopen("/system/common/lib/libSceUserService.sprx", 0);
    if (!user) return user_id;

    user_service_init_fn initialize =
        (user_service_init_fn)dlsym(user, "sceUserServiceInitialize");
    user_service_get_user_fn get_initial =
        (user_service_get_user_fn)dlsym(user, "sceUserServiceGetInitialUser");

    if (initialize && get_initial) {
        initialize(NULL);
        get_initial(&user_id);
    }
    return user_id;
}

static void notify_error(const char *prefix, int rc)
{
    char msg[96];
    static const char hex[] = "0123456789ABCDEF";
    int p = 0;
    while (*prefix && p < 70) msg[p++] = *prefix++;
    msg[p++] = ':';
    msg[p++] = ' ';
    msg[p++] = '0';
    msg[p++] = 'x';
    uint32_t v = (uint32_t)rc;
    for (int i = 0; i < 8; ++i)
        msg[p++] = hex[(v >> (28 - i * 4)) & 0x0f];
    msg[p] = 0;
    notify_text(msg);
}

int main(void)
{
    int rc = setup_jbc_stable();

    void *sysutil = dlopen("/system/common/lib/libSceSysUtil.sprx", 0);
    if (sysutil)
        g_notify = (notify_fn)dlsym(sysutil, "sceSysUtilSendSystemNotificationWithText");
    if (!g_notify) return -100;

    if (rc != 0) {
        notify_error("Cred", rc);
        return rc;
    }

    void *bgft = dlopen("/system/common/lib/libSceBgft.sprx", 0);
    if (!bgft) return -101;

    bgft_init_fn bgft_init =
        (bgft_init_fn)dlsym(bgft, "sceBgftServiceIntInit");
    bgft_register_fn bgft_register =
        (bgft_register_fn)dlsym(bgft, "sceBgftServiceIntDebugDownloadRegisterPkg");
    bgft_start_fn bgft_start =
        (bgft_start_fn)dlsym(bgft, "sceBgftServiceIntDownloadStartTask");
    if (!bgft_init || !bgft_register || !bgft_start) return -102;

    void *heap = mmap(NULL, 0x100000,
                      PROT_READ | PROT_WRITE,
                      MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (!heap || heap == (void *)-1) return -103;

    struct bgft_init_params init_params = {
        .mem = heap,
        .size = 0x100000
    };
    bgft_init(&init_params);

    int user_id = initial_user_id();
    const char *icon_path = prepare_icon();

    struct bgft_download_param params = {0};
    params.user_id = user_id;
    params.entitlement_type = 5;
    params.id = PKG_CONTENT_ID;
    params.content_url = PKG_URL;
    params.content_name = PKG_NAME;
    params.icon_path = icon_path;
    params.package_type = PKG_TYPE;
    params.package_size = (unsigned long)PKG_SIZE;

    int task = -1;
    rc = bgft_register(&params, &task);
    if (rc < 0) {
        notify_error("Registro", rc);
        return rc;
    }

    rc = bgft_start(task);
    if (rc < 0) {
        notify_error("Erro Inicio", rc);
        return rc;
    }

    if (icon_path)
        notify_text("BGFT iniciado com capa do jogo");
    else
        notify_text("BGFT iniciado sem capa");

    return 0;
}
