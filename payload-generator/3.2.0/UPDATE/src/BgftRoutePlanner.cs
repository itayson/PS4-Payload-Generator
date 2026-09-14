using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tayson.Providers;

/// <summary>
/// Converts a freshly validated provider URL into the URL that is embedded in
/// the generated ELF. The original provider URL remains the source of truth for
/// DownloadClient/PKG validation; this class is only a BGFT transport adapter.
/// </summary>
public sealed class BgftRoutePlanner : IDisposable
{
    public const string DefaultWorkerBase = "https://drive.itaysondaniel.workers.dev";
    const int PayloadUrlLimit = 2047;
    const int RouteReplyLimit = 4096;

    readonly Func<HttpMessageHandler> factory;
    readonly string workerBase;

    public BgftRoutePlanner() : this(
        () => new HttpClientHandler { AllowAutoRedirect = false },
        DefaultWorkerBase) { }

    public BgftRoutePlanner(Func<HttpMessageHandler> createHandler, string worker = DefaultWorkerBase)
    {
        factory = createHandler ?? throw new ArgumentNullException(nameof(createHandler));
        Uri parsed = LinkPolicy.Parse(worker);
        if (parsed.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(parsed.Query))
            throw new ArgumentException("O endereço do Worker deve ser HTTPS e sem query string.", nameof(worker));
        workerBase = parsed.AbsoluteUri.TrimEnd('/');
    }

    public void Dispose() { }

    public async Task<string> PrepareAsync(string upstreamUrl, CancellationToken token = default)
    {
        Uri uri = LinkPolicy.Parse(upstreamUrl);
        ValidateTemporalParameters(uri);

        if (!IsSupportedR2(uri))
            return upstreamUrl.Trim();

        string exact = upstreamUrl.Trim();
        Exception routeError = null;
        try
        {
            string shortRoute = await CreateWorkerRouteAsync(exact, token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(shortRoute)) return shortRoute;
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException)
        {
            if (ex is OperationCanceledException && token.IsCancellationRequested) throw;
            routeError = ex;
        }

        // Worker v7 compatibility path. This does not require KV and still keeps
        // the phone out of the PKG data path. It is safe only while the resulting
        // ELF URL fits the template's 2,047-byte field.
        string inline = workerBase + "/r2.pkg?u=" + Base64Url(Encoding.UTF8.GetBytes(exact));
        string registration = PayloadPatcher.RegistrationUrl(inline);
        if (Encoding.UTF8.GetByteCount(registration) > PayloadUrlLimit)
            throw new InvalidDataException(
                "A URL R2 assinada é longa demais para o payload. Configure o binding KV ROUTES no Worker para gerar uma rota curta." +
                (routeError == null ? "" : " O registro de rota também falhou: " + routeError.Message));
        return inline;
    }

    async Task<string> CreateWorkerRouteAsync(string exactUpstream, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var http = new HttpClient(factory()) { Timeout = TimeSpan.FromSeconds(15) };
        using var request = new HttpRequestMessage(HttpMethod.Post, workerBase + "/api/routes");
        request.Content = new StringContent(exactUpstream, Encoding.UTF8, "text/plain");
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);

        string text = await ReadBoundedAsync(response.Content, RouteReplyLimit, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            int status = (int)response.StatusCode;
            if (status == 400 || status == 401 || status == 403 || status == 410)
                throw new InvalidDataException(string.IsNullOrWhiteSpace(text)
                    ? "O Worker recusou a URL R2 (HTTP " + status + ")."
                    : text.Trim());

            if (status == 404 || status == 405 || status >= 500)
                return null;

            throw new InvalidDataException("O Worker respondeu com um status inesperado (HTTP " + status + ").");
        }

