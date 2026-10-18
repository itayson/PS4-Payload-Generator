using System.Net;
using System.Net.Http;
using Tayson.Providers;

static class Program
{
    static int passed;
    static void Check(bool test,string label)
    {
        if(!test) throw new Exception("FAILED: "+label);
        passed++; Console.WriteLine("PASS "+label);
    }
    static async Task Throws<T>(Func<Task> action,string label) where T:Exception
    {
        try { await action(); } catch(T) { Check(true,label);return; }
        throw new Exception("Expected "+typeof(T).Name+": "+label);
    }
    static HttpRequestMessage Req(string path,string host=StagingRouteAuth.Host,HttpMethod? method=null)
        => new(method??HttpMethod.Post,"https://"+host+path);

    sealed class Handler:HttpMessageHandler
    {
        public int Sends;
        public bool HasKey;
        public string? Key;
        public HttpStatusCode Status=HttpStatusCode.OK;
        public string Reply="https://"+StagingRouteAuth.Host+"/f/0123456789abcdef0123456789abcdef.pkg";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            Sends++;
            HasKey=request.Headers.TryGetValues("x-api-key",out var values);
            Key=HasKey?string.Join("",values!):null;
            return Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(Status==HttpStatusCode.OK?Reply:"rejected")});
        }
    }
    static async Task Main()
    {
        StagingRouteAuth.Clear();
        Check(!await StagingRouteAuth.IsConfiguredAsync(),"no embedded key");
        await Throws<InvalidDataException>(()=>StagingRouteAuth.SaveAsync(""),"empty key rejected");
        await Throws<InvalidDataException>(()=>StagingRouteAuth.SaveAsync("has space"),"whitespace rejected");
        await Throws<InvalidDataException>(()=>StagingRouteAuth.SaveAsync("bad\r\nheader"),"header injection rejected");
        await Throws<InvalidOperationException>(()=>StagingRouteAuth.AttachAsync(Req("/api/routes")),"missing credential blocks request");
        await StagingRouteAuth.SaveAsync("NONPRODUCTION-FAKE-KEY");
        Check(await StagingRouteAuth.IsConfiguredAsync(),"test credential available");
        foreach(var path in new[]{"/api/routes","/api/akira-routes","/api/mocha-routes","/api/filek-routes","/api/viking-routes","/api/v12/auth-check"})
        {
            using var req=Req(path);
            await StagingRouteAuth.AttachAsync(req);
            Check(req.Headers.Contains("x-api-key"),"allow "+path);
        }
        foreach(var path in new[]{"/api/foo-routes","/api/health","/api/routes?key=bad","/health"})
            await Throws<InvalidOperationException>(()=>StagingRouteAuth.AttachAsync(Req(path)),"reject "+path);
        await Throws<InvalidOperationException>(()=>StagingRouteAuth.AttachAsync(Req("/api/routes","evil.example")),"reject foreign host");
        await Throws<InvalidOperationException>(()=>StagingRouteAuth.AttachAsync(Req("/api/routes",StagingRouteAuth.Host,HttpMethod.Get)),"reject GET");
        Check(StagingRouteAuth.IsStagingWorker("https://"+StagingRouteAuth.Host),"exact host");
        Check(!StagingRouteAuth.IsStagingWorker("https://"+StagingRouteAuth.Host+".evil.example"),"reject suffix");
        Check(!StagingRouteAuth.IsStagingWorker("http://"+StagingRouteAuth.Host),"reject cleartext");
        var sender=new Handler();
        using(var planner=new BgftRoutePlanner(()=>sender,"https://"+StagingRouteAuth.Host,StagingRouteAuth.AttachAsync))
        {
            var route=await planner.PrepareAsync("https://filekeeper.net/fixture.pkg",Provider.FileKeeper);
            Check(route==sender.Reply,"staging short route");
            Check(sender.Sends==1 && sender.Key=="NONPRODUCTION-FAKE-KEY","staging sends header");
        }
        foreach(var code in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden})
        {
            var denied=new Handler{Status=code};
            using var planner=new BgftRoutePlanner(()=>denied,"https://"+StagingRouteAuth.Host,StagingRouteAuth.AttachAsync);
            await Throws<InvalidDataException>(()=>planner.PrepareAsync("https://filekeeper.net/fixture.pkg",Provider.FileKeeper),"reject staging HTTP "+(int)code);
        }
        var unavailable=new Handler{Status=HttpStatusCode.ServiceUnavailable};
        using(var planner=new BgftRoutePlanner(()=>unavailable,"https://"+StagingRouteAuth.Host,StagingRouteAuth.AttachAsync))
            await Throws<InvalidDataException>(()=>planner.PrepareAsync("https://filekeeper.net/fixture.pkg",Provider.FileKeeper),"no staging fallback on HTTP 503");
        var prod=new Handler{Reply="https://drive.itaysondaniel.workers.dev/f/0123456789abcdef0123456789abcdef.pkg"};
        using(var planner=new BgftRoutePlanner(()=>prod,"https://drive.itaysondaniel.workers.dev"))
        {
            var route=await planner.PrepareAsync("https://filekeeper.net/fixture.pkg",Provider.FileKeeper);
            Check(route==prod.Reply && prod.Sends==1 && !prod.HasKey,"production unchanged; no auth header");
        }
        StagingRouteAuth.Clear();
        var missing=new Handler();
        using(var planner=new BgftRoutePlanner(()=>missing,"https://"+StagingRouteAuth.Host,StagingRouteAuth.AttachAsync))
        {
            await Throws<InvalidOperationException>(()=>planner.PrepareAsync("https://filekeeper.net/fixture.pkg",Provider.FileKeeper),"staging fails closed without key");
            Check(missing.Sends==0,"zero network requests without key");
        }
        Console.WriteLine($"RESULT: {passed} staging auth checks passed");
    }
}
