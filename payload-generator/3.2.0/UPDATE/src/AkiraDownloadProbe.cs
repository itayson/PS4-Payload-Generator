using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Android.Webkit;

namespace Tayson.Providers;

/// <summary>
/// AkiraBox download URLs are created by the browser flow and can require the
/// same browser-shaped request context while the signed access URL is valid.
/// This probe reuses Android WebView cookies for AkiraBox hosts and sends a
/// normal browser User-Agent/Referer while still validating the PKG with tiny
/// ranged requests only. It never downloads the complete package.
/// </summary>
public static class AkiraDownloadProbe
{
    public const string BrowserUserAgent = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Mobile Safari/537.36";
    public const string DefaultReferer = "https://akirabox.to/";
    static readonly Regex ContentId = new Regex(@"\A[A-Z]{2}[0-9]{4}-[A-Z]{4}[0-9]{5}_[0-9A-Z]{2}-[A-Z0-9_]{16}\z", RegexOptions.None);

    sealed class Transfer : IDisposable
    {
        public HttpClient Client;
        public HttpResponseMessage Response;
        public void Dispose() { Response.Dispose(); Client.Dispose(); }
    }

    static string Header(HttpHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? string.Join(",", values) : "";

    static bool Decimal(string text, out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
        {
            int digit = c - '0';
            if (digit < 0 || digit > 9 || value > (long.MaxValue - digit) / 10) return false;
            value = value * 10 + digit;
        }
        return true;
    }

    static (long From, long To, long Length) Range(HttpContentHeaders headers)
    {
        Match m = new Regex(@"\Abytes ([0-9]+)-([0-9]+)/([0-9]+)\z", RegexOptions.None)
            .Match(Header(headers, "Content-Range"));
        if (!m.Success || !Decimal(m.Groups[1].Value, out long from) ||
            !Decimal(m.Groups[2].Value, out long to) || !Decimal(m.Groups[3].Value, out long length) ||
            from > to || to >= length)
            throw new InvalidDataException("AkiraBox retornou um Content-Range inválido.");
        return (from, to, length);
    }

    static bool Encoded(HttpContentHeaders headers)
    {
        string value = Header(headers, "Content-Encoding");
        return value.Length > 0 && !value.Equals("identity", StringComparison.OrdinalIgnoreCase);
    }

    static string Cookies(Uri uri)
    {
        try { return CookieManager.Instance?.GetCookie(uri.AbsoluteUri) ?? ""; }
        catch { return ""; }
    }

    public static string GetCookieHeader(string value)
    {
        Uri uri = LinkPolicy.Parse(value);
        return ProviderResolvers.IsAkiraBox(uri) ? Cookies(uri) : "";
    }

    static async Task<Transfer> GetAsync(Uri start, long from, long to, CancellationToken token)
    {
        Uri url = start;
        string referer = DefaultReferer;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int redirects = 0; redirects <= 8; redirects++)
        {
            if (!ProviderResolvers.IsAkiraBox(url))
                throw new InvalidDataException("O download AkiraBox redirecionou para um host não reconhecido.");
            if (!seen.Add(url.AbsoluteUri))
                throw new InvalidDataException("O AkiraBox entrou em um ciclo de redirecionamentos.");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(from, to);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", new[] { "identity" });
            request.Headers.TryAddWithoutValidation("User-Agent", new[] { BrowserUserAgent });
            request.Headers.TryAddWithoutValidation("Accept", new[] { "*/*" });
            if (Uri.TryCreate(referer, UriKind.Absolute, out Uri _))
                request.Headers.TryAddWithoutValidation("Referer", new[] { referer });
            string cookie = Cookies(url);
            if (!string.IsNullOrWhiteSpace(cookie))
                request.Headers.TryAddWithoutValidation("Cookie", new[] { cookie });

            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(25) };
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); }
            catch { http.Dispose(); throw; }

            int status = (int)response.StatusCode;
            if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
            {
                string location = Header(response.Headers, "Location");
                response.Dispose();
                http.Dispose();
                if (string.IsNullOrEmpty(location))
                    throw new InvalidDataException("O redirecionamento AkiraBox não contém um endereço.");
                referer = url.AbsoluteUri;
                url = LinkPolicy.Parse(new Uri(url, location).AbsoluteUri);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                http.Dispose();
                if (status == 401 || status == 403)
                    throw new InvalidDataException("O AkiraBox recusou a URL assinada (HTTP " + status + "). Reabra o arquivo no AkiraBox e toque em Download para gerar um acesso novo.");
                if (status == 404 || status == 410)
                    throw new InvalidDataException("O link AkiraBox foi removido ou expirou.");
                throw new InvalidDataException("O AkiraBox respondeu HTTP " + status + ".");
            }
            return new Transfer { Client = http, Response = response };
        }
        throw new InvalidDataException("O AkiraBox excedeu o limite de redirecionamentos.");
    }

    static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        using Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        byte[] data = new byte[limit];
        int read = 0;
        while (read < limit)
        {
            int n = await stream.ReadAsync(data, read, limit - read, token).ConfigureAwait(false);
            if (n == 0) break;
            read += n;
        }
        if (read != data.Length) Array.Resize(ref data, read);
        return data;
    }

    public static async Task<DownloadInfo> ProbeAsync(string value, int prefixLimit = 128, CancellationToken token = default)
    {
        Uri requested = LinkPolicy.Parse(value);
        if (!ProviderResolvers.IsAkiraBox(requested))
            throw new ArgumentException("AkiraDownloadProbe aceita apenas hosts AkiraBox.", nameof(value));
        if (prefixLimit < 128 || prefixLimit > 2097152)
            throw new ArgumentOutOfRangeException(nameof(prefixLimit));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        token = timeout.Token;

        using var first = await GetAsync(requested, 0, prefixLimit - 1, token).ConfigureAwait(false);
        HttpResponseMessage response = first.Response;
        string mime = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mime.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0 ||
            mime.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 || Encoded(response.Content.Headers))
            throw new InvalidDataException("O endereço AkiraBox ainda entrega uma página, não o PKG.");

        var range = response.StatusCode == HttpStatusCode.PartialContent ? Range(response.Content.Headers) : default;
        if (response.StatusCode == HttpStatusCode.PartialContent && range.From != 0)
            throw new InvalidDataException("O AkiraBox enviou um Content-Range inicial inválido.");
        long size = response.StatusCode == HttpStatusCode.PartialContent
            ? range.Length : response.Content.Headers.ContentLength.GetValueOrDefault();
        if (size < 128) throw new InvalidDataException("Não foi possível obter o tamanho completo do PKG AkiraBox.");

        int wanted = (int)Math.Min(size, prefixLimit);
        if (response.StatusCode == HttpStatusCode.PartialContent && range.To != wanted - 1)
            throw new InvalidDataException("O AkiraBox retornou um intervalo diferente do solicitado.");
        byte[] prefix = await ReadBoundedAsync(response.Content, wanted, token).ConfigureAwait(false);
        if (prefix.Length != wanted)
            throw new InvalidDataException("O cabeçalho do PKG AkiraBox chegou incompleto.");
        if (prefix[0] != 0x7f || prefix[1] != 0x43 || prefix[2] != 0x4e || prefix[3] != 0x54)
            throw new InvalidDataException("O arquivo AkiraBox não possui assinatura de PKG de PS4.");

        string id = Encoding.ASCII.GetString(prefix, 0x40, 36);
        if (!ContentId.IsMatch(id))
            throw new InvalidDataException("O PKG AkiraBox não contém um Content ID válido.");
        Uri final = LinkPolicy.Parse((response.RequestMessage?.RequestUri ?? requested).AbsoluteUri);

        using var second = await GetAsync(final, 127, 127, token).ConfigureAwait(false);
        HttpResponseMessage seek = second.Response;
        var sr = seek.StatusCode == HttpStatusCode.PartialContent ? Range(seek.Content.Headers) : default;
        if (seek.StatusCode != HttpStatusCode.PartialContent || sr.From != 127 || sr.To != 127 ||
            sr.Length != size || Encoded(seek.Content.Headers))
            throw new InvalidDataException("O AkiraBox não aceitou HTTP Range de forma compatível com o payload.");
        byte[] one = await ReadBoundedAsync(seek.Content, 2, token).ConfigureAwait(false);
        if (one.Length != 1 || one[0] != prefix[127])
            throw new InvalidDataException("O AkiraBox retornou dados inconsistentes ao validar o download.");

        string file = "";
        string disposition = Header(response.Content.Headers, "Content-Disposition");
        Match named = new Regex(@"(?:^|;)\s*filename\*\s*=\s*UTF-8''([^;]+)", RegexOptions.IgnoreCase).Match(disposition);
        if (named.Success) file = Uri.UnescapeDataString(named.Groups[1].Value);
        else
        {
            named = new Regex("(?:^|;)\\s*filename\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Match(disposition);
            if (named.Success) file = named.Groups[1].Value;
        }
        if (string.IsNullOrWhiteSpace(file)) file = Uri.UnescapeDataString(Path.GetFileName(final.AbsolutePath));

        return new DownloadInfo
        {
            Url = final,
            Length = size,
            Prefix = prefix,
            ContentId = id,
            Filename = DownloadClient.CleanFilename(file)
        };
    }
}
