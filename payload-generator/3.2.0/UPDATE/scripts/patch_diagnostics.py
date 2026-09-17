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

# The inherited native show_credits() routine does not store its message as one
# contiguous string. It writes each byte to a stack buffer with a sequence of
# `mov byte ptr [rbp-N], imm8` instructions at virtual address 0x438. The .text
# section starts at file offset 0x4000, so only the imm8 operands are changed.
# Instruction layout, call target, function size and notification behavior stay
# exactly the same; only "Dev. By Phoenixx" becomes "Dev. By Tayson".
text_file_offset = 0x4000
credit_vaddr = 0x438
old_credit = b'Dev. By Phoenixx\0'
new_credit = b'Dev. By Tayson\0\0\0'
assert len(old_credit) == len(new_credit) == 17

for i, (old_byte, new_byte) in enumerate(zip(old_credit, new_credit)):
    instruction = text_file_offset + credit_vaddr + i * 4
    # Each instruction must remain: C6 45 <disp8> <imm8>.
    assert patched[instruction:instruction + 2] == b'\xC6\x45'
    assert patched[instruction + 2] == ((0xE0 + i) & 0xff)
    assert patched[instruction + 3] == old_byte
    patched[instruction + 3] = new_byte

patched = bytes(patched)
assert hashlib.sha256(patched).hexdigest() == '1cd739a6cfc5bd9bc0f8c86516eda7485da36655d98265e041e99a52d07276c1'
(root / 'assets/template.elf').write_bytes(patched)
print('Template patched: diagnostic text corrected and native credit changed to Dev. By Tayson.')
