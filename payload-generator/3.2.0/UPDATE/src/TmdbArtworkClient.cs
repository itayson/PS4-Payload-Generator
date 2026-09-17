using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Tayson.Providers;

/// <summary>
/// Optional PS4 title-artwork fallback. The title database exposes an icon and,
/// for many titles, a background image keyed by Title ID. This code avoids APIs
/// trimmed from the inherited Android runtime and remains strictly best-effort:
/// artwork failure never invalidates an otherwise valid PKG.
/// </summary>
public static class TmdbArtworkClient
{
    const int MaxImageBytes = 4194304;
    const string TmdbKeyHex = "F5DE66D2680E255B2DF79E74F890EBF349262F618BCAE2A9ACCDEE5156CE8DF2CDF2D48C71173CDC2594465B87405D197CF1AED3B7E9671EEB56CA6753C2E6B0";
    static readonly byte[] TmdbKey = Hex(TmdbKeyHex);
    static readonly Regex TitleId = new Regex("^[A-Z]{4}[0-9]{5}$", RegexOptions.None);
    static readonly Regex IconUrl = new Regex("\\\"icon\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
    static readonly Regex BackgroundUrl = new Regex("\\\"backgroundImage\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);

    static byte[] Hex(string text)
    {
        byte[] data = new byte[text.Length / 2];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)((Nibble(text[i * 2]) << 4) | Nibble(text[i * 2 + 1]));
        return data;
    }

    static int Nibble(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        throw new InvalidDataException("TMDB key inválida.");
    }

    static uint Rol(uint value, int count) => (value << count) | (value >> (32 - count));

    // Compact SHA-1 implementation used only to derive the documented PS4 TMDB
    // path. We keep it local because HMACSHA1 is trimmed from the base APK runtime.
    static byte[] Sha1(byte[] source)
    {
        ulong bits = (ulong)source.Length * 8UL;
        int padded = ((source.Length + 9 + 63) / 64) * 64;
        byte[] message = new byte[padded];
        Array.Copy(source, message, source.Length);
        message[source.Length] = 0x80;
        for (int i = 0; i < 8; i++)
            message[padded - 1 - i] = (byte)(bits >> (i * 8));

        uint h0 = 0x67452301;
        uint h1 = 0xEFCDAB89;
        uint h2 = 0x98BADCFE;
        uint h3 = 0x10325476;
        uint h4 = 0xC3D2E1F0;
        uint[] w = new uint[80];

        for (int block = 0; block < padded; block += 64)
        {
            for (int i = 0; i < 16; i++)
            {
                int p = block + i * 4;
                w[i] = ((uint)message[p] << 24) | ((uint)message[p + 1] << 16) |
                       ((uint)message[p + 2] << 8) | message[p + 3];
            }
            for (int i = 16; i < 80; i++)
                w[i] = Rol(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);

            uint a = h0, b = h1, c = h2, d = h3, e = h4;
            for (int i = 0; i < 80; i++)
            {
                uint f, k;
                if (i < 20) { f = (b & c) | ((~b) & d); k = 0x5A827999; }
                else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                else { f = b ^ c ^ d; k = 0xCA62C1D6; }
                uint temp = unchecked(Rol(a, 5) + f + e + k + w[i]);
                e = d; d = c; c = Rol(b, 30); b = a; a = temp;
            }
            h0 = unchecked(h0 + a); h1 = unchecked(h1 + b); h2 = unchecked(h2 + c);
            h3 = unchecked(h3 + d); h4 = unchecked(h4 + e);
        }

        byte[] result = new byte[20];
        Write32(result, 0, h0); Write32(result, 4, h1); Write32(result, 8, h2);
        Write32(result, 12, h3); Write32(result, 16, h4);
        return result;
    }

    static void Write32(byte[] output, int p, uint value)
    {
        output[p] = (byte)(value >> 24); output[p + 1] = (byte)(value >> 16);
        output[p + 2] = (byte)(value >> 8); output[p + 3] = (byte)value;
    }

    static byte[] HmacSha1(byte[] key, byte[] value)
    {
        byte[] normalized = key.Length > 64 ? Sha1(key) : key;
        byte[] inner = new byte[64 + value.Length];
        byte[] outer = new byte[64 + 20];
        for (int i = 0; i < 64; i++)
        {
            byte b = i < normalized.Length ? normalized[i] : (byte)0;
            inner[i] = (byte)(b ^ 0x36);
            outer[i] = (byte)(b ^ 0x5c);
        }
        Array.Copy(value, 0, inner, 64, value.Length);
        byte[] innerHash = Sha1(inner);
        Array.Copy(innerHash, 0, outer, 64, innerHash.Length);
        return Sha1(outer);
    }

    static string Hash(string title)
    {
        byte[] digest = HmacSha1(TmdbKey, Encoding.ASCII.GetBytes(title));
        var text = new StringBuilder(digest.Length * 2);
        foreach (byte b in digest) text.Append(b.ToString("X2"));
        return text.ToString();
    }

    static bool AllowedImageHost(Uri uri)
    {
        string host = uri.IdnHost.TrimEnd('.');
        return LinkPolicy.Domain(host, "playstation.net");
    }

    static string JsonUrl(string titleId)
    {
        string id = (titleId ?? "").Trim().ToUpperInvariant();
        if (!TitleId.IsMatch(id)) return "";
        string np = id + "_00";
        return "https://tmdb.np.dl.playstation.net/tmdb2/" + np + "_" + Hash(np) + "/" + np + ".json";
    }

    static string UnescapeUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return value.Replace("\\/", "/").Replace("\\u0026", "&").Trim();
    }

