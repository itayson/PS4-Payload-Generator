using System;

namespace Tayson.Providers;

/// <summary>
/// Strict private IPv4 PS4 destination policy. Pure local validation only:
/// no DNS, TCP probing or GoldHEN listener consumption.
/// </summary>
public static class Ps4LanAddressPolicy
{
    public const int BinLoaderPort = 9090;

    public static bool TryNormalize(string address, out string normalized)
    {
        normalized = "";
        string candidate = (address ?? "").Trim();
        if (candidate.Length < 7 || candidate.Length > 15) return false;
        string[] parts = candidate.Split('.');
        if (parts.Length != 4) return false;
        var b = new byte[4];
        for (int i = 0; i < parts.Length; i++)
        {
            string v = parts[i];
            if (v.Length < 1 || v.Length > 3 ||
                v.Length > 1 && v[0] == '0') return false;
            int n = 0;
            foreach (char ch in v)
            {
                if (ch < '0' || ch > '9') return false;
                n = n * 10 + ch - '0';
                if (n > 255) return false;
            }
            b[i] = (byte)n;
        }
        // RFC 1918 only: public, loopback, link-local, multicast and
        // DNS names are not eligible for console payload delivery.
        bool privateLan = b[0] == 10 ||
                          b[0] == 172 && b[1] >= 16 && b[1] <= 31 ||
                          b[0] == 192 && b[1] == 168;
        if (!privateLan) return false;
        // Block conventional network/broadcast host identifiers.
        if (b[3] == 0 || b[3] == 255) return false;
        normalized = b[0] + "." + b[1] + "." + b[2] + "." + b[3];
        return true;
    }

    public static string RejectionMessage =>
        "Informe o IPv4 privado do PS4 na rede local (ex.: 192.168.0.7). " +
        "Não utilize domínio, endereço público ou porta no campo de IP.";
}
