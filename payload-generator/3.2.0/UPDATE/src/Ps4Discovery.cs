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
    public static string LastDiagnostic { get; private set; } = "Busca não iniciada.";
    // Canonical DDP search uses LF, without an extra blank line.
    const string Request = "SRCH * HTTP/1.1\n" +
                           "device-discovery-protocol-version:00020020\n";
    public static byte[] BuildSearchPacket() => Encoding.ASCII.GetBytes(Request);
    const int AfInet = 2, SockDgram = 2, IpProtoUdp = 17, SolSocket = 1, SoBroadcast = 6, PollIn = 1;
    [StructLayout(LayoutKind.Sequential)]
    struct PollFd { public int File; public short Events; public short Returned; }

    [DllImport("libc", EntryPoint = "socket", SetLastError = true)]
    static extern int OpenSocket(int family, int socketType, int protocol);
    [DllImport("libc", EntryPoint = "setsockopt", SetLastError = true)]
    static extern int SetOption(int socket, int level, int option, ref int value, uint valueLength);
    [DllImport("libc", EntryPoint = "bind", SetLastError = true)]
    static extern int BindSocket(int socket, byte[] address, uint addressLength);
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
        if (lines.Length < 2 || !lines[0].Trim().Equals("HTTP/1.1 200 OK", StringComparison.OrdinalIgnoreCase)) return false;
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

    static byte[] MakeAddress(byte a, byte b, byte c, byte d)
    {
        // sockaddr_in: native-endian family, network-endian port, raw IPv4 octets.
        var addr = new byte[16];
        addr[0] = BitConverter.IsLittleEndian ? (byte)2 : (byte)0;
        addr[1] = BitConverter.IsLittleEndian ? (byte)0 : (byte)2;
        addr[2] = (byte)(DiscoveryPort >> 8);
        addr[3] = (byte)(DiscoveryPort & 255);
        addr[4] = a; addr[5] = b; addr[6] = c; addr[7] = d;
        return addr;
    }

    static byte[] BindAddress(int port)
    {
        var address = MakeAddress(0, 0, 0, 0);
        address[2] = (byte)(port >> 8);
        address[3] = (byte)(port & 255);
        return address;
    }

    static byte[] KnownAddress(string knownIp)
    {
        if (!Ps4LanAddressPolicy.TryNormalize(knownIp, out string ip)) return null;
        var octets = ip.Split('.');
        return MakeAddress(byte.Parse(octets[0]), byte.Parse(octets[1]),
                           byte.Parse(octets[2]), byte.Parse(octets[3]));
    }

    public static bool TryBuildKnownTarget(string knownIp, out byte[] address)
    {
        address = KnownAddress(knownIp);
        return address != null;
    }

    // Only a user-invoked discovery; knownIp is never scanned or altered.
    public static Task<IReadOnlyList<string>> DiscoverAsync(string knownIp = null)
        => Task.Run<IReadOnlyList<string>>(() =>
        {
            var found = new List<string>();
            int fd = -1;
            int sent = 0;
            int receivedCount = 0;
            int validResponses = 0;
            string issue = "";
            byte[] direct = KnownAddress(knownIp);
            try
            {
                LastDiagnostic = "Consultando UDP 987…";
                fd = OpenSocket(AfInet, SockDgram, IpProtoUdp);
                if (fd < 0)
                {
                    LastDiagnostic = "Android não abriu socket UDP (código " +
                        Marshal.GetLastWin32Error() + ").";
                    return found;
                }
                // Prefer the widely-used DDP source port 1987. If occupied,
                // let sendto auto-bind an ephemeral port rather than failing.
                bool bound1987 = BindSocket(fd, BindAddress(1987), 16) == 0;
                int enabled = 1;
                bool broadcastAllowed = SetOption(fd, SolSocket, SoBroadcast, ref enabled, 4) == 0;
                byte[] bytes = BuildSearchPacket();
                DateTime stop = DateTime.UtcNow.AddSeconds(4);
                DateTime nextProbe = DateTime.UtcNow;
                int rounds = 0;
                var poll = new[] { new PollFd { File = fd, Events = PollIn } };
                while (DateTime.UtcNow < stop)
                {
                    if (rounds < 2 && DateTime.UtcNow >= nextProbe)
                    {
                        if (direct != null)
                        {
                            long n = SendTo(fd, bytes, (UIntPtr)(uint)bytes.Length, 0, direct, 16);
                            if (n == bytes.Length) sent++;
                            else issue = "Consulta direta UDP recusada (" +
                                Marshal.GetLastWin32Error() + ").";
                        }
                        if (broadcastAllowed)
                        {
                            var broadcast = MakeAddress(255, 255, 255, 255);
                            long n = SendTo(fd, bytes, (UIntPtr)(uint)bytes.Length, 0, broadcast, 16);
                            if (n == bytes.Length) sent++;
                            else if (issue.Length == 0) issue = "Broadcast UDP recusado (" +
                                Marshal.GetLastWin32Error() + ").";
                        }
                        rounds++;
                        nextProbe = DateTime.UtcNow.AddSeconds(2);
                    }
                    int timeout = (int)Math.Min(450, Math.Max(1, (stop - DateTime.UtcNow).TotalMilliseconds));
                    poll[0].Returned = 0;
                    int ready = Poll(poll, 1, timeout);
                    if (ready < 0)
                    {
                        issue = "Recepção UDP falhou (" + Marshal.GetLastWin32Error() + ").";
                        break;
                    }
                    if (ready == 0 || (poll[0].Returned & PollIn) == 0) continue;
                    var reply = new byte[4096];
                    var origin = new byte[16];
                    uint originLength = 16;
                    long received = ReceiveFrom(fd, reply, (UIntPtr)(uint)reply.Length, 0, origin, ref originLength);
                    if (received <= 0 || received > reply.Length || originLength < 8) continue;
                    receivedCount++;
                    if (!IsPs4Response(Encoding.ASCII.GetString(reply, 0, (int)received))) continue;
                    validResponses++;
                    string ip = origin[4] + "." + origin[5] + "." + origin[6] + "." + origin[7];
                    if (Ps4LanAddressPolicy.TryNormalize(ip, out string normalized) &&
                        !found.Contains(normalized)) found.Add(normalized);
                }
                if (found.Count > 0)
                    LastDiagnostic = "PS4 respondeu ao protocolo UDP 987 (" +
                        (bound1987 ? "origem 1987" : "porta de origem dinâmica") + ").";
                else if (sent == 0)
                    LastDiagnostic = issue.Length > 0 ? issue : "Não foi possível enviar consultas UDP.";
                else if (receivedCount == 0)
                    LastDiagnostic = "Consultas UDP enviadas, mas nenhuma resposta chegou (verifique Wi-Fi e Segunda Tela).";
                else if (validResponses == 0)
                    LastDiagnostic = "Houve resposta UDP, mas nenhuma identificada como PS4.";
                else
                    LastDiagnostic = "Respostas PS4 recebidas fora dos endereços locais permitidos.";
            }
            catch (Exception ex)
            {
                LastDiagnostic = "Erro na descoberta UDP (" + ex.GetType().Name + ").";
            }
            finally
            {
                if (fd >= 0) Close(fd);
            }
            return found;
        });
}
