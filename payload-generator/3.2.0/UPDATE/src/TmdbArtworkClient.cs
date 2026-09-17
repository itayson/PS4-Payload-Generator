using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Tayson.Providers;

/// <summary>
/// Optional PS4 title-artwork fallback. The PS4 title database exposes an icon
/// and, for many titles, a background image keyed only by Title ID. This client
/// is deliberately read-only, bounded and best-effort: failures never make a
/// valid PKG fail validation.
/// </summary>
public static class TmdbArtworkClient
{
    const int MaxImageBytes = 4194304;
    const string TmdbKeyHex = "F5DE66D2680E255B2DF79E74F890EBF349262F618BCAE2A9ACCDEE5156CE8DF2CDF2D48C71173CDC2594465B87405D197CF1AED3B7E9671EEB56CA6753C2E6B0";

    static readonly byte[] TmdbKey = Hex(TmdbKeyHex);

    static byte[] Hex(string text)
    {
        byte[] data = new byte[text.Length / 2];
        for (int i = 0; i < data.Length; i++)
        {
            int hi = Nibble(text[i * 2]);
            int lo = Nibble(text[i * 2 + 1]);
            data[i] = (byte)((hi << 4) | lo);
        }
        return data;
    }

    static int Nibble(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        throw new InvalidDataException("TMDB key inválida.");
    }

    static string Hash(string title)
    {
        using var hmac = new HMACSHA1(TmdbKey);
        byte[] digest = hmac.ComputeHash(Encoding.ASCII.GetBytes(title));
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
        if (!Regex.IsMatch(id, "^[A-Z]{4}[0-9]{5}$")) return "";
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

    static async Task<byte[]> DownloadBoundedAsync(HttpClient http, string url)
    {
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
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
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PS4TitleMetadata/1.00");
            string json = await http.GetStringAsync(metadataUrl).ConfigureAwait(false);

            var urls = new List<string>();
            foreach (Match m in Regex.Matches(json, "\\\"icon\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                AddUrl(urls, m.Groups[1].Value);
            Match background = Regex.Match(json, "\\\"backgroundImage\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
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
