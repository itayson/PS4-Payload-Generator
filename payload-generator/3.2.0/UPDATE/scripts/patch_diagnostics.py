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

patched = bytes(patched)
assert hashlib.sha256(patched).hexdigest() == 'ec778c6b39835e6d9ee8681faf99e827b18da0148b8d8e75f0c9d85925f4b1bc'
(root / 'assets/template.elf').write_bytes(patched)
print('Template patched: diagnostic text corrected and fixed-width native credit changed to Dev. By TaySon.')
