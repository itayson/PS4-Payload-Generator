using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Tayson.Providers;

/// <summary>
/// PS4 Second Screen discovery over UDP 987; never connects to TCP 9090.
/// Discovery is best-effort, user-confirmed, and does NOT imply that GoldHEN is running.
/// </summary>
public static class Ps4Discovery
{
    const int DiscoveryPort = 987;
    const string Request = "SRCH * HTTP/1.1\r\ndevice-discovery-protocol-version:00020020\r\n\r\n";

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

    public static Task<IReadOnlyList<string>> DiscoverAsync()
        => Task.Run<IReadOnlyList<string>>(() =>
        {
            var found = new List<string>();
            try
            {
                using var client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                client.EnableBroadcast = true;
                client.Client.ReceiveTimeout = 500;
                byte[] bytes = Encoding.ASCII.GetBytes(Request);
                client.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                DateTime stop = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < stop)
                {
                    try
                    {
                        var remote = new IPEndPoint(IPAddress.Any, 0);
                        byte[] reply = client.Receive(ref remote);
                        if (reply.Length == 0 || reply.Length > 4096) continue;
                        string message = Encoding.ASCII.GetString(reply);
                        if (!IsPs4Response(message)) continue;
                        string ip = remote.Address.ToString();
                        if (Ps4LanAddressPolicy.TryNormalize(ip, out string normalized) &&
                            !found.Contains(normalized)) found.Add(normalized);
                    }
                    catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut ||
                                                     e.SocketErrorCode == SocketError.WouldBlock)
                    {
                        // Normal after a discovery window without responses.
                    }
                }
            }
            catch (SocketException)
            {
                // Network may disallow UDP broadcasts; manual IP entry remains available.
            }
            return found;
        });
}
