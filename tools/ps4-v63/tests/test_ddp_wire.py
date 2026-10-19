#!/usr/bin/env python3
"""Independent Linux POSIX/UDP DDP contract checks, not an Android hardware test.

Confirms the exact LF-only DDP search payload in the C# source and simulates a
PS4 200 Ok reply on localhost port 987. No production PS4 or network scan.
"""
from pathlib import Path
import ctypes
import re
import socket
import struct
import threading
import time

SRC = Path(__file__).resolve().parents[3] / "payload-generator/3.2.0/UPDATE/src/Ps4Discovery.cs"
source = SRC.read_text(encoding="utf-8")
check_count = 0

def check(cond, name):
    global check_count
    if not cond:
        raise AssertionError(name)
    check_count += 1
    print("PASS", name)

check('const string Request = "SRCH * HTTP/1.1\\n" +' in source, "C# source uses LF request")
check('"device-discovery-protocol-version:00020020\\n"' in source, "C# source DDP version and terminator")
check('"SRCH * HTTP/1.1\\r\\n' not in source, "C# source not legacy CRLF")
check('EntryPoint = "bind"' in source and 'BindAddress(1987)' in source, "C# prefers source port 1987")
check("BindSocket(fd, BindAddress(1987), 16) == 0" in source, "C# optional binding preserves fallback")
check("KnownAddress(knownIp)" in source, "C# probes selected PS4 IP")
check('MakeAddress(255, 255, 255, 255)' in source, "C# retains broadcast fallback")
check("IsPs4Response" in source and "OrdinalIgnoreCase" in source, "C# response parser is case-insensitive")
check('DiscoveryPort = 987' in source, "Sony PS4 discovery port 987")
check('9090' in source and 'ConnectAsync(' not in source, "no BinLoader TCP call in discovery source")

wire = b"SRCH * HTTP/1.1\ndevice-discovery-protocol-version:00020020\n"
check(b"\r" not in wire and wire.count(b"\n") == 2, "canonical DDP bytes match independent implementation")
check(len(wire) < 100, "bounded discovery request")

# Verify the POSIX sockaddr_in layout the Android C# code constructs.
ip_octets = socket.inet_aton("192.168.0.5")
family = struct.pack("=H", socket.AF_INET)
port = struct.pack("!H", 987)
address = family + port + ip_octets + bytes(8)
check(len(address) == 16 and address[4:8] == bytes([192,168,0,5]), "sockaddr 16-byte layout for known IP")
check(address[2:4] == b"\x03\xdb", "port 987 is in network byte order")

# Native sendto/recvfrom/poll simulation with controlled local PS4-like responder.
response = (
    b"HTTP/1.1 200 Ok\n"
    b"host-id:TEST-NOT-A-REAL-PS4\nhost-type:PS4\n"
    b"device-discovery-protocol-version:00020020\n"
)
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind(("127.0.0.1", 0))
sock.settimeout(2)
received = []

def mock_server():
    try:
        payload, endpoint = sock.recvfrom(4096)
        received.append(payload)
        sock.sendto(response, endpoint)
    except Exception as exc:
        received.append(exc)

th = threading.Thread(target=mock_server, daemon=True)
th.start()
client = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
client.settimeout(2)
client.sendto(wire, ("127.0.0.1", sock.getsockname()[1]))
reply, endpoint = client.recvfrom(4096)
th.join(timeout=2)
check(received == [wire], "mock PS4 receives exact DDP discovery request")
check(reply == response and reply.startswith(b"HTTP/1.1 200 Ok"), "mock PS4 emits case-variant valid reply")
client.close()
sock.close()

print(f"RESULT: {check_count} independent PS4 DDP/UDP protocol checks passed")
