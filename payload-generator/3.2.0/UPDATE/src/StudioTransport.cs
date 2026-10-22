using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tayson.Providers;

public sealed class StudioTransportException : IOException
{
    public string Code { get; }
    public int HttpStatus { get; }
    public StudioTransportException(string code, string message, int status = 0) : base(message)
    { Code = code; HttpStatus = status; }
}

public sealed class StudioPreparedPackage
{
    public string Route { get; }
    public DownloadInfo File { get; }
    public StudioPreparedPackage(string route, DownloadInfo file) { Route = route; File = file; }
}

/// <summary>Network operations are explicit, cancellable and never send a payload.</summary>
public sealed class StudioTransport
{
    readonly Func<HttpMessageHandler> factory;
    public StudioTransport() : this(() => new HttpClientHandler { AllowAutoRedirect = false }) { }
    public StudioTransport(Func<HttpMessageHandler> handlerFactory)
    { factory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory)); }

    public static string WorkerBase(string input)
    {
        string value = (input ?? "").Trim();
        if (value.Length > 2047 || !Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
            uri.Scheme != "https" || string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new StudioTransportException("WORKER_CONFIG", "Use a URL HTTPS completa do Worker, sem query, fragmento ou usuário/senha.");
        foreach (char c in value)
            if (char.IsControl(c)) throw new StudioTransportException("WORKER_CONFIG", "A configuração do Worker contém caracteres de controle.");
        return uri.AbsoluteUri.TrimEnd('/');
    }

    public static string WorkerHost(string input)
    {
        try { return new Uri(WorkerBase(input)).Host; }
        catch (StudioTransportException) { return "Worker não configurado"; }
    }

    static async Task<string> ReadTextAsync(HttpContent content, int maximum, CancellationToken token)
    {
        if (content.Headers.ContentLength.HasValue && content.Headers.ContentLength.Value > maximum)
            throw new StudioTransportException("REPLY_LIMIT", "O Worker enviou uma resposta maior que o limite permitido.");
        using Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        byte[] bytes = new byte[maximum + 1];
        int length = 0;
        while (length < bytes.Length)
        {
            int count = await stream.ReadAsync(bytes, length, bytes.Length - length, token).ConfigureAwait(false);
            if (count == 0) break;
            length += count;
        }
        if (length > maximum)
            throw new StudioTransportException("REPLY_LIMIT", "A resposta do Worker excede 4096 bytes.");
        return Encoding.UTF8.GetString(bytes, 0, length).Trim();
    }

    static StudioTransportException HttpError(int status)
    {
        string message;
        switch (status)
        {
            case 401: case 403: message = "Acesso recusado. Confira a URL privada do aplicativo e a autorização do arquivo."; break;
            case 404: case 405: message = "Este Worker não oferece /api/link. Configure o Tayson Universal Proxy com a base privada /app/… ."; break;
            case 410: message = "A sessão ou autorização expirou. Gere uma nova rota a partir do link autorizado."; break;
            case 429: message = "Limite de requisições atingido. Aguarde antes de tentar novamente."; break;
            case 502: case 503: case 504: message = "O Worker ou a origem está indisponível. Nenhum payload foi enviado."; break;
            default: message = "O Worker recusou a solicitação. Nenhum payload foi enviado."; break;
        }
        // Never echo an upstream response body, signed URL, cookie or private base path.
        return new StudioTransportException("HTTP_" + status, message, status);
    }

    public async Task<int> CheckWorkerAsync(string configuredWorker, CancellationToken token)
    {
        string worker = WorkerBase(configuredWorker);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var http = new HttpClient(factory()) { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, worker + "/health");
        using HttpResponseMessage response = await http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw HttpError((int)response.StatusCode);
        // Bounded read also checks that the endpoint can finish its response.
        await ReadTextAsync(response.Content, 4096, timeout.Token).ConfigureAwait(false);
        return (int)response.StatusCode;
    }

    public async Task<string> RegisterAsync(string configuredWorker, string original, CancellationToken token)
    {
        string worker = WorkerBase(configuredWorker);
        Uri source = LinkPolicy.Parse(original);
        if (source.Scheme != "https")
            throw new StudioTransportException("SOURCE_SCHEME", "O resolvedor do Worker exige uma origem HTTPS.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var http = new HttpClient(factory()) { Timeout = TimeSpan.FromSeconds(15) };
        using var request = new HttpRequestMessage(HttpMethod.Post, worker + "/api/link");
        request.Content = new StringContent(original.Trim(), Encoding.UTF8, "text/plain");
        using HttpResponseMessage response = await http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw HttpError((int)response.StatusCode);
        string route = await ReadTextAsync(response.Content, 4096, timeout.Token).ConfigureAwait(false);
        Uri configured = new Uri(worker);
        if (!Uri.TryCreate(route, UriKind.Absolute, out Uri result) || result.Scheme != "https" ||
            !result.Host.Equals(configured.Host, StringComparison.OrdinalIgnoreCase) || result.Port != configured.Port ||
            !string.IsNullOrEmpty(result.UserInfo) || !string.IsNullOrEmpty(result.Fragment) ||
            !result.AbsolutePath.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
            throw new StudioTransportException("INVALID_ROUTE", "A resposta deve ser uma URL HTTPS .pkg da mesma origem do Worker.");
        if (Encoding.UTF8.GetByteCount(route) > 2047)
            throw new StudioTransportException("ROUTE_LIMIT", "A rota excede os 2047 bytes aceitos pelo payload.");
        return route;
    }

    public async Task<StudioPreparedPackage> PrepareAsync(string worker, string original, CancellationToken token)
    {
        string route = await RegisterAsync(worker, original, token).ConfigureAwait(false);
        // Reuse the 3.4.3 byte/range/PKG parser instead of inventing another parser.
        using var client = new DownloadClient(factory);
        DownloadInfo info = await client.ProbeAsync(route, 128, token).ConfigureAwait(false);
        Uri registered = new Uri(route);
        if (!info.Url.Host.Equals(registered.Host, StringComparison.OrdinalIgnoreCase) ||
            info.Url.Scheme != registered.Scheme || info.Url.Port != registered.Port)
            throw new StudioTransportException("EXTERNAL_REDIRECT", "A rota do Worker redirecionou para outra origem.");
        return new StudioPreparedPackage(route, info);
    }

    public static string SafeError(Exception error)
    {
        if (error is StudioTransportException known) return known.Code + ": " + known.Message;
        if (error is OperationCanceledException) return "Operação cancelada ou prazo de resposta excedido. Nenhum payload foi enviado por este diagnóstico.";
        if (error is InvalidDataException) return "PKG_INVALID: a origem não passou na validação de PKG, tamanho ou HTTP Range.";
        return "NETWORK_ERROR: falha de conexão ou resposta interrompida. Confira a rede e tente novamente.";
    }
}
