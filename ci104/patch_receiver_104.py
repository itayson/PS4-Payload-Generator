from pathlib import Path

p = Path('ps4receiver/receiver/main.c')
t = p.read_text()
t = t.replace('#define RECEIVER_VERSION "1.0.3"', '#define RECEIVER_VERSION "1.0.4"')
t = t.replace(
    'static pthread_mutex_t g_service_init_mutex = PTHREAD_MUTEX_INITIALIZER;',
    '''static pthread_mutex_t g_service_init_mutex = PTHREAD_MUTEX_INITIALIZER;
static char g_service_stage[40] = "idle";
static int32_t g_mod_system_service = -1;
static int32_t g_mod_user_service = -1;
static int32_t g_mod_appinst = -1;
static int32_t g_mod_sysutil = -1;
static int32_t g_mod_bgft = -1;''')

old = '''static int load_module_symbol(const char *path, const char *symbol, void **out) {
    int32_t h = (int32_t)sceKernelLoadStartModule(path, 0, NULL, 0, NULL, NULL);
    if (h < 0) return h;
    *out = NULL;
    int32_t r = sceKernelDlsym(h, symbol, out);
    return (r < 0 || !*out) ? (r < 0 ? r : -1) : h;
}

static void try_init_notifications(void) {
    if (p_sceSysUtilSendSystemNotificationWithText) return;
    void *s = NULL;
    int r = load_module_symbol("/system/common/lib/libSceSysUtil.sprx", "sceSysUtilSendSystemNotificationWithText", &s);
    if (r >= 0) p_sceSysUtilSendSystemNotificationWithText = (receiver_notify_fn)s;
}
'''
new = '''static void set_service_stage(const char *stage) {
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
'''
if old not in t:
    raise SystemExit('1.0.3 loader block not found')
t = t.replace(old, new, 1)

old = '''static int load_install_symbols(void) {
    void *s = NULL;
    int user = load_module_symbol("/system/common/lib/libSceUserService.sprx", "sceUserServiceInitialize", &s);
    if (user < 0) return user;
    p_sceUserServiceInitialize = (receiver_user_init_fn)s;
    int ret = sceKernelDlsym(user, "sceUserServiceGetForegroundUser", &s);
    if (ret < 0 || !s) return ret < 0 ? ret : -1;
    p_sceUserServiceGetForegroundUser = (receiver_user_get_fg_fn)s;
    int app = load_module_symbol("/system/common/lib/libSceAppInstUtil.sprx", "sceAppInstUtilInitialize", &s);
    if (app < 0) return app;
    p_sceAppInstUtilInitialize = (receiver_appinst_init_fn)s;
    int bgft = load_module_symbol("/system/common/lib/libSceBgft.sprx", "sceBgftServiceIntInit", &s);
    if (bgft < 0) return bgft;
    p_sceBgftServiceIntInit = (receiver_bgft_init_fn)s;
    ret = sceKernelDlsym(bgft, "sceBgftServiceIntDebugDownloadRegisterPkg", &s);
    if (ret < 0 || !s) return ret < 0 ? ret : -1;
    p_sceBgftServiceIntDebugDownloadRegisterPkg = (receiver_bgft_register_fn)s;
    ret = sceKernelDlsym(bgft, "sceBgftServiceIntDownloadStartTask", &s);
    if (ret < 0 || !s) return ret < 0 ? ret : -1;
    p_sceBgftServiceIntDownloadStartTask = (receiver_bgft_start_fn)s;
    return 0;
}
'''
new = '''static int load_install_symbols(void) {
    void *s = NULL;
    int ret;

    /* SystemService must precede AppInstUtil. Without it AppInstUtil can fail
     * to load with SCE_KERNEL_ERROR_ENOENT (0x80020002). */
    set_service_stage("systemservice-load");
    ret = load_system_module("libSceSystemService.sprx", &g_mod_system_service);
    if (ret < 0) return ret;

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
    return 0;
}
'''
if old not in t:
    raise SystemExit('1.0.3 install-symbol block not found')
t = t.replace(old, new, 1)

t = t.replace(
    '''static int init_bgft(void) {
    struct { uint32_t priority; } user_params = { ORBIS_KERNEL_PRIO_FIFO_NORMAL };
    int ret = p_sceUserServiceInitialize ? p_sceUserServiceInitialize(&user_params) : -1;''',
    '''static int init_bgft(void) {
    struct { uint32_t priority; } user_params = { ORBIS_KERNEL_PRIO_FIFO_NORMAL };
    set_service_stage("userservice-init");
    int ret = p_sceUserServiceInitialize ? p_sceUserServiceInitialize(&user_params) : -1;''')
t = t.replace(
    '    ret = p_sceAppInstUtilInitialize ? p_sceAppInstUtilInitialize() : -1;',
    '    set_service_stage("appinst-init");\n    ret = p_sceAppInstUtilInitialize ? p_sceAppInstUtilInitialize() : -1;', 1)
t = t.replace(
    '    ret = p_sceBgftServiceIntInit ? p_sceBgftServiceIntInit(&params) : -1;',
    '    set_service_stage("bgft-init");\n    ret = p_sceBgftServiceIntInit ? p_sceBgftServiceIntInit(&params) : -1;', 1)

old = '''static int ensure_transfer_services(void) {
    pthread_mutex_lock(&g_service_init_mutex);
    if (g_transfer_services_ready) { pthread_mutex_unlock(&g_service_init_mutex); return 0; }
    int ret = load_http_stack();
    if (!ret) ret = load_install_symbols();
    if (!ret) ret = init_bgft();
    if (!ret) g_transfer_services_ready = 1;
    pthread_mutex_unlock(&g_service_init_mutex);
    return ret;
}
'''
new = '''static int ensure_transfer_services(void) {
    pthread_mutex_lock(&g_service_init_mutex);
    if (g_transfer_services_ready) { pthread_mutex_unlock(&g_service_init_mutex); return 0; }
    set_service_stage("http-stack");
    int ret = load_http_stack();
    if (!ret) ret = load_install_symbols();
    if (!ret) ret = init_bgft();
    if (!ret) {
        g_transfer_services_ready = 1;
        set_service_stage("ready");
    }
    pthread_mutex_unlock(&g_service_init_mutex);
    return ret;
}
'''
if old not in t:
    raise SystemExit('1.0.3 service-init block not found')
t = t.replace(old, new, 1)

t = t.replace(
    'snprintf(errline, sizeof(errline), "TAYSON/1 ERR services-0x%08X\\n", (unsigned int)service_ret);',
    'snprintf(errline, sizeof(errline), "TAYSON/1 ERR services-%s-0x%08X\\n", g_service_stage, (unsigned int)service_ret);')
t = t.replace(
    '''    try_init_notifications();
    notify_text("Tayson PS4 Receiver 1.0.3 ativo - porta 9095");''',
    '''    notify_text("Tayson PS4 Receiver 1.0.4 ativo - porta 9095");''')

assert 'RECEIVER_VERSION "1.0.4"' in t
assert 'libSceSystemService.sprx' in t
assert 'sceKernelGetFsSandboxRandomWord' in t
assert 'services-%s-0x%08X' in t
assert 'try_init_notifications' not in t
assert 'load_module_symbol' not in t
p.write_text(t)

m = Path('ps4receiver/Makefile')
s = m.read_text().replace('VERSION     := 1.03', 'VERSION     := 1.04')
assert 'VERSION     := 1.04' in s
m.write_text(s)