    static void AddUrl(List<string> urls, string value)
    {
        value = UnescapeUrl(value);
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            value = "https://" + value.Substring(7);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps || !AllowedImageHost(uri)) return;
        foreach (string existing in urls)
            if (existing.Equals(uri.AbsoluteUri, StringComparison.Ordinal)) return;
        urls.Add(uri.AbsoluteUri);
    }

    static bool IsImage(byte[] data)
    {
        if (data == null || data.Length < 8) return false;
        bool png = data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71 &&
                   data[4] == 13 && data[5] == 10 && data[6] == 26 && data[7] == 10;
        bool jpeg = data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff;
        return png || jpeg;
    }

    static async Task<HttpResponseMessage> SendAsync(HttpClient http, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
    }

    static async Task<string> DownloadTextAsync(HttpClient http, string url)
    {
        using HttpResponseMessage response = await SendAsync(http, url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return "";
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    static async Task<byte[]> DownloadBoundedAsync(HttpClient http, string url)
    {
        using HttpResponseMessage response = await SendAsync(http, url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        long declared = response.Content.Headers.ContentLength.GetValueOrDefault();
        if (declared > MaxImageBytes) return null;
        using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16384];
        while (buffer.Length <= MaxImageBytes)
        {
            int n = await stream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false);
            if (n == 0) break;
            if (buffer.Length + n > MaxImageBytes) return null;
            buffer.Write(chunk, 0, n);
        }
        byte[] data = buffer.ToArray();
        return IsImage(data) ? data : null;
    }

    public static async Task<byte[][]> FetchAsync(string titleId)
    {
        string metadataUrl = JsonUrl(titleId);
        if (metadataUrl.Length == 0) return Array.Empty<byte[]>();

        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
            { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", new[] { "PS4TitleMetadata/1.00" });
            string json = await DownloadTextAsync(http, metadataUrl).ConfigureAwait(false);
            if (json.Length == 0) return Array.Empty<byte[]>();

            var urls = new List<string>();
            foreach (Match m in IconUrl.Matches(json)) AddUrl(urls, m.Groups[1].Value);
            Match background = BackgroundUrl.Match(json);
            if (background.Success) AddUrl(urls, background.Groups[1].Value);

            var result = new List<byte[]>();
            foreach (string url in urls)
            {
                if (result.Count >= 3) break;
                byte[] image = await DownloadBoundedAsync(http, url).ConfigureAwait(false);
                if (image != null) result.Add(image);
            }
            return result.ToArray();
        }
        catch
        {
            return Array.Empty<byte[]>();
        }
    }
}
