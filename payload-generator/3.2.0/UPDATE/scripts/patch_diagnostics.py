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

# Temporary native stage instrumentation for the unresolved hardware BGFT
# failure. Reuse show_credits() (vaddr 0x430..0x48f) as two small wrappers:
#
#   register wrapper:
#     notify(symbol name) -> NULL guard -> call register
#   start wrapper:
#     notify(symbol name) -> NULL guard -> tail-call start
#
# The existing main() error handlers remain untouched. Therefore:
# - if the register symbol is NULL, main reports Registro: 0x80990006;
# - if Register returns an error, main reports Registro: 0x...;
# - if Register returns successfully, the Start symbol notification appears;
# - if Start is NULL/returns an error, main reports Erro Inicio: 0x....
#
# The normal credit notification is intentionally disabled in this diagnostic
# build because its 96-byte function body is the safest fixed-size executable
# area available without shifting any ELF offsets.
stage_wrapper = bytes.fromhex(
    '53488d35c8cd00006a015fe85000000048837de8007413'
    '488dbd40ffffff488db53cffffffff55e85bc3b8060099805bc3'
    '53488d35e6c900006a015fe81f00000048837de000740a'
    '8bbd3cffffff5bff65e0b8060099805bc3'
)
assert len(stage_wrapper) == 89

stage_offset = text_file_offset + 0x430
assert patched[stage_offset:stage_offset + 4] == b'\x55\x48\x89\xe5'
patched[stage_offset:stage_offset + 96] = stage_wrapper + b'\x90' * (96 - len(stage_wrapper))

# Replace the native Register call setup with a fixed direct call to wrapper
# vaddr 0x430. The wrapper reconstructs params/taskId from main()'s frame.
register_call = text_file_offset + 0x6ed
old_register_call = bytes.fromhex('4889d7488d72fcff55e8')
assert patched[register_call:register_call + len(old_register_call)] == old_register_call
patched[register_call:register_call + 10] = bytes.fromhex('e83efdffff') + b'\x90' * 5

# Replace Start setup/call with wrapper vaddr 0x461.
start_call = text_file_offset + 0x71f
old_start_call = bytes.fromhex('488b45e08bbd3cffffffffd0')
assert patched[start_call:start_call + len(old_start_call)] == old_start_call
patched[start_call:start_call + 12] = bytes.fromhex('e83dfdffff') + b'\x90' * 7

# show_credits() has been repurposed, so suppress its former success call.
credit_call = text_file_offset + 0x752
assert patched[credit_call:credit_call + 5] == bytes.fromhex('e8d9fcffff')
patched[credit_call:credit_call + 5] = b'\x90' * 5

patched = bytes(patched)
assert hashlib.sha256(patched).hexdigest() == 'e9e912b50868f7229fd2b1fe3e9c0025c1459c4c4cc64c46bc3932c1d4f6cd8e'
(root / 'assets/template.elf').write_bytes(patched)
print('Template patched: BGFT parameters completed and native Register/Start stage diagnostics enabled.')
