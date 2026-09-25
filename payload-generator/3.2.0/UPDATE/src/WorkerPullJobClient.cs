using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Tayson.Providers;

public static class WorkerPullJobClient
{
    public static async Task<string> CreateAsync(
        string workerBase,
        string sourceUrl,
        Provider provider,
        CancellationToken token = default)
    {
        Uri worker = LinkPolicy.Parse(workerBase);
        if (worker.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(worker.Query))
            throw new InvalidDataException("Worker inválido.");

        string providerName = ProviderName(provider, sourceUrl);
        string cookie = "";
        string referer = "";
        string userAgent = "";

        if (provider == Provider.AkiraBox)
        {
            cookie = AkiraDownloadProbe.GetCookieHeader(sourceUrl);
            referer = AkiraDownloadProbe.DefaultReferer;
            userAgent = AkiraDownloadProbe.BrowserUserAgent;
        }

        using var http = new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            worker.AbsoluteUri.TrimEnd('/') + "/api/pull-jobs");

        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["provider"] = providerName,
                ["url"] = sourceUrl,
                ["cookie"] = cookie,
                ["referer"] = referer,
                ["userAgent"] = userAgent
            });

        using HttpResponseMessage response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            token).ConfigureAwait(false);

        string text = await response.Content
            .ReadAsStringAsync()
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidDataException(
                string.IsNullOrWhiteSpace(text)
                    ? "Worker recusou o job de download."
                    : text.Trim());

        Match match = Regex.Match(
            text ?? "",
            "\"url\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            throw new InvalidDataException("Worker retornou um job inválido.");

        string value = match.Groups[1].Value
            .Replace("\\/", "/")
            .Replace("\\u0026", "&");

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri route) ||
            route.Scheme != Uri.UriSchemeHttps ||
            !route.Host.Equals(worker.Host, StringComparison.OrdinalIgnoreCase) ||
            !route.AbsolutePath.StartsWith("/j/", StringComparison.OrdinalIgnoreCase) ||
            !route.AbsolutePath.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Worker retornou uma rota de pull inválida.");

        return route.AbsoluteUri;
    }

    static string ProviderName(Provider provider, string sourceUrl)
    {
        if (provider == Provider.AkiraBox) return "akira";
        if (provider == Provider.Mocha) return "mocha";
        if (provider == Provider.FileKeeper) return "filek";
        if (provider == Provider.VikingFile) return "viking";
        if (provider == Provider.Rootz) return "rootz";

        Uri uri = LinkPolicy.Parse(sourceUrl);
        if (BgftRoutePlanner.IsVikingR2(uri)) return "viking";
        if (BgftRoutePlanner.IsRootzR2(uri)) return "rootz";
        if (BgftRoutePlanner.IsMochaDownloadUrl(uri)) return "mocha";
        if (BgftRoutePlanner.IsFileKeeperDownloadUrl(uri)) return "filek";
        if (ProviderResolvers.IsAkiraBox(uri)) return "akira";

        throw new InvalidDataException(
            "Nesta fase, o Worker Pull aceita apenas provedores conhecidos.");
    }
}
