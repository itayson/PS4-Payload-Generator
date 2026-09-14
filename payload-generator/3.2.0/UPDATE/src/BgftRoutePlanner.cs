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
    const int MinimumLifetimeSeconds = 90;

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
        ValidateExpiry(uri);

        if (!IsVikingR2(uri))
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

        // Worker v4/v5 compatibility path. This does not require KV and still
        // keeps the phone out of the PKG data path. It is safe only while the
        // resulting ELF URL fits the template's 2,047-byte field.
        string inline = workerBase + "/viking.pkg?u=" + Base64Url(Encoding.UTF8.GetBytes(exact));
        string registration = PayloadPatcher.RegistrationUrl(inline);
        if (Encoding.UTF8.GetByteCount(registration) > PayloadUrlLimit)
            throw new InvalidDataException(
                "A URL assinada do VikingFile é longa demais para o payload. Configure o binding KV ROUTES no Worker para gerar uma rota curta." +
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
            // 400/401/403/410 mean the Worker deliberately rejected this URL.
            // Never bypass those policy/expiry decisions with the inline fallback.
            if (status == 400 || status == 401 || status == 403 || status == 410)
                throw new InvalidDataException(string.IsNullOrWhiteSpace(text) ? "O Worker recusou a URL VikingFile (HTTP " + status + ")." : text.Trim());

            // 404/405/5xx mean the optional route API is absent/unavailable. Return
            // null instead of constructing HttpRequestException with an overload
            // that is not present in the Xamarin/MAUI reference set used by V3.
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

        // Keep query text if the Worker selected inline mode; RegistrationUrl
        // will append the local #tayson.pkg fragment later when needed.
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

    public static bool IsVikingR2(Uri uri)
    {
        if (uri == null || uri.Scheme != Uri.UriSchemeHttps) return false;

        // Avoid Regex API/version differences in the Xamarin/MAUI reference set.
        // Required form: vikingfile.<32 hex>.r2.cloudflarestorage.com
        string host = uri.IdnHost.TrimEnd('.');
        const string prefix = "vikingfile.";
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

    public static void ValidateExpiry(Uri uri)
    {
        if (IsVikingR2(uri))
        {
            string date = Query(uri, "X-Amz-Date");
            string secondsText = Query(uri, "X-Amz-Expires");
            if (!DateTimeOffset.TryParseExact(date, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset start) ||
                !long.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds) ||
                seconds <= 0 || seconds > 604800)
                throw new InvalidDataException("A assinatura temporária do VikingFile contém uma validade inválida.");
            RejectNearExpiry(start.AddSeconds(seconds), "VikingFile");
            return;
        }

        if (LinkPolicy.Domain(uri.IdnHost, "mocha.my"))
        {
            string expires = Query(uri, "expires");
            if (expires.Length > 0)
            {
                if (!long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out long unix) || unix <= 0)
                    throw new InvalidDataException("O parâmetro expires do Mocha é inválido.");
                DateTimeOffset end;
                try { end = DateTimeOffset.FromUnixTimeSeconds(unix); }
                catch (ArgumentOutOfRangeException) { throw new InvalidDataException("O parâmetro expires do Mocha é inválido."); }
                RejectNearExpiry(end, "Mocha");
            }
        }
    }

    static void RejectNearExpiry(DateTimeOffset expires, string provider)
    {
        if (expires <= DateTimeOffset.UtcNow.AddSeconds(MinimumLifetimeSeconds))
            throw new InvalidDataException("O link do " + provider + " expirou ou está perto de expirar. Gere um link novo no site antes de criar o payload.");
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
