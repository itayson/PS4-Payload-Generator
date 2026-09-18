using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Tayson.Providers;

public enum Provider { Direct, VikingFile, AkiraBox, Rootz, Mocha, MediaFire }

public static class LinkPolicy
{
    public static Uri Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Cole o link do arquivo.");
        value = value.Trim();
        foreach (char c in value)
            if (char.IsControl(c)) throw new InvalidDataException("O link contém caracteres inválidos.");
        if (!value.Contains("://") && !value.Contains(":")) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Use um link HTTP ou HTTPS, sem usuário ou senha no endereço.");
        if (!string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Remova o trecho depois de # do link direto antes de analisar.");
        return uri;
    }

    public static bool Domain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    public static Provider Detect(Uri uri)
    {
        string h = uri.IdnHost.TrimEnd('.');
        if (Domain(h, "vikingfile.com")) return Provider.VikingFile;
        if (Domain(h, "akirabox.com")) return Provider.AkiraBox;
        if (Domain(h, "rootz.so")) return Provider.Rootz;
        if (Domain(h, "mocha.my")) return Provider.Mocha;
        if (Domain(h, "mediafire.com")) return Provider.MediaFire;
        return Provider.Direct;
    }

    public static bool Unsupported(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri u)) return false;
        return Domain(u.IdnHost, "drive.google.com") || Domain(u.IdnHost, "drive.usercontent.google.com") ||
            Domain(u.IdnHost, "mega.nz") || Domain(u.IdnHost, "mega.co.nz");
    }

    public static bool DownloadHint(Uri uri) =>
        uri.AbsolutePath.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase) ||
        new Regex(@"/(?:download|dl|d)(?:/|$)", RegexOptions.IgnoreCase).IsMatch(uri.AbsolutePath) ||
        new Regex(@"^(?:download|dl|cdn|fs|node)[0-9.-]", RegexOptions.IgnoreCase).IsMatch(uri.Host);
}

public sealed class DownloadInfo
{
    public Uri Url;
    public long Length;
    public string Filename;
    public byte[] Prefix;
    public string ContentId;
}

public sealed class DownloadClient : IDisposable
{
    readonly Func<HttpMessageHandler> factory;
    static readonly Regex ContentId = new Regex(@"\A[A-Z]{2}[0-9]{4}-[A-Z]{4}[0-9]{5}_[0-9A-Z]{2}-[A-Z0-9_]{16}\z", RegexOptions.None);

    public DownloadClient() : this(() => new HttpClientHandler { AllowAutoRedirect = false }) { }
    public DownloadClient(Func<HttpMessageHandler> createHandler) { factory = createHandler; }
    public void Dispose() { }

    sealed class Transfer : IDisposable
    {
        public HttpClient Client;
        public HttpResponseMessage Response;
        public void Dispose() { Response.Dispose(); Client.Dispose(); }
    }

