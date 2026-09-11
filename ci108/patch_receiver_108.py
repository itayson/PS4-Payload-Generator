from pathlib import Path

p = Path('ps4receiver/receiver/main.c')
t = p.read_text()

t = t.replace('#define RECEIVER_VERSION "1.0.7"', '#define RECEIVER_VERSION "1.0.8"')

marker = '#define IO_BUFFER_SIZE (128 * 1024)\n'
insert = '''#define IO_BUFFER_SIZE (128 * 1024)
/* SceHttp can reject requests from threads with too little remaining stack
 * (0x80431076 / SCE_HTTP_ERROR_INSUFFICIENT_STACKSIZE). The control-path
 * preflight runs on the main thread, but BGFT fetches are served by these
 * proxy workers, so reserve a dedicated stack only for those workers. */
#define PROXY_WORKER_STACK_SIZE (1024 * 1024)
'''
if marker not in t:
    raise SystemExit('IO buffer marker not found')
t = t.replace(marker, insert, 1)

old = '''        pthread_t worker;
        if (pthread_create(&worker, NULL, proxy_client_thread, fd_arg) != 0) {
            free(fd_arg);
            sceNetSocketClose(client);
            continue;
        }
        pthread_detach(worker);
'''
new = '''        pthread_t worker;
        pthread_attr_t worker_attr;
        int attr_ok = pthread_attr_init(&worker_attr);
        if (attr_ok == 0) attr_ok = pthread_attr_setstacksize(&worker_attr, PROXY_WORKER_STACK_SIZE);
        if (attr_ok != 0 || pthread_create(&worker, &worker_attr, proxy_client_thread, fd_arg) != 0) {
            if (attr_ok == 0) pthread_attr_destroy(&worker_attr);
            free(fd_arg);
            sceNetSocketClose(client);
            notify_text("Tayson Receiver: falha ao criar worker HTTP");
            continue;
        }
        pthread_attr_destroy(&worker_attr);
        pthread_detach(worker);
'''
if old not in t:
    raise SystemExit('proxy worker creation block not found')
t = t.replace(old, new, 1)

t = t.replace('notify_text("Tayson PS4 Receiver 1.0.7 ativo - porta 9095");',
              'notify_text("Tayson PS4 Receiver 1.0.8 ativo - porta 9095");')

p.write_text(t)

m = Path('ps4receiver/Makefile')
s = m.read_text().replace('VERSION     := 1.07', 'VERSION     := 1.08')
m.write_text(s)

final = p.read_text()
make = m.read_text()
assert 'RECEIVER_VERSION "1.0.8"' in final
assert 'PROXY_WORKER_STACK_SIZE (1024 * 1024)' in final
assert 'pthread_attr_setstacksize(&worker_attr, PROXY_WORKER_STACK_SIZE)' in final
assert 'pthread_create(&worker, &worker_attr, proxy_client_thread, fd_arg)' in final
assert 'pthread_attr_destroy(&worker_attr)' in final
assert 'receiver_https_accept_callback' in final
assert 'VERSION     := 1.08' in make
