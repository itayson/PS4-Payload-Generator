using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Tayson.Providers;

int passed=0;
void Check(bool yes,string message) { if(!yes)throw new Exception(message);passed++;Console.WriteLine("PASS "+message); }
async Task Reject(Func<Task> action,string name)
{
    try { await action(); } catch(Exception e) when(e is InvalidDataException || e is ArgumentException) { passed++;Console.WriteLine("PASS "+name);return; }
    throw new Exception("Unexpected success: "+name);
}
foreach(var item in new[]{("vikingfile.com",Provider.VikingFile),("download.vikingfile.com",Provider.VikingFile),
    ("akirabox.com",Provider.AkiraBox),("rootz.so",Provider.Rootz),("mocha.my",Provider.Mocha),
    ("mocha.my.attacker.example",Provider.Direct),("evilrootz.so",Provider.Direct)})
    Check(LinkPolicy.Detect(LinkPolicy.Parse("https://"+item.Item1+"/file"))==item.Item2,"Host "+item.Item1);
foreach(string bad in new[]{"file:///tmp/file.pkg","javascript:alert(1)","https://user:pass@example.test/a.pkg","https://e.test/a\r\nb","https://e.test/a#fragment"})
    await Reject(()=>Task.FromResult(LinkPolicy.Parse(bad)),"Reject malformed URL");
string signed="https://download.vikingfile.com/file.pkg?key=A%2fb%2B%3D&x=1&x=2+3";
Check(LinkPolicy.Parse(signed).AbsoluteUri==signed,"Signed query remains byte-for-byte intact");
Check(!LinkPolicy.Unsupported("https://example.test/mega.nz/file.pkg"),"Host policy ignores domain text in path");

foreach(string scenario in new[]{"valid","redirect","large"})
{
    var state=new Fixture(scenario);using var client=new DownloadClient(()=>new FakeHandler(state));
    var info=await client.ProbeAsync("https://files.example.test/file.pkg?token=A%2Fb%3D");
    Check(info.Length==(scenario=="large"?8589934592L:4096),scenario+" full file length");
    Check(info.ContentId==Fixture.Id,scenario+" reads real header offset");
    Check(state.BytesRead==129,scenario+" reads only 128 + 1 bytes");
    Check(info.Url.Query=="?token=A%2Fb%3D",scenario+" query preserved");
    Check(state.Requests.All(r=>!r.HasCredentials),scenario+" requests have no cookies or referer");
}
foreach(string scenario in new[]{"html","json","badmagic","missingid","forbidden","notfound","ignore-range","wrong-range","wrong-seek","changed-size","short-read","compression","redirect-loop","bad-total","overflow-total"})
{
    using var client=new DownloadClient(()=>new FakeHandler(new Fixture(scenario)));
    await Reject(async()=>{await client.ProbeAsync("https://files.example.test/a.pkg");},scenario);
}
{
    var state=new Fixture("valid");using var client=new DownloadClient(()=>new FakeHandler(state));
    var info=await client.ProbeAsync("https://files.example.test/a.pkg",2097152);
    Check(info.Prefix.Length==4096&&state.BytesRead==4097,"Metadata read bounded by file length");
}
byte[] template=File.ReadAllBytes(args[0]);
byte[] result=PayloadPatcher.Patch(template,signed,Fixture.Id,"Teste ação 🎮", "PS4GD",8589934592L);
Check(result.Length==template.Length,"ELF size preserved");
Check(Encoding.UTF8.GetString(result).Contains(signed+"#tayson.pkg"+'\0'),"ELF embeds full signed URL, compatibility fragment and terminator");
string gameLoaded = Encoding.ASCII.GetString(result, 0, Math.Min(result.Length, 0x18000));
Check(gameLoaded.Contains("sceBgftServiceIntDownloadRegisterTask"+'\0') &&
      !gameLoaded.Contains("sceBgftServiceIntDebugDownloadRegisterPkg"+'\0'),
      "Base game ELF uses normal BGFT register API");
byte[] patchElf = PayloadPatcher.Patch(template,signed,Fixture.Id,"Update","PS4PATCH",4096);
string patchLoaded = Encoding.ASCII.GetString(patchElf, 0, Math.Min(patchElf.Length, 0x18000));
Check(patchLoaded.Contains("sceBgftServiceIntDebugDownloadRegisterPkg"+'\0'),
      "Update ELF preserves debug BGFT register API");
