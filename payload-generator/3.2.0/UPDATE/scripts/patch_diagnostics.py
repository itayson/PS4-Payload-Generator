"""Reproduce the read-only string correction in the exact Phoenixx template."""
import hashlib
from pathlib import Path

root = Path(__file__).resolve().parents[2]
original = (root/'UPDATE/base/original-template.elf').read_bytes()
assert hashlib.sha256(original).hexdigest() == '1e9746fc0a3b3de33801c00bcd92791a791c795593a0caf213d173d780bf24d2'
offset = 0x10ff9
old, new = b'DESCONHECIDO\0', b'Desconhecido\0'
assert len(old) == len(new)
assert original[offset:offset+len(old)] == old
patched = original[:offset]+new+original[offset+len(old):]
assert hashlib.sha256(patched).hexdigest() == 'f5dd92e0fbcb95352ad587ec30efd8cb869dd90ed7435d0b0526aae263e06711'
(root/'assets/template.elf').write_bytes(patched)
print('Diagnostic comparison corrected; native instructions unchanged.')
