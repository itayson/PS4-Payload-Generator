using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Tayson.Providers;

/// <summary>
/// Opens the already-validated PKG as a streaming HTTP response for the
/// receiver architecture. The package is never buffered as one giant Android
/// file: bytes flow provider -> Android -> PS4 Receiver.
///
/// Signed URLs are preserved and redirects are handled explicitly. AkiraBox
/// keeps the browser-shaped cookie/referer/user-agent context used by the
/// validator.
/// </summary>
public static class PackageTransferClient
{
    public sealed class Session : IDisposable
    {
        internal HttpClient Client;
        internal HttpResponseMessage Response;
        public Stream Stream { get; internal set; }
        public Uri FinalUrl { get; internal set; }
        public long Length { get; internal set; }

        public void Dispose()
        {
            try { Stream?.Dispose(); } catch { }
            try { Response?.Dispose(); } catch { }
            try { Client?.Dispose(); } catch { }
        }
    }

    static string Header(HttpHeaders headers, string name)
        => headers.TryGetValues(name, out var values) ? string.Join(",", values) : "";

    static bool Encoded(HttpContentHeaders headers)
    {
        string value = Header(headers, "Content-Encoding");
        return value.Length > 0 &&
            !value.Equals("identity", StringComparison.OrdinalIgnoreCase);
    }

    static long RangeLength(HttpContentHeaders headers)
    {
        string value = Header(headers, "Content-Range");
        Match m = new Regex(
            @"\Abytes 0-([0-9]+)/([0-9]+)\z",
            RegexOptions.None).Match(value);

        if (!m.Success ||
            !long.TryParse(m.Groups[1].Value, out long last) ||
            !long.TryParse(m.Groups[2].Value, out long total) ||
            total <= 0 || last != total - 1)
            throw new InvalidDataException(
                "O servidor retornou um Content-Range incompatível com a transferência.");

        return total;
    }

    public static async Task<Session> OpenAsync(
        string value,
        Provider provider,
        long expectedLength,
        CancellationToken token = default)
    {
        if (expectedLength < 128)
            throw new InvalidDataException("Tamanho esperado do PKG inválido.");

        Uri url = LinkPolicy.Parse(value);
        string referer = AkiraDownloadProbe.DefaultReferer;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int redirects = 0; redirects <= 8; redirects++)
        {
            if (!seen.Add(url.AbsoluteUri))
                throw new InvalidDataException(
                    "O download entrou em um ciclo de redirecionamentos.");

            if (provider == Provider.AkiraBox &&
                !ProviderResolvers.IsAkiraBox(url))
                throw new InvalidDataException(
                    "O AkiraBox redirecionou a transferência para um host não reconhecido.");

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, expectedLength - 1);
            request.Headers.TryAddWithoutValidation(
                "Accept-Encoding", new[] { "identity" });

            if (provider == Provider.AkiraBox)
            {
                request.Headers.TryAddWithoutValidation(
                    "User-Agent", new[] { AkiraDownloadProbe.BrowserUserAgent });
                request.Headers.TryAddWithoutValidation(
                    "Accept", new[] { "*/*" });
                if (!string.IsNullOrWhiteSpace(referer))
                    request.Headers.TryAddWithoutValidation(
                        "Referer", new[] { referer });

                string cookie = AkiraDownloadProbe.GetCookieHeader(url.AbsoluteUri);
                if (!string.IsNullOrWhiteSpace(cookie))
                    request.Headers.TryAddWithoutValidation(
                        "Cookie", new[] { cookie });
            }

            var http = new HttpClient(
                new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromHours(12)
            };

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    token).ConfigureAwait(false);
            }
            catch
            {
                request.Dispose();
                http.Dispose();
                throw;
            }
            request.Dispose();

            int status = (int)response.StatusCode;
            if (status == 301 || status == 302 || status == 303 ||
                status == 307 || status == 308)
            {
                string location = Header(response.Headers, "Location");
                response.Dispose();
                http.Dispose();

                if (string.IsNullOrWhiteSpace(location))
                    throw new InvalidDataException(
                        "O redirecionamento não contém um endereço.");

                if (provider == Provider.AkiraBox)
                    referer = url.AbsoluteUri;

                url = LinkPolicy.Parse(
                    new Uri(url, location).AbsoluteUri);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                http.Dispose();

                if (status == 401 || status == 403)
                    throw new InvalidDataException(
                        "O link de download expirou ou perdeu autorização.");
                if (status == 404 || status == 410)
                    throw new InvalidDataException(
                        "O arquivo não está mais disponível.");
                throw new InvalidDataException(
                    "O servidor respondeu HTTP " + status + ".");
            }

            if (Encoded(response.Content.Headers))
            {
                response.Dispose();
                http.Dispose();
                throw new InvalidDataException(
                    "O servidor tentou codificar/comprimir o PKG durante a transferência.");
            }

            long actual;
            if (response.StatusCode == HttpStatusCode.PartialContent)
                actual = RangeLength(response.Content.Headers);
            else
                actual = response.Content.Headers.ContentLength.GetValueOrDefault();

            if (actual != expectedLength)
            {
                response.Dispose();
                http.Dispose();
                throw new InvalidDataException(
                    "O tamanho do PKG mudou desde a validação.");
            }

            Stream stream;
            try
            {
                stream = await response.Content
                    .ReadAsStreamAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
                response.Dispose();
                http.Dispose();
                throw;
            }

            return new Session
            {
                Client = http,
                Response = response,
                Stream = stream,
                FinalUrl = response.RequestMessage?.RequestUri ?? url,
                Length = actual
            };
        }

        throw new InvalidDataException(
            "O link excedeu o limite de redirecionamentos.");
    }
}
