from pathlib import Path

p = Path('ps4receiver/receiver/main.c')
t = p.read_text()
t = t.replace('#define RECEIVER_VERSION "1.0.4"', '#define RECEIVER_VERSION "1.0.5"')

insert_after = 'typedef void (*receiver_notify_fn)(int type, const char *text);\n'
jbc = '''
/* libjbc credential bridge. The implementation is linked as a separate PS4
 * object and is only invoked lazily when an install job arrives, preserving
 * the safe-start behavior introduced in 1.0.3. */
struct receiver_jbc_cred {
    uint32_t uid;
    uint32_t ruid;
    uint32_t svuid;
    uint32_t rgid;
    uint32_t svgid;
    uintptr_t prison;
    uintptr_t cdir;
    uintptr_t rdir;
    uintptr_t jdir;
    uint64_t sceProcType;
    uint64_t sonyCred;
    uint64_t sceProcCap;
};

extern int jbc_get_cred(struct receiver_jbc_cred *cred);
extern int jbc_jailbreak_cred(struct receiver_jbc_cred *cred);
extern int jbc_set_cred(const struct receiver_jbc_cred *cred);
'''
if insert_after not in t:
    raise SystemExit('receiver notify typedef marker not found')
t = t.replace(insert_after, insert_after + jbc, 1)

old = 'static int32_t g_mod_bgft = -1;\n'
new = 'static int32_t g_mod_bgft = -1;\nstatic int g_privileged_credentials_ready = 0;\n'
if old not in t:
    raise SystemExit('module globals marker not found')
t = t.replace(old, new, 1)

marker = 'static int load_install_symbols(void) {\n'
elev = '''static int elevate_receiver_credentials(void) {
    if (g_privileged_credentials_ready) return 0;

    struct receiver_jbc_cred original;
    memset(&original, 0, sizeof(original));
    set_service_stage("jbc-get-cred");
    int ret = jbc_get_cred(&original);
    if (ret < 0) return ret;

    struct receiver_jbc_cred elevated = original;
    set_service_stage("jbc-jailbreak-cred");
    ret = jbc_jailbreak_cred(&elevated);
    if (ret < 0) return ret;

    /* Match the known-working AirPSX PS4 path: preserve the original root
     * directory as cwd while applying the elevated prison and capabilities. */
    elevated.cdir = original.rdir;
    set_service_stage("jbc-set-cred");
    ret = jbc_set_cred(&elevated);
    if (ret < 0) return ret;

    g_privileged_credentials_ready = 1;
    set_service_stage("jbc-ready");
    return 0;
}

'''
if marker not in t:
    raise SystemExit('load_install_symbols marker not found')
t = t.replace(marker, elev + marker, 1)

old = '''    set_service_stage("http-stack");
    int ret = load_http_stack();
    if (!ret) ret = load_install_symbols();
'''
new = '''    set_service_stage("http-stack");
    int ret = load_http_stack();
    if (!ret) ret = elevate_receiver_credentials();
    if (!ret) ret = load_install_symbols();
'''
if old not in t:
    raise SystemExit('ensure_transfer_services block not found')
t = t.replace(old, new, 1)
t = t.replace('notify_text("Tayson PS4 Receiver 1.0.4 ativo - porta 9095");',
              'notify_text("Tayson PS4 Receiver 1.0.5 ativo - porta 9095");')
p.write_text(t)

m = Path('ps4receiver/Makefile')
s = m.read_text()
s = s.replace('VERSION     := 1.04', 'VERSION     := 1.05')
s = s.replace('OBJS        := $(patsubst $(PROJDIR)/%.c,$(INTDIR)/%.o,$(CFILES))',
              'OBJS        := $(patsubst $(PROJDIR)/%.c,$(INTDIR)/%.o,$(CFILES))\nJBC_OBJ     := third_party/jbc.o')
s = s.replace('$(INTDIR)/receiver.elf: $(OBJS)\n\t$(LD) $(OBJS) -o $@ $(LDFLAGS)',
              '$(INTDIR)/receiver.elf: $(OBJS) $(JBC_OBJ)\n\t$(LD) $(OBJS) $(JBC_OBJ) -o $@ $(LDFLAGS)')
m.write_text(s)

final = p.read_text()
make = m.read_text()
assert 'RECEIVER_VERSION "1.0.5"' in final
assert 'elevate_receiver_credentials' in final
assert 'jbc-get-cred' in final
assert 'jbc-jailbreak-cred' in final
assert 'jbc-set-cred' in final
assert 'VERSION     := 1.05' in make
assert 'JBC_OBJ     := third_party/jbc.o' in make
assert '$(OBJS) $(JBC_OBJ) -o $@' in make
