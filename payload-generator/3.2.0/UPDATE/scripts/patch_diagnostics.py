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

patched = bytes(patched)
assert hashlib.sha256(patched).hexdigest() == '2d6c6aff8159588eb8b5516f514be9a6d18337b6b58686a200987b6e029d6854'
(root / 'assets/template.elf').write_bytes(patched)
print('Template patched: diagnostic/credit corrected and BGFT parameters completed for hardware registration.')
