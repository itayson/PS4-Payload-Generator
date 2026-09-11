from pathlib import Path
p=Path('ps4receiver/receiver/main.c')
t=p.read_text()
t=t.replace('#define RECEIVER_VERSION "1.1.1"', '#define RECEIVER_VERSION "1.2.0"')
anchor='#define RECEIVER_SOCKET_RCVBUF (512 * 1024)\n'
insert='''#define RECEIVER_SOCKET_RCVBUF (512 * 1024)\n\n/* Adaptive multi-range accelerator. Large BGFT reads are prefetched from the\n * upstream CDN through several independent Range requests and delivered to\n * BGFT in strict byte order. A global gate caps concurrent upstream fetchers\n * so RAM/SSL usage stays bounded even when BGFT opens more than one request. */\n#define SEGMENT_SIZE (8 * 1024 * 1024)\n#define SEGMENT_WINDOW 3\n#define SEGMENT_MIN_BODY (16 * 1024 * 1024)\n#define MAX_SEGMENT_FETCHERS 4\n'''
assert anchor in t
t=t.replace(anchor,insert,1)
anchor='static int g_privileged_credentials_ready = 0;\n'
insert='''static int g_privileged_credentials_ready = 0;\nstatic pthread_mutex_t g_segment_gate_mutex = PTHREAD_MUTEX_INITIALIZER;\nstatic pthread_cond_t g_segment_gate_cond = PTHREAD_COND_INITIALIZER;\nstatic int g_segment_fetchers = 0;\n'''
assert anchor in t
t=t.replace(anchor,insert,1)
marker='static int handle_proxy_client(int client) {'
idx=t.index(marker)
helpers=r'''typedef struct {
    const receiver_job *job;
    uint64_t start;
    uint64_t end;
    uint8_t *data;
    size_t length;
    int result;
    int status;
} segment_task;

static void segment_gate_acquire(void) {
    pthread_mutex_lock(&g_segment_gate_mutex);
    while (g_segment_fetchers >= MAX_SEGMENT_FETCHERS) {
        pthread_cond_wait(&g_segment_gate_cond, &g_segment_gate_mutex);
    }
    g_segment_fetchers++;
    pthread_mutex_unlock(&g_segment_gate_mutex);
}

static void segment_gate_release(void) {
    pthread_mutex_lock(&g_segment_gate_mutex);
    if (g_segment_fetchers > 0) g_segment_fetchers--;
    pthread_cond_signal(&g_segment_gate_cond);
    pthread_mutex_unlock(&g_segment_gate_mutex);
}

static void *segment_fetch_thread(void *arg) {
    segment_task *task = (segment_task *)arg;
    task->result = -1;
    task->status = 0;
    task->length = 0;
    task->data = NULL;

    segment_gate_acquire();
    size_t expected = (size_t)(task->end - task->start + 1);
    task->data = (uint8_t *)malloc(expected);
    if (!task->data) {
        task->result = -12;
        segment_gate_release();
        return NULL;
    }

    char range[96];
    snprintf(range, sizeof(range), "bytes=%llu-%llu",
             (unsigned long long)task->start,
             (unsigned long long)task->end);

    upstream_request u;
    int ret = upstream_open(task->job, range, &u);
    if (ret < 0) {
        task->result = ret;
        free(task->data);
        task->data = NULL;
        segment_gate_release();
        return NULL;
    }
    task->status = u.status;
    if (u.status != 206 || u.length_type != ORBIS_HTTP_CONTENTLEN_EXIST ||
        (size_t)u.content_length != expected) {
        task->result = -502;
        upstream_close(&u);
        free(task->data);
        task->data = NULL;
        segment_gate_release();
        return NULL;
    }

    size_t got = 0;
    while (got < expected) {
        uint32_t want = (uint32_t)(expected - got);
        if (want > IO_BUFFER_SIZE) want = IO_BUFFER_SIZE;
        ret = sceHttpReadData(u.req, task->data + got, want);
        if (ret < 0) {
            task->result = ret;
            break;
        }
        if (ret == 0) break;
        got += (size_t)ret;
    }
    upstream_close(&u);
    task->length = got;
    task->result = (got == expected) ? 0 : (task->result < 0 ? task->result : -503);
    if (task->result != 0) {
        free(task->data);
        task->data = NULL;
    }
    segment_gate_release();
    return NULL;
}

static int launch_segment(segment_task *task, pthread_t *thread, const receiver_job *job,
                          uint64_t start, uint64_t end) {
    memset(task, 0, sizeof(*task));
    task->job = job;
    task->start = start;
    task->end = end;
    task->result = -1;

    pthread_attr_t attr;
    int ret = pthread_attr_init(&attr);
    if (ret != 0) return ret;
    ret = pthread_attr_setstacksize(&attr, PROXY_WORKER_STACK_SIZE);
    if (ret == 0) ret = pthread_create(thread, &attr, segment_fetch_thread, task);
    pthread_attr_destroy(&attr);
    return ret;
}

static int stream_upstream_range(int client, const receiver_job *job, uint32_t job_id,
                                 uint64_t start, uint64_t end, uint64_t *sent_total) {
    if (start > end) return 0;
    char range[96];
    snprintf(range, sizeof(range), "bytes=%llu-%llu",
             (unsigned long long)start, (unsigned long long)end);
    upstream_request u;
    int ret = upstream_open(job, range, &u);
    if (ret < 0) return ret;
    if (u.status != 206 || u.length_type != ORBIS_HTTP_CONTENTLEN_EXIST) {
        upstream_close(&u);
        return -502;
    }
    uint64_t expected = end - start + 1;
    if ((uint64_t)u.content_length != expected) {
        upstream_close(&u);
        return -502;
    }
    uint8_t *buf = (uint8_t *)malloc(IO_BUFFER_SIZE);
    if (!buf) { upstream_close(&u); return -12; }
    uint64_t done = 0;
    while (done < expected) {
        uint32_t want = IO_BUFFER_SIZE;
        uint64_t remain = expected - done;
        if (remain < want) want = (uint32_t)remain;
        ret = sceHttpReadData(u.req, buf, want);
        if (ret < 0) break;
        if (ret == 0) { ret = -503; break; }
        if (send_all(client, buf, (size_t)ret)) { ret = -1; break; }
        done += (uint64_t)ret;
        *sent_total += (uint64_t)ret;
        job_transfer_progress(job_id, start + done, (uint64_t)ret);
        ret = 0;
    }
    free(buf);
    upstream_close(&u);
    return (done == expected) ? 0 : (ret ? ret : -503);
}

static int stream_segmented(int client, const receiver_job *job, uint32_t job_id,
                            uint64_t start, uint64_t end, uint64_t *sent_total) {
    segment_task tasks[SEGMENT_WINDOW];
    memset(tasks, 0, sizeof(tasks));
    pthread_t threads[SEGMENT_WINDOW];
    int active[SEGMENT_WINDOW] = {0};
    uint64_t next = start;

    for (int i = 0; i < SEGMENT_WINDOW && next <= end; i++) {
        uint64_t seg_end = next + SEGMENT_SIZE - 1;
        if (seg_end > end) seg_end = end;
        if (launch_segment(&tasks[i], &threads[i], job, next, seg_end) != 0) break;
        active[i] = 1;
        next = seg_end + 1;
    }

    int failed = 0;
    int client_failed = 0;
    uint64_t current = start;
    for (int turn = 0; current <= end; turn++) {
        int idx = turn % SEGMENT_WINDOW;
        if (!active[idx]) { failed = 1; break; }
        pthread_join(threads[idx], NULL);
        active[idx] = 0;
        if (tasks[idx].result != 0 || !tasks[idx].data) {
            failed = 1;
            break;
        }
        if (tasks[idx].start != current) {
            free(tasks[idx].data);
            tasks[idx].data = NULL;
            failed = 1;
            break;
        }
        if (send_all(client, tasks[idx].data, tasks[idx].length)) {
            free(tasks[idx].data);
            tasks[idx].data = NULL;
            failed = 1;
            client_failed = 1;
            break;
        }
        current = tasks[idx].end + 1;
        *sent_total += (uint64_t)tasks[idx].length;
        job_transfer_progress(job_id, current, (uint64_t)tasks[idx].length);
        free(tasks[idx].data);
        tasks[idx].data = NULL;

        if (next <= end) {
            uint64_t seg_end = next + SEGMENT_SIZE - 1;
            if (seg_end > end) seg_end = end;
            if (launch_segment(&tasks[idx], &threads[idx], job, next, seg_end) == 0) {
                active[idx] = 1;
                next = seg_end + 1;
            } else {
                failed = 1;
                break;
            }
        }
    }

    for (int i = 0; i < SEGMENT_WINDOW; i++) {
        if (active[i]) {
            pthread_join(threads[i], NULL);
            active[i] = 0;
        }
        if (tasks[i].data) {
            free(tasks[i].data);
            tasks[i].data = NULL;
        }
    }

    if (client_failed) return -1;
    if (!failed && current > end) return 0;
    /* Automatic safety fallback: continue the same BGFT response from the
     * first byte not yet delivered, using the proven single-range path. */
    if (current <= end) return stream_upstream_range(client, job, job_id, current, end, sent_total);
    return 0;
}

'''
t=t[:idx]+helpers+t[idx:]
old=r'''    uint8_t *io_buf = (uint8_t *)malloc(IO_BUFFER_SIZE);
    if (!io_buf) {
        job_transfer_end(job_id, -1, u.status);
        upstream_close(&u);
        return -1;
    }
    uint64_t sent = 0;
    while (sent < body_len) {
        uint32_t want = IO_BUFFER_SIZE;
        uint64_t remain = body_len - sent;
        if (remain < want) want = (uint32_t)remain;
        ret = sceHttpReadData(u.req, io_buf, want);
        if (ret < 0) {
            notify_code("Tayson Receiver read", ret);
            job_transfer_end(job_id, ret, u.status);
            free(io_buf);
            upstream_close(&u);
            return -1;
        }
        if (ret == 0) break;
        if (send_all(client, io_buf, (size_t)ret)) {
            job_transfer_end(job_id, -1, u.status);
            free(io_buf);
            upstream_close(&u);
            return -1;
        }
        sent += (uint64_t)ret;
        job_transfer_progress(job_id, range_start + sent, (uint64_t)ret);
    }
    free(io_buf);
    upstream_close(&u);
    int complete = sent == body_len;
    job_transfer_end(job_id, complete ? 0 : -1, u.status);
    return complete ? 0 : -1;
'''
new=r'''    uint64_t sent = 0;
    uint64_t absolute_end = body_len ? range_start + body_len - 1 : range_start;
    int use_segmented = body_len >= SEGMENT_MIN_BODY && job.upstream_status == 206;
    if (use_segmented) {
        /* The first request was used only to validate the exact BGFT response
         * length/status. Multi-range workers now fetch that same byte interval
         * concurrently and feed it back to BGFT in strict order. */
        upstream_close(&u);
        ret = stream_segmented(client, &job, job_id, range_start, absolute_end, &sent);
        if (ret < 0) {
            notify_code("Tayson Receiver segmented", ret);
            job_transfer_end(job_id, ret, 206);
            return -1;
        }
    } else {
        uint8_t *io_buf = (uint8_t *)malloc(IO_BUFFER_SIZE);
        if (!io_buf) {
            job_transfer_end(job_id, -1, u.status);
            upstream_close(&u);
            return -1;
        }
        while (sent < body_len) {
            uint32_t want = IO_BUFFER_SIZE;
            uint64_t remain = body_len - sent;
            if (remain < want) want = (uint32_t)remain;
            ret = sceHttpReadData(u.req, io_buf, want);
            if (ret < 0) {
                notify_code("Tayson Receiver read", ret);
                job_transfer_end(job_id, ret, u.status);
                free(io_buf);
                upstream_close(&u);
                return -1;
            }
            if (ret == 0) break;
            if (send_all(client, io_buf, (size_t)ret)) {
                job_transfer_end(job_id, -1, u.status);
                free(io_buf);
                upstream_close(&u);
                return -1;
            }
            sent += (uint64_t)ret;
            job_transfer_progress(job_id, range_start + sent, (uint64_t)ret);
        }
        free(io_buf);
        upstream_close(&u);
    }
    int complete = sent == body_len;
    job_transfer_end(job_id, complete ? 0 : -1, 206);
    return complete ? 0 : -1;
'''
assert old in t
t=t.replace(old,new,1)
p.write_text(t)

m=Path('ps4receiver/Makefile')
s=m.read_text().replace('VERSION     := 1.11','VERSION     := 1.20')
m.write_text(s)
