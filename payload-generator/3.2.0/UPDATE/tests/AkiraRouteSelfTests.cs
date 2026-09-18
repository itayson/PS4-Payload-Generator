using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using Tayson.Providers;

internal static class AkiraRouteSelfTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const string akira =
            "https://download.akirabox.com/test.pkg?access=4070908800000.ABCDEFGHIJKLMNOP";

        var ok = new FakeRouteHandler(
            HttpStatusCode.OK,
            "https://drive.itaysondaniel.workers.dev/a/0123456789abcdef0123456789abcdef.pkg\n");
        using (var router = new BgftRoutePlanner(() => ok))
        {
            string route = router.PrepareAsync(
                akira,
                "session=abc",
                "https://akirabox.to/",
                "Mozilla/5.0").GetAwaiter().GetResult();

            if (!route.Contains("/a/0123456789abcdef0123456789abcdef.pkg", StringComparison.Ordinal))
                throw new Exception("Akira KV route was not accepted");
        }

        var fallback = new FakeRouteHandler(
            HttpStatusCode.OK,
            "https://drive.itaysondaniel.workers.dev/akira.pkg?u=opaque\n");
        using (var router = new BgftRoutePlanner(() => fallback))
        {
            try
            {
                router.PrepareAsync(
                    akira,
                    "session=abc",
                    "https://akirabox.to/",
                    "Mozilla/5.0").GetAwaiter().GetResult();
            }
            catch (InvalidDataException)
            {
                Console.WriteLine("PASS AkiraBox requires KV-backed Worker route");
                return;
            }
        }

        throw new Exception("AkiraBox URL-only fallback should be rejected");
    }

    sealed class FakeRouteHandler(HttpStatusCode status, string reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            return Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(reply, Encoding.UTF8, "text/plain")
            });
        }
    }
}
