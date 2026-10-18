using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace Tayson.Providers;

/// <summary>Opt-in Android SecureStorage credential for the isolated staging Worker.</summary>
public static class StagingRouteAuth
{
    const string Host = "tayson-transport-v12-staging.itayson.workers.dev";
    const string StorageKey = "tayson.staging.route_api_key.v1";

    public static bool IsStagingWorker(string workerBase)
    {
        if (!Uri.TryCreate(workerBase, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 &&
            uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 &&
            uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
            uri.IdnHost.Equals(Host, StringComparison.OrdinalIgnoreCase);
    }

    static bool Valid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 512) return false;
        foreach (char ch in value) if (ch < '!' || ch > '~') return false;
        return true;
    }

    public static async Task SaveAsync(string value)
    {
        if (!Valid(value)) throw new InvalidDataException("Chave de homologação inválida.");
        await SecureStorage.Default.SetAsync(StorageKey, value).ConfigureAwait(false);
    }

    public static async Task<bool> HasCredentialAsync()
    {
        try { return Valid(await SecureStorage.Default.GetAsync(StorageKey).ConfigureAwait(false)); }
        catch { return false; }
    }

    public static void Clear() => SecureStorage.Default.Remove(StorageKey);

    public static async Task AttachAsync(HttpRequestMessage request)
    {
        Uri uri = request?.RequestUri;
        if (uri == null || request.Method != HttpMethod.Post ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !uri.IdnHost.Equals(Host, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.AbsolutePath != "/api/routes" && uri.AbsolutePath != "/api/akira-routes" &&
             uri.AbsolutePath != "/api/mocha-routes" && uri.AbsolutePath != "/api/filek-routes" &&
             uri.AbsolutePath != "/api/viking-routes"))
            throw new InvalidOperationException("Destino de homologação não autorizado.");

        string value;
        try { value = await SecureStorage.Default.GetAsync(StorageKey).ConfigureAwait(false); }
        catch { throw new InvalidOperationException("Chave indisponível no armazenamento seguro."); }
        if (!Valid(value)) throw new InvalidOperationException("Configure a chave em Ajustes.");
        if (!request.Headers.TryAddWithoutValidation("x-api-key", value))
            throw new InvalidOperationException("Falha ao autenticar solicitação de homologação.");
    }
}