        string route = text.Trim();
        if (!Uri.TryCreate(route, UriKind.Absolute, out Uri result) ||
            result.Scheme != Uri.UriSchemeHttps ||
            !result.Host.Equals(new Uri(workerBase).Host, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(result.UserInfo) ||
            !result.AbsolutePath.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O Worker retornou uma rota BGFT inválida.");

        if (Encoding.UTF8.GetByteCount(PayloadPatcher.RegistrationUrl(route)) > PayloadUrlLimit)
            throw new InvalidDataException("A rota devolvida pelo Worker excede o espaço disponível no payload.");
        return route;
    }

    static async Task<string> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        using Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        byte[] buffer = new byte[limit + 1];
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer, total, buffer.Length - total, token).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
        }
        if (total > limit) throw new InvalidDataException("Resposta do Worker maior que o limite permitido.");
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    public static bool IsVikingR2(Uri uri) => IsR2WithPrefix(uri, "vikingfile.");

    public static bool IsRootzR2(Uri uri) => IsR2WithPrefix(uri, "rootz-files.");

    public static bool IsSupportedR2(Uri uri) => IsVikingR2(uri) || IsRootzR2(uri);

    static bool IsR2WithPrefix(Uri uri, string prefix)
    {
        if (uri == null || uri.Scheme != Uri.UriSchemeHttps) return false;

        string host = uri.IdnHost.TrimEnd('.');
        const string suffix = ".r2.cloudflarestorage.com";
        if (!host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        int accountLength = host.Length - prefix.Length - suffix.Length;
        if (accountLength != 32) return false;
        for (int i = prefix.Length; i < prefix.Length + accountLength; i++)
        {
            char c = host[i];
            bool hex = (c >= '0' && c <= '9') ||
                       (c >= 'a' && c <= 'f') ||
                       (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }

        return Query(uri, "X-Amz-Algorithm").Equals("AWS4-HMAC-SHA256", StringComparison.Ordinal) &&
            Query(uri, "X-Amz-Signature").Length >= 32 &&
            Query(uri, "X-Amz-Date").Length > 0 && Query(uri, "X-Amz-Expires").Length > 0;
    }

    /// <summary>
    /// Performs format/range validation without using DateTimeOffset APIs that
    /// are absent from the trimmed Xamarin runtime embedded in the original V3.
    /// Actual clock-expiry enforcement for R2 is performed by Worker v7; Mocha
    /// itself remains authoritative for its direct token expiry.
    /// </summary>
    public static void ValidateTemporalParameters(Uri uri)
    {
        if (IsSupportedR2(uri))
        {
            string date = Query(uri, "X-Amz-Date");
            string secondsText = Query(uri, "X-Amz-Expires");
            if (!LooksLikeAmzDate(date) ||
                !long.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds) ||
                seconds <= 0 || seconds > 604800)
                throw new InvalidDataException("A assinatura temporária R2 contém parâmetros de validade inválidos.");
            return;
        }

        if (LinkPolicy.Domain(uri.IdnHost, "mocha.my"))
        {
            string expires = Query(uri, "expires");
            if (expires.Length > 0 &&
                (!long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out long unix) || unix <= 0))
                throw new InvalidDataException("O parâmetro expires do Mocha é inválido.");
        }
    }

    static bool LooksLikeAmzDate(string value)
    {
        if (value == null || value.Length != 16 || value[8] != 'T' || value[15] != 'Z') return false;
        for (int i = 0; i < value.Length; i++)
        {
            if (i == 8 || i == 15) continue;
            if (value[i] < '0' || value[i] > '9') return false;
        }
        return true;
    }

    static string Query(Uri uri, string wanted)
    {
        string query = uri.Query;
        if (query.StartsWith("?", StringComparison.Ordinal)) query = query.Substring(1);
        foreach (string item in query.Split('&'))
        {
            if (item.Length == 0) continue;
            int eq = item.IndexOf('=');
            string rawKey = eq < 0 ? item : item.Substring(0, eq);
            string rawValue = eq < 0 ? "" : item.Substring(eq + 1);
            string key;
            try { key = Uri.UnescapeDataString(rawKey); }
            catch { continue; }
            if (!key.Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;
            try { return Uri.UnescapeDataString(rawValue); }
            catch { return ""; }
        }
        return "";
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
