"""Apply deterministic corrections to the exact inherited Phoenixx ELF template."""
import hashlib
from pathlib import Path

root = Path(__file__).resolve().parents[2]
original = (root / 'UPDATE/base/original-template.elf').read_bytes()
assert hashlib.sha256(original).hexdigest() == '1e9746fc0a3b3de33801c00bcd92791a791c795593a0caf213d173d780bf24d2'

patched = bytearray(original)

# Existing read-only diagnostic string correction.
diag_offset = 0x10ff9
old_diag, new_diag = b'DESCONHECIDO\0', b'Desconhecido\0'
assert len(old_diag) == len(new_diag)
assert patched[diag_offset:diag_offset + len(old_diag)] == old_diag
patched[diag_offset:diag_offset + len(old_diag)] = new_diag

# The inherited native show_credits() routine builds the message byte-by-byte
# with `mov byte ptr [rbp-N], imm8`. Keep the exact instruction sequence and,
# importantly, keep the C-string terminator at the exact same final byte as the
# inherited payload. TaySon is padded with two spaces instead of introducing
# earlier NUL bytes. This is the most conservative credit-only patch possible:
# no opcode, displacement, call target, stack layout or function size changes.
text_file_offset = 0x4000
credit_vaddr = 0x438
old_credit = b'Dev. By Phoenixx\0'
new_credit = b'Dev. By TaySon  \0'
assert len(old_credit) == len(new_credit) == 17

for i, (old_byte, new_byte) in enumerate(zip(old_credit, new_credit)):
    instruction = text_file_offset + credit_vaddr + i * 4
    # Each instruction must remain: C6 45 <disp8> <imm8>.
    assert patched[instruction:instruction + 2] == b'\xC6\x45'
    assert patched[instruction + 2] == ((0xE0 + i) & 0xff)
    assert patched[instruction + 3] == old_byte
    patched[instruction + 3] = new_byte

# The inherited main() zeroed the BGFT parameter struct, but only populated
# userId, entitlementType, id, URL, name, packageType and size. Established
# PS4 BGFT implementations also provide a non-null iconPath, playgoScenarioId
# "0", packageSubType "" and BGFT_TASK_OPT_DISABLE_CDN_QUERY_PARAM (0x10000).
# A normal Game/DLC registration can dereference those string fields before it
# returns, which matches the hardware symptom: GoldHEN reports "Payload
# received" but the payload never reaches either success or error notification.
#
# Replace only the fixed main() struct-initialization window. The replacement
# is 134 bytes plus one NOP, exactly fitting vaddr 0x674..0x6FA; the existing
# compare/error/start control flow resumes untouched at 0x6FB.
bgft_vaddr = 0x674
bgft_offset = text_file_offset + bgft_vaddr
old_bgft = bytes.fromhex(
    '488dbd40ffffff31f6ba68000000e8c98900008b45c0898540ffffff'
    'c78544ffffff05000000488d055f39010048898548ffffff488d0591390100'
    '48898550ffffff488d058341010048898560ffffff488d05f541010048894590'
    '488d05fa410100488b00488945a0c7853cffffffffffffff488dbd40ffffff'
    '488db53cffffffff55e88945c4'
)
new_bgft = bytes.fromhex(
    '488d9540ffffff31c04889421848894230488942488b45c08902c6420405'
    '488d056739010048894208488d059c39010048894210488d059141010048894220'
    '488d05b6cf00004889424048ffc04889422848894258c7423800000100'
    '488d05e941010048894250488b05ee41010048894260c742fcffffffff'
    '4889d7488d72fcff55e88945c4'
)
assert len(old_bgft) == 135
assert len(new_bgft) == 134
assert patched[bgft_offset:bgft_offset + len(old_bgft)] == old_bgft
patched[bgft_offset:bgft_offset + len(new_bgft)] = new_bgft
patched[bgft_offset + len(new_bgft)] = 0x90

