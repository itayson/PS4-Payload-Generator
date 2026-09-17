using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

public static class Integration
{
    static readonly DownloadClient Client = new DownloadClient();
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(MainPage page, string name) => (T)typeof(MainPage).GetField(name, PrivateInstance).GetValue(page);

    public static void Initialize(MainPage page)
    {
        var entry = Field<Entry>(page, "txtUrl");
        var ip = Field<Entry>(page, "txtIp");
        var bottom = Field<Label>(page, "lblBottomStatus");

        entry.Placeholder = "Mocha, VikingFile, FileKeeper, Rootz, AkiraBox ou link direto";
        bottom.Text = "Tayson 3.4.0 • BGFT universal • BinLoader 9090";

        string savedIp = UiStateStore.LoadPs4Ip();
        if (!string.IsNullOrWhiteSpace(savedIp)) ip.Text = savedIp;
        ip.TextChanged += (_, _) => UiStateStore.SavePs4Ip(ip.Text);

        entry.TextChanged += (sender, e) => Invalidate(page);
        var scan = Field<Button>(page, "btnScan");
        scan.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == "IsEnabled") Field<Button>(page, "btnLimpar").IsEnabled = scan.IsEnabled;
        };

        bottom.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Label.Text)) return;
            string text = bottom.Text ?? "";
            if (text.IndexOf("Payload enviado", StringComparison.OrdinalIgnoreCase) < 0 &&
                text.IndexOf("sent", StringComparison.OrdinalIgnoreCase) < 0) return;
            var meta = Field<PkgMetadata>(page, "_currentMeta");
            if (meta != null) UiStateStore.Record(meta, "PAYLOAD ENVIADO", ip.Text);
        };

        UiRedesign.Apply(page);
        UiPolish.Apply(page);
    }

    public static void Invalidate(MainPage page)
    {
        var current = Field<PkgMetadata>(page, "_currentMeta");
        if (current != null) UiArtworkStore.Clear(current.ContentId);
        typeof(MainPage).GetField("_currentMeta", PrivateInstance).SetValue(page, null);
        Field<Button>(page, "btnGerar").IsEnabled = false;
        Field<Button>(page, "btnInjetar").IsEnabled = false;
        Field<Image>(page, "imgIcon").Source = null;
    }

    static Task<DownloadInfo> ProbeForProviderAsync(string url, int limit = 128)
    {
        Uri uri = LinkPolicy.Parse(url);
        return ProviderResolvers.IsAkiraBox(uri)
            ? AkiraDownloadProbe.ProbeAsync(url, limit)
            : Client.ProbeAsync(url, limit);
    }

    public static async Task<string> ResolveAsync(MainPage page, string value)
    {
        Uri uri = LinkPolicy.Parse(value);
        Provider provider = LinkPolicy.Detect(uri);
        bool knownProviderPage = provider != Provider.Direct || ProviderResolvers.IsAkiraBox(uri);

        try { return (await ProbeForProviderAsync(uri.AbsoluteUri)).Url.AbsoluteUri; }
        catch (Exception ex) when (ex is InvalidDataException || ex is HttpRequestException || ex is OperationCanceledException)
        {
            if (!knownProviderPage) throw;
        }

        if (ProviderResolvers.IsRootz(uri))
        {
            string proxy = ProviderResolvers.RootzProxyUrl(uri);
            if (!string.IsNullOrEmpty(proxy))
            {
                try { return (await Client.ProbeAsync(proxy)).Url.AbsoluteUri; }
                catch (Exception ex) when (ex is InvalidDataException || ex is HttpRequestException || ex is OperationCanceledException) { }
            }
        }

        if (provider == Provider.MediaFire)
        {
            try
            {
                string html = await Client.ReadPageAsync(uri.AbsoluteUri);
                foreach (Match anchor in new Regex("<a\\b[^>]*>", RegexOptions.IgnoreCase).Matches(html))
                {
                    if (!new Regex("\\bid\\s*=\\s*['\"]downloadButton['\"]", RegexOptions.IgnoreCase).IsMatch(anchor.Value)) continue;
                    Match href = new Regex("\\bhref\\s*=\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase).Match(anchor.Value);
                    if (href.Success)
                    {
                        string target = new Uri(uri, WebUtility.HtmlDecode(href.Groups[1].Value)).AbsoluteUri;
                        return (await Client.ProbeAsync(target)).Url.AbsoluteUri;
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is HttpRequestException || ex is OperationCanceledException) { }
        }

        return await ProviderBrowser.ResolveAsync(page, uri, Client);
    }

    public static async Task<PkgMetadata> AnalyzeAsync(MainPage page, string url, int limit)
    {
        // 3.4.0 keeps validation bounded while reading enough of the metadata
        // area to discover ICON/PIC artwork when the package exposes it early.
        int metadataLimit = Math.Min(2097152, Math.Max(1048576, limit));
        DownloadInfo info = await ProbeForProviderAsync(url, metadataLimit);
        var sfo = ((string Title, string Category))typeof(MainPage).GetMethod("ExtractMetadataFromSfo", PrivateInstance)
            .Invoke(page, new object[] { info.Prefix });
        string title = sfo.Title;
        if (string.IsNullOrWhiteSpace(title)) title = Path.GetFileNameWithoutExtension(info.Filename);
        if (string.IsNullOrWhiteSpace(title)) title = "Pacote PS4 " + info.ContentId.Substring(7, 9);
        string category = (sfo.Category ?? "").Trim().ToLowerInvariant();
        string kind = category == "gp" ? "PS4PATCH" : category == "ac" ? "PS4AC" : "PS4GD";
        string suffix = kind == "PS4PATCH" ? "Update" : kind == "PS4AC" ? "DLC" : "Game";

        byte[][] artworks = UiArtworkStore.Extract(info.Prefix);
        UiArtworkStore.Set(info.ContentId, artworks);
        byte[] icon = artworks.Length > 0 ? artworks[0] : null;

        var meta = new PkgMetadata
        {
            Url = info.Url.AbsoluteUri,
            ContentLength = info.Length,
            ContentId = info.ContentId,
            TitleId = info.ContentId.Substring(7, 9),
            PkgType = kind,
            DisplayName = title,
            CleanName = DownloadClient.CleanFilename(title + " " + suffix),
            IconBytes = icon
        };
        UiStateStore.Record(meta, "VALIDADO", Field<Entry>(page, "txtIp").Text);
        return meta;
    }

    public static async Task<byte[]> GenerateAsync(MainPage page)
    {
        PkgMetadata meta = Field<PkgMetadata>(page, "_currentMeta");
        if (meta == null) throw new InvalidDataException("Analise um link válido antes de gerar o payload.");
        DownloadInfo current = await ProbeForProviderAsync(meta.Url);
        if (!ReferenceEquals(meta, Field<PkgMetadata>(page, "_currentMeta")))
            throw new InvalidDataException("O link foi alterado durante a verificação. Analise o novo link antes de enviar.");
        if (current.Length != meta.ContentLength || current.ContentId != meta.ContentId)
            throw new InvalidDataException("O arquivo mudou desde a análise. Analise o link novamente.");

        string worker = UiStateStore.LoadWorkerUrl(BgftRoutePlanner.DefaultWorkerBase);
        using var router = new BgftRoutePlanner(
            () => new HttpClientHandler { AllowAutoRedirect = false },
            worker);

        string bgftUrl;
        Uri currentUri = LinkPolicy.Parse(current.Url.AbsoluteUri);
        if (BgftRoutePlanner.IsAkiraAccessUrl(currentUri))
        {
            string cookie = AkiraDownloadProbe.GetCookieHeader(current.Url.AbsoluteUri);
            bgftUrl = await router.PrepareAsync(
                current.Url.AbsoluteUri,
                cookie,
                AkiraDownloadProbe.DefaultReferer,
                AkiraDownloadProbe.BrowserUserAgent);
        }
        else
        {
            bgftUrl = await router.PrepareAsync(current.Url.AbsoluteUri);
        }

        using Stream input = await FileSystem.OpenAppPackageFileAsync("template.elf");
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        byte[] payload = PayloadPatcher.Patch(
            buffer.ToArray(),
            bgftUrl,
            meta.ContentId,
            meta.DisplayName,
            meta.PkgType,
            meta.ContentLength);
        UiStateStore.Record(meta, "ELF GERADO", Field<Entry>(page, "txtIp").Text);
        return payload;
    }
}
