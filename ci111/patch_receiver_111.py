from pathlib import Path
p=Path('ps4receiver/receiver/main.c')
t=p.read_text()
t=t.replace('#define RECEIVER_VERSION "1.1.0"', '#define RECEIVER_VERSION "1.1.1"')
t=t.replace('#define IO_BUFFER_SIZE (128 * 1024)', '#define IO_BUFFER_SIZE (512 * 1024)')
t=t.replace('#define NET_POOL_SIZE (1024 * 1024)', '#define NET_POOL_SIZE (2 * 1024 * 1024)')
t=t.replace('#define SSL_POOL_SIZE (512 * 1024)', '#define SSL_POOL_SIZE (1024 * 1024)')
t=t.replace('#define HTTP_POOL_SIZE (1024 * 1024)', '#define HTTP_POOL_SIZE (2 * 1024 * 1024)')
t=t.replace('#define RECEIVER_NET_SO_RCVTIMEO 0x1106', '#define RECEIVER_NET_SO_RCVTIMEO 0x1106\n#define RECEIVER_NET_SO_SNDBUF 0x1001\n#define RECEIVER_NET_SO_RCVBUF 0x1002\n#define RECEIVER_SOCKET_SNDBUF (2 * 1024 * 1024)\n#define RECEIVER_SOCKET_RCVBUF (512 * 1024)')
old='''    int yes = 1;\n    sceNetSetsockopt(fd, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_REUSEADDR, &yes, sizeof(yes));'''
new='''    int yes = 1;\n    sceNetSetsockopt(fd, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_REUSEADDR, &yes, sizeof(yes));\n    int sndbuf = RECEIVER_SOCKET_SNDBUF;\n    int rcvbuf = RECEIVER_SOCKET_RCVBUF;\n    sceNetSetsockopt(fd, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_SNDBUF, &sndbuf, sizeof(sndbuf));\n    sceNetSetsockopt(fd, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_RCVBUF, &rcvbuf, sizeof(rcvbuf));'''
assert old in t
t=t.replace(old,new,1)
old='''        int client = sceNetAccept(listener, (OrbisNetSockaddr *)&peer, &peer_len);\n        if (client < 0) break;'''
new='''        int client = sceNetAccept(listener, (OrbisNetSockaddr *)&peer, &peer_len);\n        if (client < 0) break;\n        int sndbuf = RECEIVER_SOCKET_SNDBUF;\n        int rcvbuf = RECEIVER_SOCKET_RCVBUF;\n        sceNetSetsockopt(client, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_SNDBUF, &sndbuf, sizeof(sndbuf));\n        sceNetSetsockopt(client, RECEIVER_NET_SOL_SOCKET, RECEIVER_NET_SO_RCVBUF, &rcvbuf, sizeof(rcvbuf));'''
assert old in t
t=t.replace(old,new,1)
t=t.replace('sceNetListen(fd, 8)', 'sceNetListen(fd, 16)')
t=t.replace('Tayson PS4 Receiver 1.1.0', 'Tayson PS4 Receiver 1.1.1')
p.write_text(t)

m=Path('ps4receiver/Makefile')
s=m.read_text().replace('VERSION     := 1.10','VERSION     := 1.11')
m.write_text(s)
