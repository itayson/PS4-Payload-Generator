#!/usr/bin/env python3
import struct, sys, zlib

out = sys.argv[1] if len(sys.argv) > 1 else "sce_sys/icon0.png"
w = h = 512
rows = []
for y in range(h):
    row = bytearray()
    for x in range(w):
        # dark navy background with a simple cyan T-shaped receiver glyph
        r, g, b = 18, 27, 42
        if 96 <= y < 160 and 110 <= x < 402:
            r, g, b = 51, 194, 255
        if 210 <= x < 302 and 150 <= y < 404:
            r, g, b = 51, 194, 255
        if 184 <= x < 328 and 350 <= y < 414:
            r, g, b = 92, 224, 173
        row += bytes((r, g, b))
    rows.append(b"\x00" + bytes(row))
raw = b"".join(rows)

def chunk(t, data):
    return struct.pack(">I", len(data)) + t + data + struct.pack(">I", zlib.crc32(t + data) & 0xffffffff)

png = b"\x89PNG\r\n\x1a\n"
png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
png += chunk(b"IDAT", zlib.compress(raw, 9))
png += chunk(b"IEND", b"")
open(out, "wb").write(png)
print(out)
