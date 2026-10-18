using System;
using System.Net.Http;
using System.Threading.Tasks;
namespace Tayson.Providers;
// Test-only shim. The production build uses ../src/StagingRouteAuth.cs instead.
public static class StagingRouteAuth
{
    public const string Host = "tayson-transport-v12-staging.itayson.workers.dev";
    public static bool IsStagingWorker(string workerBase) =>
        Uri.TryCreate(workerBase,UriKind.Absolute,out var uri) &&
        uri.Scheme==Uri.UriSchemeHttps && uri.Port==443 &&
        uri.AbsolutePath=="/" && uri.Query.Length==0 && uri.UserInfo.Length==0 &&
        uri.Fragment.Length==0 &&
        uri.IdnHost.Equals(Host,StringComparison.OrdinalIgnoreCase);
    public static Task AttachAsync(HttpRequestMessage request) =>
        Task.FromException(new InvalidOperationException("No staging key in legacy tests."));
}
