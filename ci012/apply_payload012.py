from pathlib import Path

main = Path('payload-installer/main.c')
makefile = Path('payload-installer/Makefile')

s = main.read_text()
s = s.replace('TAYSON_PAYLOAD_VERSION "0.1.1"', 'TAYSON_PAYLOAD_VERSION "0.1.2"')
s = s.replace('Tayson Payload Installer 0.1.1', 'Tayson Payload Installer 0.1.2')

# If the cache is already complete, skip HTTP and go directly to local install.
old = '''    if (stat(CACHE_FILE, &st) == 0 && st.st_size > 0) {
        resume = (uint64_t)st.st_size;
        if (expected_size && resume > expected_size) {
            unlink(CACHE_FILE);
            resume = 0;
        }
    }
'''
new = '''    if (stat(CACHE_FILE, &st) == 0 && st.st_size > 0) {
        resume = (uint64_t)st.st_size;
        if (expected_size && resume > expected_size) {
            unlink(CACHE_FILE);
            resume = 0;
        } else if (expected_size && resume == expected_size) {
            pthread_mutex_lock(&g_job_lock);
            g_job.downloaded = resume;
            pthread_mutex_unlock(&g_job_lock);
            return 0;
        }
    }
'''
if old not in s:
    raise SystemExit('cache resume anchor not found')
s = s.replace(old, new, 1)

# START remains a clean job (Android calls RESET first). RESUME reuses current.pkg.
old = '''    if (strncmp(req, "TAYSONP/1 START", 15) == 0) {
        char url[URL_CAP] = {0}, sizebuf[32] = {0}, name[NAME_CAP] = {0}, title[TITLE_CAP] = {0};
'''
new = '''    bool resume_command = strncmp(req, "TAYSONP/1 RESUME", 16) == 0;
    if (resume_command || strncmp(req, "TAYSONP/1 START", 15) == 0) {
        char url[URL_CAP] = {0}, sizebuf[32] = {0}, name[NAME_CAP] = {0}, title[TITLE_CAP] = {0};
'''
if old not in s:
    raise SystemExit('START command anchor not found')
s = s.replace(old, new, 1)

old = '''        if (strncmp(url, "http://", 7) != 0) { respond_text(fd, "TAYSONP/1 ERR relay-http-required\\n"); return; }

        pthread_mutex_lock(&g_job_lock);
'''
new = '''        if (strncmp(url, "http://", 7) != 0) { respond_text(fd, "TAYSONP/1 ERR relay-http-required\\n"); return; }
        uint64_t requested_total = sizebuf[0] ? strtoull(sizebuf, NULL, 10) : 0;
        if (resume_command) {
            struct stat cached;
            if (stat(CACHE_FILE, &cached) != 0 || cached.st_size <= 0) {
                respond_text(fd, "TAYSONP/1 ERR no-cache\\n");
                return;
            }
            if (requested_total && (uint64_t)cached.st_size > requested_total) {
                respond_text(fd, "TAYSONP/1 ERR cache-too-large\\n");
                return;
            }
        }

        pthread_mutex_lock(&g_job_lock);
'''
if old not in s:
    raise SystemExit('URL validation anchor not found')
s = s.replace(old, new, 1)

s = s.replace('        g_job.total = sizebuf[0] ? strtoull(sizebuf, NULL, 10) : 0;\n',
              '        g_job.total = requested_total;\n', 1)

old = '        respond_text(fd, "TAYSONP/1 ACCEPTED job=1 path=" CACHE_FILE "\\n");\n'
new = '''        if (resume_command)
            respond_text(fd, "TAYSONP/1 ACCEPTED job=1 resumed=1 path=" CACHE_FILE "\\n");
        else
            respond_text(fd, "TAYSONP/1 ACCEPTED job=1 path=" CACHE_FILE "\\n");
'''
if old not in s:
    raise SystemExit('accepted response anchor not found')
s = s.replace(old, new, 1)

main.write_text(s)

m = makefile.read_text()
m = m.replace('TARGET := Tayson-Payload-Installer-0.1.1', 'TARGET := Tayson-Payload-Installer-0.1.2')
makefile.write_text(m)

assert 'TAYSONP/1 RESUME' in main.read_text()
assert 'resumed=1' in main.read_text()
assert 'resume == expected_size' in main.read_text()
assert 'Tayson-Payload-Installer-0.1.2' in makefile.read_text()
