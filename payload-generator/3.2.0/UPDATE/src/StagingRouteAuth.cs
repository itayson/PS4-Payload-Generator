using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Tayson.Providers;

/// <summary>
/// Isolated staging-only route credential. Session memory ONLY.
/// Nothing is written to the APK, Android Preferences, history, URL, ELF, or log.
/// Re-enter the credential in Settings after each application restart.
/// </summary>
public static class StagingRouteAuth
{
    public const string Host = "tayson-transport-v12-staging.itayson.workers.dev";
    static readonly object Gate = new object();
    static string currentCredential = "";

    public static bool IsStagingWorker(string workerBase)
    {
        if (!Uri.TryCreate(workerBase, UriKind.Absolute, out Uri uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 &&
            uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 &&
            uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
            uri.IdnHost.Equals(Host, StringComparison.OrdinalIgnoreCase);
    }

    static bool Valid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 512) return false;
        foreach (char c in value) if (c < '!' || c > '~') return false;
        return true;
    }

    public static Task SaveAsync(string credential)
    {
        if (!Valid(credential))
            throw new InvalidDataException("Chave de homologação inválida.");
        lock (Gate) currentCredential = credential;
        return Task.CompletedTask;
    }

    public static Task<bool> IsConfiguredAsync()
    {
        lock (Gate) return Task.FromResult(Valid(currentCredential));
    }

    public static void Clear()
    {
        lock (Gate) currentCredential = "";
    }

    public static Task AttachAsync(HttpRequestMessage request)
    {
        Uri uri = request == null ? null : request.RequestUri;
        if (uri == null || request.Method != HttpMethod.Post ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !uri.IdnHost.Equals(Host, StringComparison.OrdinalIgnoreCase) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.AbsolutePath != "/api/routes" && uri.AbsolutePath != "/api/akira-routes" &&
             uri.AbsolutePath != "/api/mocha-routes" && uri.AbsolutePath != "/api/filek-routes" &&
             uri.AbsolutePath != "/api/viking-routes"))
            throw new InvalidOperationException("Destino de homologação não autorizado.");
        string secret;
        lock (Gate) secret = currentCredential;
        if (!Valid(secret))
            throw new InvalidOperationException("Informe a chave em Ajustes para esta sessão do aplicativo.");
        if (!request.Headers.TryAddWithoutValidation("x-api-key", new[] { secret }))
            throw new InvalidOperationException("Falha ao autenticar o registro de rota.");
        return Task.CompletedTask;
    }
}