Check(template.SequenceEqual(File.ReadAllBytes(args[0])),"Original template remains unchanged");
int urlOffset=Encoding.ASCII.GetString(template).IndexOf("###URL_PLACEHOLDER###",StringComparison.Ordinal);
string exact="https://example.test/"+new string('a',2047-"https://example.test/".Length-4)+".pkg";
byte[] boundary=PayloadPatcher.Patch(template,exact,Fixture.Id,"Title","PS4GD",4096);
Check(boundary[urlOffset+2047]==0,"2047-byte URL fits with terminator");
await Reject(()=>Task.FromResult(PayloadPatcher.Patch(template,exact+"a",Fixture.Id,"Title","PS4GD",4096)),"2048-byte URL rejected, never truncated");
byte[] broken=(byte[])template.Clone();broken[urlOffset]=0;
await Reject(()=>Task.FromResult(PayloadPatcher.Patch(broken,signed,Fixture.Id,"Title","PS4GD",4096)),"Missing marker rejected");
byte[] longName=PayloadPatcher.Patch(template,signed,Fixture.Id,string.Concat(Enumerable.Repeat("🎮á",60)),"PS4GD",4096);
int nameOffset=Encoding.ASCII.GetString(template).IndexOf("###NAME_PLACEHOLDER###",StringComparison.Ordinal);
int nameEnd=Array.IndexOf(longName,(byte)0,nameOffset,128);
Check(nameEnd>nameOffset&&nameEnd<nameOffset+128,"Long UTF-8 title is terminated");
new UTF8Encoding(false,true).GetString(longName,nameOffset,nameEnd-nameOffset);
Check(true,"Long title retains valid UTF-8");
foreach (string opaque in new[] {
    "https://vikingfile.com/opaque/abc123?token=A%2fb%2B%3D&x=1&x=2+3",
    "https://akirabox.com/download/abc?sig=123",
    "https://rootz.so/d/abc", "https://mocha.my/d/abc",
    "https://cdn.example.test/abc?filename=test.pkg&token=XYZ",
    "https://cdn.example.test/abc%23def?sig=%23%2F" })
{
    string registration = PayloadPatcher.RegistrationUrl(opaque);
    Check(registration == opaque + "#tayson.pkg", "Opaque URL gets local package fragment");
    Check(new Uri(registration).GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped) == opaque,
        "HTTP request target preserves signed path and query");
    byte[] patched = PayloadPatcher.Patch(template, opaque, Fixture.Id, "Title", "PS4GD", 4096);
    int end = Array.IndexOf(patched, (byte)0, urlOffset, 2048);
    Check(Encoding.UTF8.GetString(patched, urlOffset, end-urlOffset) == registration,
        "Generated ELF contains complete compatibility URL");
}
Check(PayloadPatcher.RegistrationUrl("https://cdn.example.test/test.PKG?sig=A%2fb") ==
    "https://cdn.example.test/test.PKG?sig=A%2fb#tayson.pkg", "Query-signed PKG gets local compatibility fragment");
Check(PayloadPatcher.RegistrationUrl("https://cdn.example.test/test.PKG") ==
    "https://cdn.example.test/test.PKG", "Plain direct PKG stays unchanged");
Check(PayloadPatcher.RegistrationUrl("https://cdn.mocha.my/game.pkg?token=A_B&expires=4070908800&node=sugar") ==
    "https://cdn.mocha.my/game.pkg?token=A_B&expires=4070908800&node=sugar#tayson.pkg",
    "Mocha keeps authenticated query and gains terminal PKG fragment");
string maxOpaque = "https://example.test/" + new string('a', 2047-"#tayson.pkg".Length-"https://example.test/".Length);
Check(Encoding.UTF8.GetByteCount(PayloadPatcher.RegistrationUrl(maxOpaque)) == 2047,
    "Opaque URL capacity includes fragment and null terminator");
await Reject(()=>Task.FromResult(PayloadPatcher.Patch(template,maxOpaque+"a",Fixture.Id,"Title","PS4GD",4096)),
    "Reject opaque URL when compatibility suffix would overflow");

string r2 = "https://vikingfile.04b3d96d52475741e6b10f97f0a84a16.r2.cloudflarestorage.com/object" +
    "?X-Amz-Content-Sha256=UNSIGNED-PAYLOAD&X-Amz-Algorithm=AWS4-HMAC-SHA256" +
    "&X-Amz-Credential=test%2F20990101%2Fauto%2Fs3%2Faws4_request" +
    "&X-Amz-Date=20990101T000000Z&X-Amz-SignedHeaders=host&X-Amz-Expires=21600" +
    "&X-Amz-Signature=" + new string('a',64);