    static string Header(HttpHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? string.Join(",", values) : "";

    static (long From, long To, long Length) Range(HttpContentHeaders headers)
    {
        Match m = new Regex(@"\Abytes ([0-9]+)-([0-9]+)/([0-9]+)\z", RegexOptions.None).Match(Header(headers, "Content-Range"));
        if (!m.Success || !Decimal(m.Groups[1].Value, out long from) ||
            !Decimal(m.Groups[2].Value, out long to) || !Decimal(m.Groups[3].Value, out long length) ||
            from > to || to >= length) throw new InvalidDataException("O servidor enviou um Content-Range inválido.");
        return (from, to, length);
    }

    static bool Decimal(string text, out long value)
    {
        value = 0;
        if (text.Length == 0) return false;
        foreach (char c in text)
        {
            int digit = c - '0';
            if (digit < 0 || digit > 9 || value > (long.MaxValue - digit) / 10) return false;
            value = value * 10 + digit;
        }
        return true;
    }

    static bool Encoded(HttpContentHeaders headers)
    {
        string value = Header(headers, "Content-Encoding");
        return value.Length > 0 && !value.Equals("identity", StringComparison.OrdinalIgnoreCase);
    }

    async Task<Transfer> GetAsync(Uri url, long from, long to, CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int redirects = 0; redirects <= 8; redirects++)
        {
            if (!seen.Add(url.AbsoluteUri)) throw new InvalidDataException("O servidor entrou em um ciclo de redirecionamentos.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(from, to);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", new[] { "identity" });
            var http = new HttpClient(factory()) { Timeout = TimeSpan.FromSeconds(25) };
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); }
            catch { http.Dispose(); throw; }
            int status = (int)response.StatusCode;
            if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
            {
                string location = Header(response.Headers, "Location");
                response.Dispose(); http.Dispose();
                if (string.IsNullOrEmpty(location)) throw new InvalidDataException("O redirecionamento não contém um endereço.");
                url = LinkPolicy.Parse(new Uri(url, location).AbsoluteUri);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose(); http.Dispose();
                if (status == 401 || status == 403)
                    throw new InvalidDataException("O link exige autorização ou expirou. Libere o download na página do site e tente novamente.");
                if (status == 404 || status == 410)
                    throw new InvalidDataException("Arquivo removido ou link expirado.");
                throw new InvalidDataException("O servidor respondeu HTTP " + status + ". Tente novamente mais tarde.");
            }
            return new Transfer { Client = http, Response = response };
        }
        throw new InvalidDataException("O link excedeu o limite de redirecionamentos.");
    }

    static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        using Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        byte[] data = new byte[limit];
        int read = 0;
        while (read < limit)
        {
            int n = await stream.ReadAsync(data, read, limit - read, token).ConfigureAwait(false);
            if (n == 0) break;
            read += n;
        }
        if (read != data.Length) Array.Resize(ref data, read);
        return data;
    }

    public async Task<DownloadInfo> ProbeAsync(string value, int prefixLimit = 128, CancellationToken token = default)
    {
        Uri requested = LinkPolicy.Parse(value);
        if (prefixLimit < 128 || prefixLimit > 2097152) throw new ArgumentOutOfRangeException(nameof(prefixLimit));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        token = timeout.Token;
        using var first = await GetAsync(requested, 0, prefixLimit - 1, token).ConfigureAwait(false);
        var response = first.Response;
        string mime = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mime.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0 ||
            mime.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 || Encoded(response.Content.Headers))
            throw new InvalidDataException("Este endereço entrega uma página, não um PKG direto.");
        var range = response.StatusCode == HttpStatusCode.PartialContent ? Range(response.Content.Headers) : default;
        if (response.StatusCode == HttpStatusCode.PartialContent && range.From != 0)
            throw new InvalidDataException("O servidor enviou um Content-Range inválido.");
        long size = response.StatusCode == HttpStatusCode.PartialContent ? range.Length : response.Content.Headers.ContentLength.GetValueOrDefault();
        if (size < 128) throw new InvalidDataException("Não foi possível obter o tamanho completo do PKG.");
        int wanted = (int)Math.Min(size, prefixLimit);
        if (response.StatusCode == HttpStatusCode.PartialContent && range.To != wanted - 1)
            throw new InvalidDataException("O servidor retornou um intervalo diferente do solicitado.");
        byte[] prefix = await ReadBoundedAsync(response.Content, wanted, token).ConfigureAwait(false);
        if (prefix.Length != wanted) throw new InvalidDataException("O cabeçalho do arquivo chegou incompleto.");
        if (prefix[0] != 0x7f || prefix[1] != 0x43 || prefix[2] != 0x4e || prefix[3] != 0x54)
            throw new InvalidDataException("O arquivo não tem a assinatura de um PKG de PS4. ZIP, RAR, HTML e payloads não são PKGs.");
        string id = Encoding.ASCII.GetString(prefix, 0x40, 36);
        if (!ContentId.IsMatch(id)) throw new InvalidDataException("O PKG não contém um Content ID válido no cabeçalho.");
        Uri final = LinkPolicy.Parse((response.RequestMessage?.RequestUri ?? requested).AbsoluteUri);

        using var second = await GetAsync(final, 127, 127, token).ConfigureAwait(false);
        var seek = second.Response;
        var sr = seek.StatusCode == HttpStatusCode.PartialContent ? Range(seek.Content.Headers) : default;
        if (seek.StatusCode != HttpStatusCode.PartialContent || sr.From != 127 || sr.To != 127 || sr.Length != size || Encoded(seek.Content.Headers))
            throw new InvalidDataException("O servidor não aceita downloads por intervalo (HTTP Range), necessários para esta integração.");
        byte[] one = await ReadBoundedAsync(seek.Content, 2, token).ConfigureAwait(false);
        if (one.Length != 1 || one[0] != prefix[127])
            throw new InvalidDataException("O servidor retornou dados inconsistentes ao validar o download.");

        string file = "";
        string disposition = Header(response.Content.Headers, "Content-Disposition");
        Match named = new Regex(@"(?:^|;)\s*filename\*\s*=\s*UTF-8''([^;]+)", RegexOptions.IgnoreCase).Match(disposition);
        if (named.Success) file = Uri.UnescapeDataString(named.Groups[1].Value);
        else
        {
            named = new Regex("(?:^|;)\\s*filename\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Match(disposition);
            if (named.Success) file = named.Groups[1].Value;
        }
        if (string.IsNullOrWhiteSpace(file)) file = Uri.UnescapeDataString(Path.GetFileName(final.AbsolutePath));
        return new DownloadInfo { Url = final, Length = size, Prefix = prefix, ContentId = id, Filename = CleanFilename(file) };
    }

    public async Task<string> ReadPageAsync(string value, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var transfer = await GetAsync(LinkPolicy.Parse(value), 0, 262143, timeout.Token).ConfigureAwait(false);
        var r = transfer.Response;
        string mime = r.Content.Headers.ContentType?.MediaType ?? "";
        if (!mime.Contains("html") && !mime.StartsWith("text/")) return "";
        return Encoding.UTF8.GetString(await ReadBoundedAsync(r.Content, 262144, timeout.Token).ConfigureAwait(false));
    }

    public static string CleanFilename(string name)
    {
        name = (name ?? "").Trim('"', ' ', '\t');
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = Regex.Replace(name, @"[\x00-\x1f/\\]", "_");
        return name.Length > 120 ? name.Substring(0, 120) : name;
    }
}

