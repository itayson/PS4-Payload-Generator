from pathlib import Path
p=Path('ps4receiver/receiver/main.c')
t=p.read_text()
t=t.replace('#define RECEIVER_VERSION "1.2.1"', '#define RECEIVER_VERSION "1.2.2"')
t=t.replace('#define BGFT_INVALID_TASK_ID (-1)\n', '#define BGFT_INVALID_TASK_ID (-1)\n#define BGFT_ERROR_CONTENT_ALREADY_DOWNLOADING 0x80990086U\n#define BGFT_SUBTYPE_GAME 6\n#define BGFT_SUBTYPE_GAME_AC 7\n#define BGFT_SUBTYPE_GAME_PATCH 8\n#define BGFT_SUBTYPE_PACKAGE 12\n',1)
anchor='''typedef int32_t (*receiver_bgft_register_fn)(receiver_bgft_download_param *params, int32_t *task_id);\ntypedef int32_t (*receiver_bgft_start_fn)(int32_t task_id);\ntypedef void (*receiver_notify_fn)(int type, const char *text);\n'''
insert='''typedef int32_t (*receiver_bgft_register_fn)(receiver_bgft_download_param *params, int32_t *task_id);\ntypedef int32_t (*receiver_bgft_start_fn)(int32_t task_id);\ntypedef int32_t (*receiver_bgft_find_fn)(const char *content_id, int sub_type, int32_t *task_id);\ntypedef int32_t (*receiver_bgft_stop_fn)(int32_t task_id);\ntypedef int32_t (*receiver_bgft_unregister_fn)(int32_t task_id);\ntypedef void (*receiver_notify_fn)(int type, const char *text);\n'''
assert anchor in t
t=t.replace(anchor,insert,1)
anchor='''static receiver_bgft_register_fn p_sceBgftServiceIntDebugDownloadRegisterPkg = NULL;\nstatic receiver_bgft_start_fn p_sceBgftServiceIntDownloadStartTask = NULL;\nstatic receiver_notify_fn p_sceSysUtilSendSystemNotificationWithText = NULL;\n'''
insert='''static receiver_bgft_register_fn p_sceBgftServiceIntDebugDownloadRegisterPkg = NULL;\nstatic receiver_bgft_start_fn p_sceBgftServiceIntDownloadStartTask = NULL;\nstatic receiver_bgft_find_fn p_sceBgftServiceDownloadFindTaskByContentId = NULL;\nstatic receiver_bgft_stop_fn p_sceBgftServiceIntDownloadStopTask = NULL;\nstatic receiver_bgft_unregister_fn p_sceBgftServiceIntDownloadUnregisterTask = NULL;\nstatic receiver_notify_fn p_sceSysUtilSendSystemNotificationWithText = NULL;\n'''
assert anchor in t
t=t.replace(anchor,insert,1)
old='''    set_service_stage("bgft-start-symbol");\n    ret = resolve_symbol(g_mod_bgft, "sceBgftServiceIntDownloadStartTask", &s);\n    if (ret < 0) return ret;\n    p_sceBgftServiceIntDownloadStartTask = (receiver_bgft_start_fn)s;\n    return 0;\n}\n'''
new='''    set_service_stage("bgft-start-symbol");\n    ret = resolve_symbol(g_mod_bgft, "sceBgftServiceIntDownloadStartTask", &s);\n    if (ret < 0) return ret;\n    p_sceBgftServiceIntDownloadStartTask = (receiver_bgft_start_fn)s;\n\n    /* Recovery helpers are optional: older firmwares may expose a smaller\n     * symbol set. Normal installs keep working even if one is unavailable. */\n    if (resolve_symbol(g_mod_bgft, "sceBgftServiceDownloadFindTaskByContentId", &s) == 0)\n        p_sceBgftServiceDownloadFindTaskByContentId = (receiver_bgft_find_fn)s;\n    if (resolve_symbol(g_mod_bgft, "sceBgftServiceIntDownloadStopTask", &s) == 0)\n        p_sceBgftServiceIntDownloadStopTask = (receiver_bgft_stop_fn)s;\n    if (resolve_symbol(g_mod_bgft, "sceBgftServiceIntDownloadUnregisterTask", &s) == 0)\n        p_sceBgftServiceIntDownloadUnregisterTask = (receiver_bgft_unregister_fn)s;\n    return 0;\n}\n'''
assert old in t
t=t.replace(old,new,1)
marker='static int register_bgft(receiver_job *job) {'
idx=t.index(marker)
helper=r'''static int find_existing_bgft_task(const char *content_id, int32_t *task_id) {
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

'''
t=t[:idx]+helper+t[idx:]
old='''    int32_t task_id = BGFT_INVALID_TASK_ID;\n    ret = p_sceBgftServiceIntDebugDownloadRegisterPkg ? p_sceBgftServiceIntDebugDownloadRegisterPkg(&params, &task_id) : -1;\n    if (ret) return ret;\n    if (task_id == BGFT_INVALID_TASK_ID) return -1;\n'''
new='''    int32_t task_id = BGFT_INVALID_TASK_ID;\n    ret = p_sceBgftServiceIntDebugDownloadRegisterPkg ? p_sceBgftServiceIntDebugDownloadRegisterPkg(&params, &task_id) : -1;\n    if ((uint32_t)ret == BGFT_ERROR_CONTENT_ALREADY_DOWNLOADING) {\n        int32_t stale_task = BGFT_INVALID_TASK_ID;\n        int cleanup = cleanup_stale_bgft_task(job->content_id, &stale_task);\n        if (cleanup == 0) {\n            char note[192];\n            snprintf(note, sizeof(note), "Tayson Receiver: removendo tarefa BGFT antiga %d", stale_task);\n            notify_text(note);\n            sleep(1);\n            task_id = BGFT_INVALID_TASK_ID;\n            ret = p_sceBgftServiceIntDebugDownloadRegisterPkg ? p_sceBgftServiceIntDebugDownloadRegisterPkg(&params, &task_id) : -1;\n        }\n    }\n    if (ret) return ret;\n    if (task_id == BGFT_INVALID_TASK_ID) return -1;\n'''
assert old in t
t=t.replace(old,new,1)
p.write_text(t)
m=Path('ps4receiver/Makefile')
s=m.read_text().replace('VERSION     := 1.21','VERSION     := 1.22')
m.write_text(s)
