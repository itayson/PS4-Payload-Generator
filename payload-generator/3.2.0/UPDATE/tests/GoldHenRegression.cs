using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Tayson.Providers;

internal static class GoldHenRegression
{
    public static async Task RunAsync()
    {
        void Require(bool ok, string name)
        {
            if (!ok) throw new Exception("GoldHEN regression: " + name);
            Console.WriteLine("PASS GoldHEN " + name);
        }

        Require(GoldHenCompatibility.Classify("1.1") == GoldHenLoaderEvidence.NoIntegratedLoader, "1.x no integrated loader");
        Require(GoldHenCompatibility.Classify("2.0b") == GoldHenLoaderEvidence.LegacyNeedsVerification, "legacy loader");
        Require(GoldHenCompatibility.Classify("2.4b18.4") == GoldHenLoaderEvidence.LegacyNeedsVerification, "pre-ELF improvement");
        Require(GoldHenCompatibility.Classify("v2.4b18.5") == GoldHenLoaderEvidence.ElfCandidate, "ELF candidate");
        Require(GoldHenCompatibility.Classify("2.4b18.14") == GoldHenLoaderEvidence.ElfCandidate, "later ELF candidate");
        Require(GoldHenCompatibility.Classify("2.4b18.99") == GoldHenLoaderEvidence.Unknown, "unknown revision not assumed");
        Require(GoldHenCompatibility.Classify("") == GoldHenLoaderEvidence.Unknown, "missing version not assumed");
        Require(PayloadTransport.DefaultPort == 9090, "default port preserved");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            byte[] fixture = new byte[4096];
            new Random(19).NextBytes(fixture);
            Task send = PayloadTransport.SendAsync("127.0.0.1", fixture, port, 3000, 3000, 1);
            using var remote = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var received = new MemoryStream();
            await remote.GetStream().CopyToAsync(received).WaitAsync(TimeSpan.FromSeconds(5));
            await send.WaitAsync(TimeSpan.FromSeconds(5));
            Require(fixture.AsSpan().SequenceEqual(received.ToArray()), "loopback exact bytes and EOF");
            Require(!listener.Pending(), "no second connection after successful delivery");
            try
            {
                await PayloadTransport.SendAsync("127.0.0.1", Array.Empty<byte>(), port);
                throw new Exception("Empty payload accepted");
            }
            catch (ArgumentException) { Require(!listener.Pending(), "empty payload rejected before connect"); }
        }
        finally { listener.Stop(); }
    }
}
