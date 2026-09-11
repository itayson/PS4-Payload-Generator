from pathlib import Path
p=Path('ps4receiver/receiver/main.c')
t=p.read_text()
t=t.replace('#define RECEIVER_VERSION "1.3.0"', '#define RECEIVER_VERSION "1.4.0"')
t=t.replace('typedef void (*receiver_notify_fn)(int type, const char *text);', 'typedef void (*receiver_notify_fn)(int type, const char *text);\ntypedef int32_t (*receiver_daemon_register_fn)(void);')
t=t.replace('static receiver_notify_fn p_sceSysUtilSendSystemNotificationWithText = NULL;', 'static receiver_notify_fn p_sceSysUtilSendSystemNotificationWithText = NULL;\nstatic receiver_daemon_register_fn p_sceSystemServiceRegisterDaemon = NULL;')
t=t.replace('static int g_privileged_credentials_ready = 0;', 'static int g_privileged_credentials_ready = 0;\nstatic int g_daemon_registered = 0;\nstatic int g_daemon_register_error = 0;')
marker='''static int load_install_symbols(void) {\n'''
helper='''static int enable_background_daemon_mode(void) {\n    if (g_daemon_registered) return 0;\n    if (!g_privileged_credentials_ready) {\n        g_daemon_register_error = -1;\n        return -1;\n    }\n    if (g_mod_system_service < 0) {\n        set_service_stage("daemon-systemservice-load");\n        int ret = load_system_module("libSceSystemService.sprx", &g_mod_system_service);\n        if (ret < 0) { g_daemon_register_error = ret; return ret; }\n    }\n    if (!p_sceSystemServiceRegisterDaemon) {\n        void *s = NULL;\n        set_service_stage("daemon-symbol");\n        int ret = resolve_symbol(g_mod_system_service, "sceSystemServiceRegisterDaemon", &s);\n        if (ret < 0) { g_daemon_register_error = ret; return ret; }\n        p_sceSystemServiceRegisterDaemon = (receiver_daemon_register_fn)s;\n    }\n    set_service_stage("daemon-register");\n    int ret = p_sceSystemServiceRegisterDaemon();\n    g_daemon_register_error = ret;\n    if (ret >= 0) {\n        g_daemon_registered = 1;\n        g_daemon_register_error = 0;\n        return 0;\n    }\n    return ret;\n}\n\nstatic int load_install_symbols(void) {\n'''
assert marker in t
t=t.replace(marker, helper, 1)
old='''    set_service_stage("systemservice-load");\n    ret = load_system_module("libSceSystemService.sprx", &g_mod_system_service);\n    if (ret < 0) return ret;\n'''
new='''    set_service_stage("systemservice-load");\n    if (g_mod_system_service < 0) {\n        ret = load_system_module("libSceSystemService.sprx", &g_mod_system_service);\n        if (ret < 0) return ret;\n    }\n'''
assert old in t
t=t.replace(old,new,1)
old='''    int ret = load_http_stack();\n    if (!ret) ret = elevate_receiver_credentials();\n    if (!ret) ret = load_install_symbols();\n    if (!ret) ret = init_bgft();\n'''
new='''    int ret = load_http_stack();\n    if (!ret) ret = elevate_receiver_credentials();\n    if (!ret) {\n        int daemon_ret = enable_background_daemon_mode();\n        if (daemon_ret < 0) g_daemon_register_error = daemon_ret;\n    }\n    if (!ret) ret = load_install_symbols();\n    if (!ret) ret = init_bgft();\n'''
assert old in t
t=t.replace(old,new,1)
old='''    int n = snprintf(line, sizeof(line), "TAYSON/1 STATUS receiver=%s count=%d restored=%d persist=1\\n", RECEIVER_VERSION, count, g_restored_jobs);\n'''
new='''    int n = snprintf(line, sizeof(line), "TAYSON/1 STATUS receiver=%s count=%d restored=%d persist=1 daemon=%d daemonerr=%d\\n", RECEIVER_VERSION, count, g_restored_jobs, g_daemon_registered, g_daemon_register_error);\n'''
assert old in t
t=t.replace(old,new,1)
old='''    int restored = load_persisted_jobs();\n    if (restored < 0) {\n        unlink(PERSIST_PATH);\n        g_restored_jobs = 0;\n    }\n\n    pthread_t proxy;\n'''
new='''    int restored = load_persisted_jobs();\n    if (restored < 0) {\n        unlink(PERSIST_PATH);\n        g_restored_jobs = 0;\n    }\n    if (restored > 0) {\n        int warm = ensure_transfer_services();\n        if (warm < 0) notify_code("Tayson Receiver: restore services", warm);\n    }\n\n    pthread_t proxy;\n'''
assert old in t
t=t.replace(old,new,1)
t=t.replace('notify_text("Tayson PS4 Receiver 1.3.0 ativo - porta 9095");', 'notify_text(g_daemon_registered ? "Tayson Receiver 1.4.0 daemon ativo - porta 9095" : "Tayson Receiver 1.4.0 ativo - porta 9095");')
p.write_text(t)
m=Path('ps4receiver/Makefile')
s=m.read_text().replace('VERSION     := 1.30','VERSION     := 1.40')
m.write_text(s)
final=p.read_text()
assert 'RECEIVER_VERSION "1.4.0"' in final
assert 'sceSystemServiceRegisterDaemon' in final
assert 'daemon=%d daemonerr=%d' in final
assert 'if (restored > 0)' in final
assert 'VERSION     := 1.40' in m.read_text()
