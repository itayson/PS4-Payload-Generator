using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Tayson.Providers;

/// <summary>
/// Best-effort PS4 Second Screen discovery using UDP 987 and bionic/POSIX libc.
/// No TCP connections, no connection to the one-shot GoldHEN BinLoader 9090.
/// Sources must send a PS4-type response and have an RFC1918 IPv4 address.
/// </summary>
public static class Ps4Discovery
{
    const int DiscoveryPort = 987;
    const string Request = "SRCH * HTTP/1.1\r\ndevice-discovery-protocol-version:00020020\r\n\r\n";
    const int AfInet = 2, SockDgram = 2, IpProtoUdp = 17, SolSocket = 1, SoBroadcast = 6, PollIn = 1;
    [StructLayout(LayoutKind.Sequential)]
    struct PollFd { public int File; public short Events; public short Returned; }

    [DllImport("libc", EntryPoint = "socket", SetLastError = true)]
    static extern int OpenSocket(int family, int socketType, int protocol);
    [DllImport("libc", EntryPoint = "setsockopt", SetLastError = true)]
    static extern int SetOption(int socket, int level, int option, ref int value, uint valueLength);
    [DllImport("libc", EntryPoint = "sendto", SetLastError = true)]
    static extern long SendTo(int socket, byte[] bytes, UIntPtr length, int flags, byte[] address, uint lengthAddress);
    [DllImport("libc", EntryPoint = "recvfrom", SetLastError = true)]
    static extern long ReceiveFrom(int socket, byte[] bytes, UIntPtr length, int flags, byte[] address, ref uint lengthAddress);
    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    static extern int Poll([In, Out] PollFd[] fds, uint count, int timeoutMs);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    static extern int Close(int socket);

    public static bool IsPs4Response(string response)
    {
        if (string.IsNullOrWhiteSpace(response) || response.Length > 4096) return false;
        string[] lines = response.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != "HTTP/1.1 200 OK") return false;
        foreach (string line in lines)
        {
            int sep = line.IndexOf(':');
            if (sep < 1) continue;
            if (line.Substring(0, sep).Trim().Equals("host-type", StringComparison.OrdinalIgnoreCase) &&
                line.Substring(sep + 1).Trim().Equals("PS4", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static byte[] BroadcastAddress()
    {
        // sockaddr_in: sin_family in host byte order, port in network byte order.
        var addr = new byte[16];
        addr[0] = BitConverter.IsLittleEndian ? (byte)2 : (byte)0;
        addr[1] = BitConverter.IsLittleEndian ? (byte)0 : (byte)2;
        addr[2] = (byte)(DiscoveryPort >> 8);
        addr[3] = (byte)(DiscoveryPort & 255);
        for (int i = 4; i < 8; i++) addr[i] = 255;
        return addr;
    }

    public static Task<IReadOnlyList<string>> DiscoverAsync()
        => Task.Run<IReadOnlyList<string>>(() =>
        {
            var found = new List<string>();
            int fd = -1;
            try
            {
                fd = OpenSocket(AfInet, SockDgram, IpProtoUdp);
                if (fd < 0) return found;
                int enabled = 1;
                if (SetOption(fd, SolSocket, SoBroadcast, ref enabled, 4) != 0) return found;
                byte[] bytes = Encoding.ASCII.GetBytes(Request);
                if (SendTo(fd, bytes, (UIntPtr)(uint)bytes.Length, 0, BroadcastAddress(), 16) < 0)
                    return found;

                DateTime stop = DateTime.UtcNow.AddSeconds(3);
                var poll = new[] { new PollFd { File = fd, Events = PollIn } };
                while (DateTime.UtcNow < stop)
                {
                    int timeout = (int)Math.Min(500, Math.Max(1, (stop - DateTime.UtcNow).TotalMilliseconds));
                    poll[0].Returned = 0;
                    int ready = Poll(poll, 1, timeout);
                    if (ready < 0) break;
                    if (ready == 0 || (poll[0].Returned & PollIn) == 0) continue;

                    var reply = new byte[4096];
                    var origin = new byte[16];
                    uint originLength = 16;
                    long received = ReceiveFrom(fd, reply, (UIntPtr)(uint)reply.Length, 0, origin, ref originLength);
                    if (received <= 0 || received > reply.Length || originLength < 8) continue;
                    if (!IsPs4Response(Encoding.ASCII.GetString(reply, 0, (int)received))) continue;
                    string ip = origin[4] + "." + origin[5] + "." + origin[6] + "." + origin[7];
                    if (Ps4LanAddressPolicy.TryNormalize(ip, out string normalized) &&
                        !found.Contains(normalized)) found.Add(normalized);
                }
            }
            catch (Exception)
            {
                // Android Wi-Fi may block broadcasts. Never attempt TCP 9090 as a fallback.
            }
            finally
            {
                if (fd >= 0) Close(fd);
            }
            return found;
        });
}