# Full native pre-BGFT stage instrumentation for the unresolved hardware
# failure. The previous Register/Start-only diagnostics still left all failures
# before Register ambiguous. This build instruments module loading and dlsym
# resolution too, without moving any existing ELF offsets.
#
# The old notify_error() body (vaddr 0x7b0..0x84d) is reused as four wrappers:
#
#   0x7b0 dlsym wrapper:
#       notify(symbol name) -> tail-jump sceKernelDlsym
#   0x7c6 module wrapper:
#       notify(module path) -> tail-jump sceKernelLoadStartModule
#   0x7ed register wrapper:
#       notify(register API) -> NULL guard -> tail-jump Register
#   0x81e start wrapper:
#       notify(start API) -> NULL guard -> tail-jump Start
#
# This yields a deterministic visible stage sequence on the PS4:
#   libSceBgft -> Init -> Register symbol -> Start symbol ->
#   libSceUserService -> UserServiceInitialize -> GetInitialUser ->
#   Register -> Start.
#
# If a native Register/Start call returns a negative error, the original
# notify_error() formatter is no longer available because its code space hosts
# the wrappers. Instead, its two call sites are redirected to a tiny prefix
# notifier at vaddr 0x430, producing "Registro" or "Erro Inicio". That is enough
# for this diagnostic build to distinguish "returned error" from
# "never returned / crashed".
#
# The normal success credit is suppressed only in this diagnostic build because
# vaddr 0x430 is temporarily used by the prefix notifier.

def _rel32(src_va, insn_len, dst_va):
    return int(dst_va - (src_va + insn_len)).to_bytes(4, "little", signed=True)

def _call(src_va, dst_va):
    return b"\xE8" + _rel32(src_va, 5, dst_va)

def _jmp(src_va, dst_va):
    return b"\xE9" + _rel32(src_va, 5, dst_va)

def _rip(src_va, insn_len, dst_va):
    return int(dst_va - (src_va + insn_len)).to_bytes(4, "little", signed=True)

def _jmp_got(src_va, got_va):
    return b"\xFF\x25" + _rip(src_va, 6, got_va)

dlsym_wrapper_va = 0x7b0
module_wrapper_va = 0x7c6
register_wrapper_va = 0x7ed
start_wrapper_va = 0x81e

# Generic sceKernelDlsym wrapper. Preserve its three arguments across notify()
# and tail-jump to the imported kernel function.
dlsym_wrapper = bytearray(b"\x57\x56\x52\xBF\x01\x00\x00\x00")
dlsym_wrapper += _call(dlsym_wrapper_va + len(dlsym_wrapper), 0x490)
dlsym_wrapper += b"\x5A\x5E\x5F"
dlsym_wrapper += _jmp_got(dlsym_wrapper_va + len(dlsym_wrapper), 0xcad8)
assert len(dlsym_wrapper) == 22

# Generic module-load wrapper. rdi already contains the module path.
module_wrapper = bytearray(
    b"\x53"                  # push rbx
    b"\x48\x89\xFB"      # mov rbx,rdi
    b"\x48\x89\xFE"      # mov rsi,rdi
    b"\xBF\x01\x00\x00\x00"
)
module_wrapper += _call(module_wrapper_va + len(module_wrapper), 0x490)
module_wrapper += bytes.fromhex(
    "4889df"      # mov rdi,rbx
    "31f6"        # xor esi,esi
    "31d2"        # xor edx,edx
    "31c9"        # xor ecx,ecx
    "4531c0"      # xor r8d,r8d
    "4531c9"      # xor r9d,r9d
    "5b"          # pop rbx
)
module_wrapper += _jmp_got(module_wrapper_va + len(module_wrapper), 0xcad0)
assert len(module_wrapper) == 39

# Register wrapper. The symbol string at vaddr 0xd200 is also the string that
# PayloadPatcher switches to sceBgftServiceDownloadRegisterTask for Game/DLC.
register_wrapper = bytearray(b"\x48\x8D\x35" + _rip(register_wrapper_va, 7, 0xd200))
register_wrapper += b"\x50\xBF\x01\x00\x00\x00"
register_wrapper += _call(register_wrapper_va + len(register_wrapper), 0x490)
register_wrapper += bytes.fromhex(
    "58"                  # pop rax
    "48837de800"          # cmp qword [rbp-0x18],0
    "7400"                # je <null>, patched below
    "488dbd40ffffff"      # lea rdi,[rbp-0xc0]
    "488db53cffffff"      # lea rsi,[rbp-0xc4]
    "ff65e8"              # jmp qword [rbp-0x18]
    "b806009980"          # mov eax,0x80990006
    "c3"                  # ret
)
je = register_wrapper.index(b"\x74\x00")
null = register_wrapper.index(bytes.fromhex("b806009980"))
register_wrapper[je + 1] = (null - (je + 2)) & 0xff
assert len(register_wrapper) == 49