Check(BgftRoutePlanner.IsVikingR2(LinkPolicy.Parse(r2)), "Recognize final VikingFile R2 presigned URL");
{
    var routeHandler = new RouteHandler(HttpStatusCode.OK, "https://drive.itaysondaniel.workers.dev/d/abc123.pkg\n");
    using var router = new BgftRoutePlanner(()=>routeHandler);
    string route = await router.PrepareAsync(r2);
    Check(route == "https://drive.itaysondaniel.workers.dev/d/abc123.pkg", "VikingFile uses short Worker route");
    Check(routeHandler.PostedBody == r2, "Worker receives exact signed R2 URL without reserialization");
}
{
    var routeHandler = new RouteHandler(HttpStatusCode.NotFound, "missing");
    using var router = new BgftRoutePlanner(()=>routeHandler);
    string route = await router.PrepareAsync(r2);
    Check(route.StartsWith("https://drive.itaysondaniel.workers.dev/viking.pkg?u=", StringComparison.Ordinal),
        "Worker registration failure falls back to inline route");
    Check(PayloadPatcher.RegistrationUrl(route).EndsWith("#tayson.pkg", StringComparison.Ordinal),
        "Inline Viking route is BGFT-compatible");
}
await Reject(async()=>
{
    using var router = new BgftRoutePlanner(()=>new RouteHandler(HttpStatusCode.Forbidden, "policy rejected"));
    await router.PrepareAsync(r2);
}, "Worker policy rejection is not bypassed by inline fallback");
{
    using var router = new BgftRoutePlanner(()=>new RouteHandler(HttpStatusCode.OK,"unused"));
    string direct = "https://cdn.example.test/game.pkg";
    Check(await router.PrepareAsync(direct) == direct, "Ordinary direct PKG bypasses Worker");
    string mocha = "https://cdn.mocha.my/game.pkg?token=abc&expires=4070908800&node=sugar";
    Check(await router.PrepareAsync(mocha) == mocha, "Mocha transport stays direct before ELF fragment patch");
}
string badDateR2 = r2.Replace("20990101T000000Z", "2099-01-01T00Z");
await Reject(async()=>{using var router=new BgftRoutePlanner(()=>new RouteHandler(HttpStatusCode.OK,"unused"));await router.PrepareAsync(badDateR2);},
    "Malformed VikingFile date rejected before payload generation");
string badTtlR2 = r2.Replace("X-Amz-Expires=21600", "X-Amz-Expires=0");
await Reject(async()=>{using var router=new BgftRoutePlanner(()=>new RouteHandler(HttpStatusCode.OK,"unused"));await router.PrepareAsync(badTtlR2);},
    "Invalid VikingFile TTL rejected before payload generation");
await Reject(async()=>{using var router=new BgftRoutePlanner(()=>new RouteHandler(HttpStatusCode.OK,"unused"));await router.PrepareAsync("https://cdn.mocha.my/game.pkg?token=a&expires=not-a-number&node=sugar");},
    "Malformed Mocha expiry rejected before payload generation");

// 3.4.0: validate PS4 PKG artwork-table parsing. The header/table are
// big-endian; icon0/pic0/pic1 point to optional small range reads.
{
    byte[] prefix = new byte[0x100];
    void Be32(int offset, uint value)
    {
        prefix[offset] = (byte)(value >> 24);
        prefix[offset + 1] = (byte)(value >> 16);
        prefix[offset + 2] = (byte)(value >> 8);
        prefix[offset + 3] = (byte)value;
    }
    Be32(0x10, 3);
    Be32(0x18, 0x40);
    void Entry(int p, uint id, uint offset, uint size)
    {
        Be32(p, id); Be32(p + 0x10, offset); Be32(p + 0x14, size);
    }
    byte[] FakePng(byte marker)
    {
        byte[] data = new byte[64];
        byte[] sig = { 137,80,78,71,13,10,26,10 };
        Array.Copy(sig, data, sig.Length);
        data[8] = marker;
        return data;
    }
    byte[] icon0 = FakePng(1), pic0 = FakePng(2), pic1 = FakePng(3);
    Entry(0x40, 0x1200, 0x300, (uint)icon0.Length);
    Entry(0x60, 0x1220, 0x400, (uint)pic0.Length);
    Entry(0x80, 0x1006, 0x500, (uint)pic1.Length);
    byte[] Range(long offset) => offset == 0x300 ? icon0 : offset == 0x400 ? pic0 : pic1;
    byte[][] gallery = await UiArtworkStore.ExtractExtendedAsync(prefix, (offset, size) => Task.FromResult(Range(offset)));
    Check(gallery.Length == 3, "Artwork table loads icon0 pic0 pic1");
    Check(gallery[0][8] == 1 && gallery[1][8] == 2 && gallery[2][8] == 3, "Artwork gallery preserves preferred PS4 image order");
}

