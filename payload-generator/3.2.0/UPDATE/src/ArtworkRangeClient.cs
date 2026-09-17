using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Tayson.Providers;

/// <summary>
/// Optional small-range reader used only for PKG artwork discovered in the
/// package file table. It never downloads the complete package and failures are
/// treated as optional metadata failures by UiArtworkStore.
/// </summary>
public static class ArtworkRangeClient
{
    static string Header(HttpHeaders headers, string name)
        => headers.TryGetValues(name, out var values) ? string.Join(",", values) : "";

    public static async Task<byte[]> ReadAsync(string value, long offset, int length, CancellationToken token = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 1 || length > 2097152) throw new ArgumentOutOfRangeException(nameof(length));
        long to = checked(offset + length - 1L);
        Uri url = LinkPolicy.Parse(value);
        bool akira = ProviderResolvers.IsAkiraBox(url);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string referer = akira ? AkiraDownloadProbe.DefaultReferer : "";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        token = timeout.Token;

        for (int redirects = 0; redirects <= 8; redirects++)
        {
            if (!seen.Add(url.AbsoluteUri)) throw new InvalidDataException("Ciclo de redirecionamento ao carregar artwork.");
            if (akira && !ProviderResolvers.IsAkiraBox(url))
                throw new InvalidDataException("Artwork AkiraBox redirecionou para host não reconhecido.");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(offset, to);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", new[] { "identity" });
            if (akira)
            {
                request.Headers.TryAddWithoutValidation("User-Agent", new[] { AkiraDownloadProbe.BrowserUserAgent });
                request.Headers.TryAddWithoutValidation("Referer", new[] { referer });
                string cookie = AkiraDownloadProbe.GetCookieHeader(url.AbsoluteUri);
                if (!string.IsNullOrWhiteSpace(cookie))
                    request.Headers.TryAddWithoutValidation("Cookie", new[] { cookie });
            }

            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(25) };
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
            {
                string location = Header(response.Headers, "Location");
                if (string.IsNullOrWhiteSpace(location)) throw new InvalidDataException("Redirecionamento de artwork sem Location.");
                referer = url.AbsoluteUri;
                url = LinkPolicy.Parse(new Uri(url, location).AbsoluteUri);
                continue;
            }

            if (response.StatusCode != HttpStatusCode.PartialContent)
                throw new InvalidDataException("Servidor não aceitou Range para artwork (HTTP " + status + ").");
            string encoding = Header(response.Content.Headers, "Content-Encoding");
            if (encoding.Length > 0 && !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Artwork retornou conteúdo comprimido.");

            RangeInfo range = DownloadClient.ParseContentRangeForTests(Header(response.Content.Headers, "Content-Range"));
            if (range.From != offset || range.To != to)
                throw new InvalidDataException("Artwork retornou intervalo diferente do solicitado.");

            using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            byte[] data = new byte[length];
            int read = 0;
            while (read < data.Length)
            {
                int n = await stream.ReadAsync(data, read, data.Length - read, token).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
            if (read != data.Length) throw new InvalidDataException("Artwork chegou incompleto.");
            return data;
        }
        throw new InvalidDataException("Artwork excedeu o limite de redirecionamentos.");
    }
}