# Start wrapper.
start_wrapper = bytearray(b"\x48\x8D\x35" + _rip(start_wrapper_va, 7, 0xce4f))
start_wrapper += b"\x50\xBF\x01\x00\x00\x00"
start_wrapper += _call(start_wrapper_va + len(start_wrapper), 0x490)
start_wrapper += bytes.fromhex(
    "58"
    "48837de000"
    "7400"
    "8bbd3cffffff"
    "ff65e0"
    "b806009980"
    "c3"
)
je = start_wrapper.index(b"\x74\x00")
null = start_wrapper.index(bytes.fromhex("b806009980"))
start_wrapper[je + 1] = (null - (je + 2)) & 0xff
assert len(start_wrapper) == 41

# Replace the complete notify_error() executable interval with the four wrappers.
wrapper_start = text_file_offset + 0x7b0
wrapper_size = 0x84e - 0x7b0
patched[wrapper_start:wrapper_start + wrapper_size] = b"\x90" * wrapper_size
for va, code in (
    (dlsym_wrapper_va, dlsym_wrapper),
    (module_wrapper_va, module_wrapper),
    (register_wrapper_va, register_wrapper),
    (start_wrapper_va, start_wrapper),
):
    patched[text_file_offset + va:text_file_offset + va + len(code)] = code

# Temporary error-prefix notifier at vaddr 0x430:
#   rdi = existing "Registro" / "Erro Inicio" pointer
#   -> notify(1, rdi)
prefix_wrapper_va = 0x430
prefix_wrapper = bytearray(bytes.fromhex("4889febf01000000"))
prefix_wrapper += _jmp(prefix_wrapper_va + len(prefix_wrapper), 0x490)
assert len(prefix_wrapper) == 13
prefix_offset = text_file_offset + prefix_wrapper_va
patched[prefix_offset:prefix_offset + 0x60] = prefix_wrapper + b"\x90" * (0x60 - len(prefix_wrapper))

# Redirect both module loads through the module-path wrapper.
for va, old in (
    (0x52e, bytes.fromhex("ff159cc50000")),
    (0x600, bytes.fromhex("ff15cac40000")),
):
    off = text_file_offset + va
    assert patched[off:off + 6] == old
    patched[off:off + 6] = _call(va, module_wrapper_va) + b"\x90"

# Redirect every dlsym in main through the symbol-name wrapper.
for va, old in (
    (0x573, bytes.fromhex("ff155fc50000")),
    (0x587, bytes.fromhex("ff154bc50000")),
    (0x59b, bytes.fromhex("ff1537c50000")),
    (0x631, bytes.fromhex("ff15a1c40000")),
    (0x645, bytes.fromhex("ff158dc40000")),
):
    off = text_file_offset + va
    assert patched[off:off + 6] == old
    patched[off:off + 6] = _call(va, dlsym_wrapper_va) + b"\x90"

# Route Register/Start through the guarded stage wrappers.
register_call = text_file_offset + 0x6ed
old_register_call = bytes.fromhex("4889d7488d72fcff55e8")
assert patched[register_call:register_call + len(old_register_call)] == old_register_call
patched[register_call:register_call + 10] = _call(0x6ed, register_wrapper_va) + b"\x90" * 5

start_call = text_file_offset + 0x71f
old_start_call = bytes.fromhex("488b45e08bbd3cffffffffd0")
assert patched[start_call:start_call + len(old_start_call)] == old_start_call
patched[start_call:start_call + 12] = _call(0x71f, start_wrapper_va) + b"\x90" * 7

# Preserve visible distinction between returned Register/Start errors and a
# function that never returns: show only the existing stage prefix.
for va, old in (
    (0x70f, bytes.fromhex("e89c000000")),
    (0x742, bytes.fromhex("e869000000")),
):
    off = text_file_offset + va
    assert patched[off:off + 5] == old
    patched[off:off + 5] = _call(va, prefix_wrapper_va)

# show_credits() has been repurposed by the prefix wrapper, so suppress only the
# former success-credit call for this diagnostic build.
credit_call = text_file_offset + 0x752
assert patched[credit_call:credit_call + 5] == bytes.fromhex("e8d9fcffff")
patched[credit_call:credit_call + 5] = b"\x90" * 5

patched = bytes(patched)
assert hashlib.sha256(patched).hexdigest() == '8b426f2d362858e4a98576a73d2628684e7adf119e432b2c9007d95a5b6de660'
(root / 'assets/template.elf').write_bytes(patched)
print('Template patched: full native BGFT module/dlsym/Register/Start stage diagnostics enabled.')