Console.WriteLine($"RESULT: {passed} checks passed");

sealed class Fixture(string mode)
{
    public const string Id="EP0000-CUSA00001_00-TEST000000000000";
    public string Mode=mode;
    public long BytesRead;
    public List<(string Url,bool HasCredentials)> Requests=new();
}
sealed class FakeHandler(Fixture state):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        state.Requests.Add((request.RequestUri.AbsoluteUri,request.Headers.Contains("Cookie")||request.Headers.Contains("Referer")||request.Headers.Contains("Authorization")));
        string mode=state.Mode;var wanted=request.Headers.Range.Ranges.Single();long from=wanted.From.Value;
        long size=mode=="large"?8589934592L:4096;long to=Math.Min(wanted.To.Value,size-1);
        if(mode=="redirect-loop"||(mode=="redirect"&&!request.RequestUri.AbsolutePath.StartsWith("/final")))
        {
            var redirect=new HttpResponseMessage(HttpStatusCode.Found){RequestMessage=request};
            redirect.Headers.Location=new Uri((mode=="redirect-loop"?"/a.pkg":"/final.pkg")+request.RequestUri.Query,UriKind.Relative);
            redirect.Headers.TryAddWithoutValidation("Set-Cookie","secret=do-not-forward");return Task.FromResult(redirect);
        }
        var response=new HttpResponseMessage(HttpStatusCode.PartialContent){RequestMessage=request};
        if(mode=="forbidden")response.StatusCode=HttpStatusCode.Forbidden;
        if(mode=="notfound")response.StatusCode=HttpStatusCode.NotFound;
        if(mode=="ignore-range"){response.StatusCode=HttpStatusCode.OK;from=0;to=size-1;}
        byte[] bytes=new byte[(int)(to-from+1)];
        byte[] header=new byte[128];header[0]=0x7f;header[1]=0x43;header[2]=0x4e;header[3]=0x54;
        Encoding.ASCII.GetBytes(Fixture.Id).CopyTo(header,64);header[127]=77;
        for(int i=0;i<bytes.Length;i++)if(from+i<header.Length)bytes[i]=header[(int)from+i];
        if(mode=="badmagic")bytes[0]=0;
        if(mode=="missingid"&&from==0)bytes[64]=0;
        if(mode=="wrong-seek"&&from==127)bytes[0]=10;
        if(mode=="short-read")Array.Resize(ref bytes,Math.Max(0,bytes.Length-1));
        response.Content=new StreamContent(new CountedStream(bytes,n=>state.BytesRead+=n));
        response.Content.Headers.ContentType=new MediaTypeHeaderValue(mode=="html"?"text/html":mode=="json"?"application/json":"application/octet-stream");
        response.Content.Headers.ContentLength=bytes.Length;
        if(mode=="compression")response.Content.Headers.ContentEncoding.Add("gzip");
        string total=mode=="bad-total"?"unknown":mode=="overflow-total"?"999999999999999999999999999999":(mode=="changed-size"&&from==127?size+1:size).ToString();
        if(response.StatusCode==HttpStatusCode.PartialContent)response.Content.Headers.TryAddWithoutValidation("Content-Range",$"bytes {(mode=="wrong-range"?1:from)}-{to}/{total}");
        response.Headers.TryAddWithoutValidation("Set-Cookie","unused=1");
        return Task.FromResult(response);
    }
}
sealed class CountedStream(byte[] bytes,Action<int> count):MemoryStream(bytes)
{
    public override Task<int> ReadAsync(byte[] buffer,int offset,int size,CancellationToken token)
    {int n=Read(buffer,offset,size);count(n);return Task.FromResult(n);}
}

sealed class RouteHandler(HttpStatusCode status,string reply):HttpMessageHandler
{
    public string PostedBody="";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        if(request.Content!=null) PostedBody=await request.Content.ReadAsStringAsync(token);
        return new HttpResponseMessage(status){RequestMessage=request,Content=new StringContent(reply)};
    }
}
