from pathlib import Path

p = Path('ps4receiver/receiver/main.c')
t = p.read_text()
t = t.replace('#define RECEIVER_VERSION "1.0.6"', '#define RECEIVER_VERSION "1.0.7"')

marker = 'static void upstream_close(upstream_request *u) {\n'
cb = '''static int32_t receiver_https_accept_callback(int32_t libssl_ctx_id, uint32_t verify_err,
                                             void * const ssl_cert[], int32_t cert_num,
                                             void *user_arg) {
    (void)libssl_ctx_id;
    (void)verify_err;
    (void)ssl_cert;
    (void)cert_num;
    (void)user_arg;
    /* SceHttps callback contract used by PS4 homebrew: non-zero accepts the
     * certificate verification result. This is scoped per Receiver template. */
    return 1;
}

'''
if marker not in t:
    raise SystemExit('upstream_close marker not found')
t = t.replace(marker, cb + marker, 1)

old = '''    if (strncasecmp(job->upstream_url, "https://", 8) == 0) {
        (void)sceHttpsDisableOption(u->tpl, RECEIVER_HTTPS_VERIFY_FLAGS);
    }

    sceHttpSetAutoRedirect(u->tpl, 1);
'''
new = '''    int is_https = (strncasecmp(job->upstream_url, "https://", 8) == 0);
    if (is_https) {
        /* Some newer CDN chains still trigger SCE_SSL_ERROR_UNKNOWN_CA even
         * when the verification flags are disabled. Install a template-scoped
         * verification callback as used by mature PS4 homebrew HTTP clients. */
        ret = sceHttpsSetSslCallback(u->tpl, receiver_https_accept_callback, NULL);
        if (ret < 0) { upstream_close(u); return ret; }
        (void)sceHttpsDisableOption(u->tpl, RECEIVER_HTTPS_VERIFY_FLAGS);
    }

    sceHttpSetAutoRedirect(u->tpl, 1);
'''
if old not in t:
    raise SystemExit('1.0.6 https block not found')
t = t.replace(old, new, 1)

old2 = '''    ret = sceHttpCreateRequestWithURL(u->conn, ORBIS_METHOD_GET, job->upstream_url, 0);
    if (ret < 0) { upstream_close(u); return ret; }
    u->req = ret;

    sceHttpAddRequestHeader(u->req, "Accept", "*/*", 0);
'''
new2 = '''    ret = sceHttpCreateRequestWithURL(u->conn, ORBIS_METHOD_GET, job->upstream_url, 0);
    if (ret < 0) { upstream_close(u); return ret; }
    u->req = ret;

    /* Mirror the reference RPI ordering as well: re-apply verification flags
     * after request creation so both template and active request path inherit
     * the relaxed policy on older/newer firmware implementations. */
    if (is_https) {
        (void)sceHttpsDisableOption(u->tpl, RECEIVER_HTTPS_VERIFY_FLAGS);
    }

    sceHttpAddRequestHeader(u->req, "Accept", "*/*", 0);
'''
if old2 not in t:
    raise SystemExit('request creation block not found')
t = t.replace(old2, new2, 1)

t = t.replace('notify_text("Tayson PS4 Receiver 1.0.6 ativo - porta 9095");',
              'notify_text("Tayson PS4 Receiver 1.0.7 ativo - porta 9095");')
p.write_text(t)

m = Path('ps4receiver/Makefile')
s = m.read_text().replace('VERSION     := 1.06', 'VERSION     := 1.07')
m.write_text(s)

final = p.read_text()
assert 'RECEIVER_VERSION "1.0.7"' in final
assert 'receiver_https_accept_callback' in final
assert 'sceHttpsSetSslCallback(u->tpl, receiver_https_accept_callback, NULL)' in final
assert final.count('sceHttpsDisableOption(u->tpl, RECEIVER_HTTPS_VERIFY_FLAGS)') >= 2
assert 'VERSION     := 1.07' in m.read_text()
