from pathlib import Path
p=Path('ps4receiver/receiver/main.c')
t=p.read_text()
t=t.replace('#define RECEIVER_VERSION "1.2.0"', '#define RECEIVER_VERSION "1.2.1"')
t=t.replace('#define SEGMENT_SIZE (8 * 1024 * 1024)\n#define SEGMENT_WINDOW 3\n#define SEGMENT_MIN_BODY (16 * 1024 * 1024)\n#define MAX_SEGMENT_FETCHERS 4\n',
'''#define SEGMENT_SIZE (32 * 1024 * 1024)\n#define SEGMENT_WINDOW 2\n#define SEGMENT_MIN_BODY (64 * 1024 * 1024)\n#define MAX_SEGMENT_FETCHERS 2\n#define RATE_LIMIT_RETRIES 2\n#define RATE_LIMIT_BACKOFF_BASE_SEC 4\n''')
anchor='''static receiver_job *find_job_locked(uint32_t id) {\n    for (int i = 0; i < MAX_JOBS; i++) {\n        if (g_jobs[i].used && g_jobs[i].id == id) return &g_jobs[i];\n    }\n    return NULL;\n}\n\n'''
insert=anchor+'''static int find_duplicate_active_job(const receiver_job *source, uint32_t *id_out, int *task_out) {\n    int found = 0;\n    pthread_mutex_lock(&g_jobs_mutex);\n    for (int i = 0; i < MAX_JOBS; i++) {\n        receiver_job *j = &g_jobs[i];\n        if (!j->used || j->task_id == BGFT_INVALID_TASK_ID || j->last_error != 0) continue;\n        if (j->package_size != source->package_size) continue;\n        if (strcmp(j->content_id, source->content_id) != 0) continue;\n        if (j->total_served >= j->package_size) continue;\n        if (id_out) *id_out = j->id;\n        if (task_out) *task_out = j->task_id;\n        found = 1;\n        break;\n    }\n    pthread_mutex_unlock(&g_jobs_mutex);\n    return found;\n}\n\n'''
assert anchor in t
t=t.replace(anchor,insert,1)
anchor='''    return 0;\n}\n\nstatic int upstream_preflight(receiver_job *job) {\n'''
helper='''    return 0;\n}\n\nstatic int upstream_open_with_backoff(const receiver_job *job, const char *range, upstream_request *u) {\n    for (int attempt = 0; attempt <= RATE_LIMIT_RETRIES; attempt++) {\n        int ret = upstream_open(job, range, u);\n        if (ret < 0) return ret;\n        if (u->status != 429) return 0;\n        upstream_close(u);\n        if (attempt == RATE_LIMIT_RETRIES) return -429;\n        unsigned int delay = (unsigned int)(RATE_LIMIT_BACKOFF_BASE_SEC << attempt);\n        sleep(delay);\n    }\n    return -429;\n}\n\nstatic int upstream_preflight(receiver_job *job) {\n'''
assert anchor in t
t=t.replace(anchor,helper,1)
old='''    int ret = upstream_open(job, "bytes=0-0", &u);\n    if (ret < 0) return ret;\n'''
new='''    int ret = upstream_open_with_backoff(job, "bytes=0-0", &u);\n    if (ret < 0) return ret;\n'''
assert old in t
t=t.replace(old,new,1)
old='''    upstream_request u;\n    int ret = upstream_open(job, range, &u);\n    if (ret < 0) return ret;\n    if (u.status != 206 || u.length_type != ORBIS_HTTP_CONTENTLEN_EXIST) {\n        upstream_close(&u);\n        return -502;\n    }\n'''
new='''    upstream_request u;\n    int ret = upstream_open_with_backoff(job, range, &u);\n    if (ret < 0) return ret;\n    if (u.status != 206 || u.length_type != ORBIS_HTTP_CONTENTLEN_EXIST) {\n        int status = u.status;\n        upstream_close(&u);\n        return status > 0 ? -status : -502;\n    }\n'''
assert old in t
t=t.replace(old,new,1)
old='''    int failed = 0;\n    int client_failed = 0;\n    uint64_t current = start;\n'''
new='''    int failed = 0;\n    int client_failed = 0;\n    int fail_status = 0;\n    uint64_t current = start;\n'''
assert old in t
t=t.replace(old,new,1)
old='''        if (tasks[idx].result != 0 || !tasks[idx].data) {\n            failed = 1;\n            break;\n        }\n'''
new='''        if (tasks[idx].result != 0 || !tasks[idx].data) {\n            fail_status = tasks[idx].status;\n            failed = 1;\n            break;\n        }\n'''
assert old in t
t=t.replace(old,new,1)
old='''    if (client_failed) return -1;\n    if (!failed && current > end) return 0;\n    /* Automatic safety fallback: continue the same BGFT response from the\n     * first byte not yet delivered, using the proven single-range path. */\n    if (current <= end) return stream_upstream_range(client, job, job_id, current, end, sent_total);\n    return 0;\n}\n'''
new='''    if (client_failed) return -1;\n    if (!failed && current > end) return 0;\n    /* A provider may throttle parallel Range requests with HTTP 429. Let the\n     * CDN cool down, then finish the exact byte interval through the stable\n     * single-range path. This avoids turning a provider throttle into a local\n     * 502 error for BGFT. */\n    if (fail_status == 429) sleep(RATE_LIMIT_BACKOFF_BASE_SEC);\n    if (current <= end) return stream_upstream_range(client, job, job_id, current, end, sent_total);\n    return 0;\n}\n'''
assert old in t
t=t.replace(old,new,1)
old='''    upstream_request u;\n    int ret = upstream_open(&job, have_range ? range_value : NULL, &u);\n    if (ret < 0) {\n'''
new='''    upstream_request u;\n    int ret = upstream_open_with_backoff(&job, have_range ? range_value : NULL, &u);\n    if (ret < 0) {\n'''
assert old in t
t=t.replace(old,new,1)
old='''    uint32_t job_id = 0;\n    if (allocate_job(&parsed, &job_id)) {\n        respond_line(client, "TAYSON/1 ERR job-table-full\\n");\n        return;\n    }\n'''
new='''    uint32_t job_id = 0;\n    int existing_task = BGFT_INVALID_TASK_ID;\n    if (find_duplicate_active_job(&parsed, &job_id, &existing_task)) {\n        char existing[160];\n        snprintf(existing, sizeof(existing),\n                 "TAYSON/1 OK job=%u task=%d upstream=206 duplicate=1\\n",\n                 job_id, existing_task);\n        respond_line(client, existing);\n        return;\n    }\n    if (allocate_job(&parsed, &job_id)) {\n        respond_line(client, "TAYSON/1 ERR job-table-full\\n");\n        return;\n    }\n'''
assert old in t
t=t.replace(old,new,1)
p.write_text(t)
m=Path('ps4receiver/Makefile')
s=m.read_text().replace('VERSION     := 1.20','VERSION     := 1.21')
m.write_text(s)
