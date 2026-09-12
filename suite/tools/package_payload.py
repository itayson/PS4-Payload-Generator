#!/usr/bin/env python3
"""Copy compiled, validated payloads into APK assets and record their digests."""
import hashlib,json,shutil,struct,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
elf=root/'ps4payload/tayson_probe.elf'
loader=Path(sys.argv[1])
data=elf.read_bytes()
assert data[:6] == b'\x7fELF\x02\x01' and struct.unpack_from('<H',data,18)[0] == 62
assert b'TAYSON/1 READY payload=' in data
bootstrap=loader.read_bytes()
assert 64 < len(bootstrap) < 4*1024*1024 and bootstrap[:4] != b'\x7fELF'
assets=root/'app/src/main/assets/payload';assets.mkdir(parents=True,exist_ok=True)
manifest={}
for source,name in [(elf,'tayson_probe.elf'),(loader,'elfldr.bin')]:
    shutil.copyfile(source,assets/name)
    raw=source.read_bytes();manifest[name]={'size':len(raw),'sha256':hashlib.sha256(raw).hexdigest()}
(assets/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps(manifest))
