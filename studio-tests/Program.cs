using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Tayson.Providers;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}
async Task Reject(Func<Task> action, string code)
{
    try { await action(); }
    catch (StudioTransportException error) { Check(error.Code == code, code); return; }
    throw new Exception("Expected " + code);
}
const string worker = "https://proxy.example/app/private-key";
const string source = "https://mocha.my/file?token=A%2Fb%3D&x=1&x=2";
const string route = "https://proxy.example/d/random/file.pkg";
Check(StudioTransport.WorkerBase(" " + worker + "/ ") == worker, "Base path preserved, final slash removed");
Check(StudioTransport.WorkerHost(worker) == "proxy.example", "Private base path absent from display");
foreach (string invalid in new[] {"http://proxy.example", "https://u:p@proxy.example", "https://proxy.example?q=secret", "https://proxy.example/#fragment", "https://proxy.example/\nsecret"})
    await Reject(() => Task.FromResult(StudioTransport.WorkerBase(invalid)), "WORKER_CONFIG");

var state = new State();
var client = new StudioTransport(() => new Handler(state));
Check(await client.CheckWorkerAsync(worker, CancellationToken.None) == 200, "Health HTTP status");
Check(state.Paths.Single() == "/app/private-key/health", "Health uses configured private base");
state.Paths.Clear();
Check(await client.RegisterAsync(worker, source, CancellationToken.None) == route, "Plain text route accepted");
Check(state.LastBody == source, "Signed URL body preserved exactly");
Check(state.LastContentType == "text/plain; charset=utf-8", "Legacy-compatible UTF-8 content type");
Check(!state.AuthorizationSeen, "No admin Authorization header embedded");
Check(state.Paths.Single() == "/app/private-key/api/link", "Uses existing app registration endpoint");
foreach (string bad in new[] {"https://evil.example/a.pkg", "https://proxy.example:444/a.pkg", "http://proxy.example/a.pkg", "https://user:pass@proxy.example/a.pkg", "https://proxy.example/a.pkg#secret", "{\"downloadUrl\":\"https://proxy.example/a.pkg\"}", "<html>blocked</html>"})
{
    state.Reply = bad;
    await Reject(() => client.RegisterAsync(worker, source, CancellationToken.None), "INVALID_ROUTE");
}
state.Reply = "https://proxy.example/" + new string('a', 2048) + ".pkg";
await Reject(() => client.RegisterAsync(worker, source, CancellationToken.None), "ROUTE_LIMIT");
state.Reply = new string('x', 4097);
await Reject(() => client.RegisterAsync(worker, source, CancellationToken.None), "REPLY_LIMIT");
state.Chunked = true;
await Reject(() => client.RegisterAsync(worker, source, CancellationToken.None), "REPLY_LIMIT");
state.Chunked = false;
state.Reply = route;
foreach (int status in new[] {301, 302, 401, 403, 404, 405, 410, 429, 500, 502, 503, 504})
{
    state.Status = status;
    await Reject(() => client.RegisterAsync(worker, source, CancellationToken.None), "HTTP_" + status);
}
state.Status = 200;
state.Paths.Clear();
StudioPreparedPackage pkg = await client.PrepareAsync(worker, source, CancellationToken.None);
Check(pkg.Route == route, "Prepared route retained");
Check(pkg.File.Length == 8589934592L, "8 GiB size preserved without integer truncation");
Check(pkg.File.ContentId == State.Id, "Content ID read from actual header offset");
Check(state.Ranges.SequenceEqual(new[] {"bytes=0-127", "bytes=127-127"}), "PKG and random access use bounded Range reads");
Check(state.BinaryBytes == 129, "Only 129 file bytes read");
state.Html = true;
try { await client.PrepareAsync(worker, source, CancellationToken.None); throw new Exception("Expected invalid PKG"); }
catch (InvalidDataException) { Check(true, "HTML rejected by shared PKG validator"); }
state.Html = false;
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel();
    try { await client.RegisterAsync(worker, source, cancel.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(true, "Cancellation propagated"); }
}
string sensitive = "https://proxy.example/app/private-key?token=SECRET";
Check(!StudioTransport.SafeError(new HttpRequestException(sensitive)).Contains("SECRET"), "Unexpected network errors hide URLs and tokens");
Check(!StudioTransport.SafeError(new InvalidDataException(sensitive)).Contains("private-key"), "PKG errors hide private paths");
Console.WriteLine("RESULT: " + checks + " studio checks passed");

sealed class State
{
    public const string Id = "JP0102-CUSA56149_00-FULLGAME00000000";
    public List<string> Paths = new();
    public List<string> Ranges = new();
    public string Reply = "https://proxy.example/d/random/file.pkg";
    public string LastBody = "", LastContentType = "";
    public bool AuthorizationSeen, Chunked, Html;
    public int Status = 200, BinaryBytes;
}
sealed class Handler(State state) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        state.Paths.Add(request.RequestUri.AbsolutePath);
        state.AuthorizationSeen |= request.Headers.Authorization != null;
        if (request.RequestUri.AbsolutePath.EndsWith("/health"))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") };
        if (request.Method == HttpMethod.Post)
        {
            state.LastBody = await request.Content.ReadAsStringAsync(token);
            state.LastContentType = request.Content.Headers.ContentType.ToString();
            HttpContent content = state.Chunked
                ? new UnknownLengthContent(state.Reply)
                : new StringContent(state.Reply);
            return new HttpResponseMessage((HttpStatusCode)state.Status) { Content = content };
        }
        state.Ranges.Add(request.Headers.Range.ToString());
        byte[] header = new byte[128];
        header[0] = 0x7f; header[1] = 0x43; header[2] = 0x4e; header[3] = 0x54;
        Encoding.ASCII.GetBytes(State.Id).CopyTo(header, 0x40);
        long from = request.Headers.Range.Ranges.Single().From.Value;
        long to = request.Headers.Range.Ranges.Single().To.Value;
        byte[] body = header[(int)from..((int)to + 1)];
        state.BinaryBytes += body.Length;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        { Content = new ByteArrayContent(body), RequestMessage = request };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, 8589934592L);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(state.Html ? "text/html" : "application/octet-stream");
        return response;
    }
}

sealed class UnknownLengthContent(string value) : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        => stream.WriteAsync(Encoding.UTF8.GetBytes(value)).AsTask();
}
