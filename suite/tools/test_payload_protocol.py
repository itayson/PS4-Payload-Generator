#!/usr/bin/env python3
"""Exercise the actual C payload protocol against simulated SCE networking."""
from pathlib import Path
import socket, struct, subprocess, tempfile, time
root=Path(__file__).resolve().parents[1]
def packet(path='ok', cookie=b'session=fixture'):
    fields=[f'https://example.test/{path}'.encode(), b'TestAgent', b'https://example.test/', cookie,
            b'Authorization: Bearer fixture\nRange: bytes=999-1000', b'UP0000-CUSA00001_00-TEST', b'test.pkg', b'PS4GD']
    return struct.pack('>IIQ',0x54594a31,1,10_000_000_000)+b''.join(struct.pack('>I',len(x))+x for x in fields)
def exchange(command,body=b''):
    with socket.create_connection(('127.0.0.1',9096),2) as s:
        s.settimeout(4);s.sendall(command+body)
        result=[]
        with s.makefile('rb') as f:
            for line in f:
                result.append(line.decode().strip())
        return result
def probe(p):
    for _ in range(20):
        if 'busy=0' in exchange(b'TAYSON/1 PING\n')[0]:break
        time.sleep(.01)
    return exchange(f'TAYSON/1 PROBE {len(p)}\n'.encode(),p)
with tempfile.TemporaryDirectory() as d:
    exe=Path(d)/'payload-host-test'
    subprocess.run(['gcc','-O1','-g','-Wall','-Wextra','-Werror','-fsanitize=undefined',str(root/'ps4payload/main.c'),str(root/'tools/mock_sce.c'),'-pthread','-o',str(exe)],check=True)
    process=subprocess.Popen([str(exe)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    try:
        assert process.stdout.readline().startswith('TAYSON/1 READY')
        count=0
        def check(value):
            global count
            assert value
            count+=1
        check('payload=1.5.0' in exchange(b'TAYSON/1 PING\n')[0])
        good=probe(packet())
        check('http=206 bytes=16 pkg=1 scheme=https' in good[-1])
        check(any('ssl2-init' in l for l in good) and any('http2-init' in l for l in good))
        check('http=200 bytes=16 pkg=1' in probe(packet('ignore-range'))[-1])
        check('pkg=0' in probe(packet('html'))[-1])
        check('http=403 bytes=0' in probe(packet('denied'))[-1])
        check('ERR stage=read-16-bytes' in probe(packet('truncated'))[-1])
        check('invalid-packet' in probe(packet(cookie=b'session=bad\r\nX-Injected: yes'))[-1])
        check('invalid-packet' in probe(packet(cookie=b'a\x00b'))[-1])
        check('invalid-packet' in probe(packet()+b'extra')[-1])
        malformed=bytearray(packet());malformed[16:20]=b'\xff'*4
        check('invalid-packet' in probe(malformed)[-1])
        check('invalid-length' in exchange(b'TAYSON/1 PROBE 99999999\n')[-1])
        check('invalid-length' in exchange(b'TAYSON/1 PROBE -1\n')[-1])
        check('probe-only' in exchange(b'TAYSON/1 JOB 100\n')[-1])
        check('payload=1.5.0' in exchange(b'TAYSON/1 PING\n')[0])
        print(f'{count} native protocol/HTTP simulation checks passed (not PS4 hardware tests)')
    finally:
        process.terminate();process.wait(timeout=4)
        errors=process.stderr.read()
        assert not errors,errors
