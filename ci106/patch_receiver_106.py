from pathlib import Path

p = Path('ps4receiver/receiver/main.c')
t = p.read_text()

t = t.replace('#define RECEIVER_VERSION "1.0.5"', '#define RECEIVER_VERSION "1.0.6"')

marker = '#define ORBIS_KERNEL_PRIO_FIFO_NORMAL 0x2BC\n'
insert = '''#define ORBIS_KERNEL_PRIO_FIFO_NORMAL 0x2BC
/* PS4 SceHttps verification flags. 0x3F matches the six checks disabled by
 * the reference Remote Package Installer before talking to external CDNs:
 * server/client verify, CN, validity dates and known-CA validation. */
#define RECEIVER_HTTPS_VERIFY_FLAGS 0x3FU
'''
if marker not in t:
    raise SystemExit('constant marker not found')
t = t.replace(marker, insert, 1)

old = '''    u->tpl = ret;
    sceHttpSetAutoRedirect(u->tpl, 1);
'''
new = '''    u->tpl = ret;

    /* Direct-download CDNs increasingly present certificate chains that the
     * PS4 trust store does not know. VikingFile/Cloudflare currently reaches
     * SceSsl and fails with 0x8095F00C (UNKNOWN_CA). Match the proven PS4
     * Remote Package Installer approach and disable certificate validation
     * for the Receiver's upstream fetch only. The signed/opaque provider URL
     * is still used unchanged and HTTP status/range behavior is validated. */
    if (strncasecmp(job->upstream_url, "https://", 8) == 0) {
        (void)sceHttpsDisableOption(u->tpl, RECEIVER_HTTPS_VERIFY_FLAGS);
    }

    sceHttpSetAutoRedirect(u->tpl, 1);
'''
if old not in t:
    raise SystemExit('upstream template marker not found')
t = t.replace(old, new, 1)

t = t.replace('notify_text("Tayson PS4 Receiver 1.0.5 ativo - porta 9095");',
              'notify_text("Tayson PS4 Receiver 1.0.6 ativo - porta 9095");')

p.write_text(t)

m = Path('ps4receiver/Makefile')
s = m.read_text()
s = s.replace('VERSION     := 1.05', 'VERSION     := 1.06')
m.write_text(s)

final = p.read_text()
make = m.read_text()
assert 'RECEIVER_VERSION "1.0.6"' in final
assert 'RECEIVER_HTTPS_VERIFY_FLAGS 0x3FU' in final
assert 'sceHttpsDisableOption(u->tpl, RECEIVER_HTTPS_VERIFY_FLAGS)' in final
assert 'strncasecmp(job->upstream_url, "https://", 8) == 0' in final
assert 'VERSION     := 1.06' in make
assert 'elevate_receiver_credentials' in final
assert 'jbc-get-cred' in final
