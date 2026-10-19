using System;
using Tayson.Providers;

static class Program
{
    static int checks;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: "+name);
        checks++; Console.WriteLine("PASS "+name);
    }
    static void Main()
    {
        const string standard = "HTTP/1.1 200 OK\r\ndevice-discovery-protocol-version:00020020\r\nhost-type:PS4\r\nhost-name:PlayStation\r\n\r\n";
        var probe = System.Text.Encoding.ASCII.GetString(Ps4Discovery.BuildSearchPacket());
        Check(probe == "SRCH * HTTP/1.1\n" +
                      "device-discovery-protocol-version:00020020\n",
            "canonical PS4 DDP search packet LF bytes");
        Check(!probe.Contains("\r"), "DDP probe does not use CRLF");
        Check(Ps4Discovery.IsPs4Response(standard), "standard PS4 response");
        Check(Ps4Discovery.IsPs4Response("HTTP/1.1 200 Ok\r\nhost-type: PS4\r\n"), "Sony PS4 200 Ok case variant");
        Check(Ps4Discovery.TryBuildKnownTarget("192.168.0.5", out var target) && target[4] == 192 && target[5] == 168 && target[6] == 0 && target[7] == 5, "known IP directed sockaddr");
        Check(!Ps4Discovery.TryBuildKnownTarget("8.8.8.8", out _), "public IP cannot receive directed probe");
        Check(Ps4Discovery.IsPs4Response("HTTP/1.1 200 OK\nhost-type: PS4\n"), "whitespace parsed");
        Check(Ps4Discovery.IsPs4Response("HTTP/1.1 200 OK\nHOST-TYPE:ps4\n"), "case insensitive");
        Check(!Ps4Discovery.IsPs4Response("HTTP/1.1 200 OK\nhost-type:PS5\n"), "reject PS5");
        Check(!Ps4Discovery.IsPs4Response("HTTP/1.1 200 OK\nhost-type:TV\n"), "reject TV");
        Check(!Ps4Discovery.IsPs4Response("HTTP/1.1 404 Not Found\nhost-type:PS4\n"), "reject non200");
        Check(!Ps4Discovery.IsPs4Response("HTTP/1.1 200 OK\nhost-name: PS4\n"), "reject spoofed title");
        Check(!Ps4Discovery.IsPs4Response("garbage"), "reject malformed");
        Check(!Ps4Discovery.IsPs4Response(""), "reject empty");
        Check(!Ps4Discovery.IsPs4Response(new string('A', 4097)), "reject oversized");
        Check(Ps4LanAddressPolicy.TryNormalize("192.168.0.7", out var address) && address == "192.168.0.7","accept last PS4 LAN");
        Check(Ps4LanAddressPolicy.BinLoaderPort==9090, "preserve original BinLoader port");
        var start = DateTime.UtcNow;
        var detected = Ps4Discovery.DiscoverAsync().GetAwaiter().GetResult();
        Check(detected != null && (DateTime.UtcNow - start).TotalSeconds < 8,
            "libc UDP discovery completes within bounded window in build sandbox");
        Console.WriteLine("RESULT: "+checks+" discovery protocol checks passed");
    }
}