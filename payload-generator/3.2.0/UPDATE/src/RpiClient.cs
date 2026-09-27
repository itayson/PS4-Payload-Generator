using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Tayson.Providers;

/// <summary>
/// Minimal client for flatz Remote Package Installer.
/// It deliberately performs a single POST per user action: retrying a request
/// after the PS4 has accepted it could create a duplicate BGFT task.
/// </summary>
public static class RpiClient
{
    public const int Port = 12800;
    const int ReplyLimit = 64 * 1024;

    public static async Task<RpiInstallResult> InstallAsync(
        string ps4Ip,
        string packageUrl,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(ps4Ip))
            throw new ArgumentException("IP do PS4 não informado.", nameof(ps4Ip));
        if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out Uri package) ||
            (package.Scheme != Uri.UriSchemeHttp && package.Scheme != Uri.UriSchemeHttps))
            throw new InvalidDataException("A rota final do PKG não é HTTP/HTTPS.");

        string endpoint = "http://" + ps4Ip.Trim() + ":" + Port + "/api/install";
        string json = "{\"type\":\"direct\",\"packages\":[\"" +
            EscapeJson(package.AbsoluteUri) + "\"]}";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        };
        using var http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("O Remote Package Installer não respondeu na porta 12800.");
        }

        using (response)
        {
            string body = await ReadBoundedAsync(
                response.Content,
                ReplyLimit,
                timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string detail = string.IsNullOrWhiteSpace(body)
                    ? "HTTP " + (int)response.StatusCode
                    : body.Trim();
                throw new InvalidDataException("RPI recusou a instalação: " + detail);
            }

            int taskId = ParseTaskId(body);
            if (taskId < 0)
                throw new InvalidDataException(
                    "RPI respondeu sem um task_id BGFT válido: " +
                    (string.IsNullOrWhiteSpace(body) ? "(resposta vazia)" : body.Trim()));

            return new RpiInstallResult(taskId, body);
        }
    }

    static int ParseTaskId(string body)
    {
        Match match = Regex.Match(
            body ?? "",
            "\"task_id\"\\s*:\\s*(-?\\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out int id) ? id : -1;
    }

    static string EscapeJson(string value)
    {
        var b = new StringBuilder(value?.Length + 16 ?? 16);
        foreach (char c in value ?? "")
        {
            switch (c)
            {
                case '\\': b.Append("\\\\"); break;
                case '"': b.Append("\\\""); break;
                case '\b': b.Append("\\b"); break;
                case '\f': b.Append("\\f"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                    break;
            }
        }
        return b.ToString();
    }

    static async Task<string> ReadBoundedAsync(
        HttpContent content,
        int limit,
        CancellationToken token)
    {
        using Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        byte[] buffer = new byte[limit + 1];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer,
                total,
                buffer.Length - total,
                token).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        if (total > limit)
            throw new InvalidDataException("Resposta do RPI excedeu o limite permitido.");
        return Encoding.UTF8.GetString(buffer, 0, total);
    }
}

public sealed class RpiInstallResult
{
    public int TaskId { get; }
    public string ResponseText { get; }

    public RpiInstallResult(int taskId, string responseText)
    {
        TaskId = taskId;
        ResponseText = responseText ?? "";
    }
}
