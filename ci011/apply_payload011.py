from pathlib import Path

main = Path('payload-installer/main.c')
makefile = Path('payload-installer/Makefile')

s = main.read_text()
s = s.replace('#define TAYSON_PAYLOAD_VERSION "0.1.0"', '#define TAYSON_PAYLOAD_VERSION "0.1.1"')
s = s.replace('Tayson Payload Installer 0.1.0', 'Tayson Payload Installer 0.1.1')

anchor = '''static void *job_worker(void *unused) {
'''
helper = '''static bool retryable_download_error(int r) {
    if (r == -7002 || r == -222 || r == -220) return true;
    if (r <= -1000 && r > -2000) return true;   /* connect/socket */
    if (r <= -5000 && r > -6000) return true;   /* recv/network */
    if (r <= -3429 && r >= -3599) return true;  /* HTTP 429 / 5xx from relay */
    return false;
}

'''
if helper not in s:
    if anchor not in s:
        raise SystemExit('job_worker anchor not found')
    s = s.replace(anchor, helper + anchor, 1)

old = '    int r = download_from_relay(url, total);\n'
new = '''    int r = 0;
    int retry = 0;
    const int max_retries = 24;
    for (;;) {
        r = download_from_relay(url, total);
        if (r == 0 || r == -7000) break;
        if (!retryable_download_error(r) || retry >= max_retries) break;
        retry++;
        char msg[192];
        snprintf(msg, sizeof(msg),
                 "Tayson Payload: conexão interrompida; retomando download (%d/%d)",
                 retry, max_retries);
        notify_user(msg);
        sleep(retry <= 4 ? 2 : 5);
    }
'''
if old not in s:
    raise SystemExit('download call anchor not found')
s = s.replace(old, new, 1)

# Keep the job as downloading across automatic resume attempts.
# -7002 is our own 'final cache size != expected size' sentinel; it is now recoverable.
main.write_text(s)

m = makefile.read_text()
m = m.replace('TARGET := Tayson-Payload-Installer-0.1.0', 'TARGET := Tayson-Payload-Installer-0.1.1')
makefile.write_text(m)

assert 'TAYSON_PAYLOAD_VERSION "0.1.1"' in main.read_text()
assert 'retryable_download_error' in main.read_text()
assert 'max_retries = 24' in main.read_text()
assert 'Tayson-Payload-Installer-0.1.1' in makefile.read_text()