public static class PayloadPatcher
{
    public static string RegistrationUrl(string url)
    {
        Uri uri = LinkPolicy.Parse(url);
        string candidate = url.Trim();
        if (!uri.AbsolutePath.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(uri.Query))
            candidate += "#tayson.pkg";
        if (Encoding.UTF8.GetByteCount(candidate) >= 2048)
            throw new InvalidDataException("A URL com o ajuste de compatibilidade excede os 2.047 bytes do payload. Publique o Worker universal com KV para usar uma rota curta.");
        return candidate;
    }

    public static byte[] Patch(byte[] template, string url, string contentId, string name, string type, long size)
    {
        if (template == null || template.Length < 64 || template[0] != 0x7f || template[1] != 69 || template[2] != 76 || template[3] != 70)
            throw new InvalidDataException("Template ELF inválido.");
        url = RegistrationUrl(url);
        if (size < 128) throw new InvalidDataException("Tamanho de PKG inválido.");
        byte[] output = (byte[])template.Clone();

        // The inherited payload always resolved the fake-package/debug register
        // API. That is appropriate for patches, but established PS4 BGFT
        // implementations use the normal registration path for base games and
        // add-on content. Switch only the exported symbol string in the loaded
        // ELF image; no instruction, offset or control-flow change is required.
        PatchBgftRegisterApi(output, type);

        Put(output, "###URL_PLACEHOLDER###", 2048, url, false);
        Put(output, "###CONTENT_ID_PLACEHOLDER###", 64, contentId, false);
        Put(output, "###NAME_PLACEHOLDER###", 128, name, true);
        Put(output, "###TYPE###", 16, type, false);
        byte[] marker = { 187, 187, 187, 187, 187, 187, 187, 187 };
        int position = Unique(output, marker);
        for (int i = 0; i < 8; i++) output[position + i] = (byte)((ulong)size >> (8 * i));
        return output;
    }

    static void PatchBgftRegisterApi(byte[] output, string type)
    {
        if (string.Equals(type, "PS4PATCH", StringComparison.Ordinal)) return;

        byte[] oldName = Encoding.ASCII.GetBytes("sceBgftServiceIntDebugDownloadRegisterPkg\0");
        byte[] newName = Encoding.ASCII.GetBytes("sceBgftServiceDownloadRegisterTask\0");
        if (newName.Length > oldName.Length)
            throw new InvalidDataException("Nome da API BGFT excede o espaço reservado no template.");

        // The same symbol text also appears later in DWARF debug strings. Only
        // the loaded ELF image (before .debug_info) is relevant to sceKernelDlsym.
        int searchLimit = Math.Min(output.Length, 0x18000);
        int position = -1;
        for (int i = 0; i <= searchLimit - oldName.Length; i++)
        {
            bool same = true;
            for (int j = 0; j < oldName.Length; j++)
            {
                if (output[i + j] != oldName[j]) { same = false; break; }
            }
            if (!same) continue;
            if (position != -1)
                throw new InvalidDataException("Template contém múltiplos símbolos BGFT carregados.");
            position = i;
        }

        if (position < 0)
            throw new InvalidDataException("Template não contém a API BGFT esperada.");

        Array.Clear(output, position, oldName.Length);
        Array.Copy(newName, 0, output, position, newName.Length);
    }

    static void Put(byte[] output, string marker, int width, string value, bool truncateName)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('\0') >= 0) throw new InvalidDataException("Os metadados do payload estão incompletos.");
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length >= width)
        {
            if (!truncateName) throw new InvalidDataException("O link ou identificador excede o espaço do payload original. Use um link direto mais curto; a URL não será cortada.");
            int count = width - 1;
            while (count > 0 && (bytes[count] & 0xc0) == 0x80) count--;
            Array.Resize(ref bytes, count);
        }
        byte[] needle = new byte[width];
        byte[] text = Encoding.ASCII.GetBytes(marker);
        Array.Copy(text, needle, text.Length);
        int position = Unique(output, needle);
        Array.Clear(output, position, width);
        Array.Copy(bytes, 0, output, position, bytes.Length);
    }

    static int Unique(byte[] data, byte[] needle)
    {
        int result = -1;
        for (int i = 0; i <= data.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && data[i + j] == needle[j]) j++;
            if (j != needle.Length) continue;
            if (result != -1) throw new InvalidDataException("Template com marcador duplicado.");
            result = i;
        }
        if (result == -1) throw new InvalidDataException("Template sem um marcador obrigatório.");
        return result;
    }
}
