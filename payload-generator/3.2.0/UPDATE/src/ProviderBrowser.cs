using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Tayson.Providers;

// Uses the MAUI WebView already present in V3. No new Java peer registrations,
// embedded credentials, or hardcoded private download APIs.
public static class ProviderBrowser
{
    const string CaptureScript = @"
(function(){
 if(!window.__taysonDownloads){
  var q=[];
  function likely(u){try{return /\.pkg(?:[?#]|$)|\/(?:download|dl|d)(?:\/|$)|proxy-download|[?&](?:access|signature|token|expires|expiration)=/i.test(u.href);}catch(e){return false;}}
  function add(v,force){try{if(!v)return;var u=new URL(String(v),location.href);if(!/^https?:$/.test(u.protocol))return;if(!force&&!likely(u))return;if(q.indexOf(u.href)<0){q.push(u.href);if(q.length>24)q.shift();}}catch(e){}}
  function attrs(el,force){if(!el||!el.getAttribute)return;['href','action','data-url','data-href','data-download','data-download-url','data-link'].forEach(function(n){var v=el.getAttribute(n);if(v)add(v,force);});}
  function scan(root){try{(root||document).querySelectorAll('a[href],form[action],[data-url],[data-href],[data-download],[data-download-url],[data-link]').forEach(function(el){attrs(el,false);});}catch(e){}}
  function data(v,d){if(!v||typeof v!=='object'||d>5)return;Object.keys(v).slice(0,60).forEach(function(k){var x=v[k];if(typeof x==='string'&&/^(url|href|link|location|redirect|download_?url|download_?link|direct_?url|direct_?link|file_?url|signed_?url)$/i.test(k))add(x,true);else if(x&&typeof x==='object')data(x,d+1);});}
  document.addEventListener('click',function(e){var el=e.target.closest&&e.target.closest('a[href],button,form,[data-url],[data-href],[data-download],[data-download-url],[data-link]');if(el)attrs(el,true);},true);
  document.addEventListener('submit',function(e){attrs(e.target,true);},true);
  var op=window.open;window.open=function(u){if(u)add(u,true);return op.apply(this,arguments);};
  if(window.fetch){var ft=window.fetch;window.fetch=function(){return ft.apply(this,arguments).then(function(r){try{if(r.url)add(r.url,false);if((r.headers.get('content-type')||'').indexOf('application/json')>=0){var c=r.clone(),reader=c.body.getReader(),chunks=[],size=0;function read(){reader.read().then(function(p){if(p.done){try{var a=new Uint8Array(size),o=0;chunks.forEach(function(b){a.set(b,o);o+=b.length;});data(JSON.parse(new TextDecoder().decode(a)),0);}catch(e){}return;}size+=p.value.length;if(size>131072){reader.cancel();return;}chunks.push(p.value);read();}).catch(function(){});}read();}}catch(e){}return r;});};}
  var send=XMLHttpRequest.prototype.send;XMLHttpRequest.prototype.send=function(){this.addEventListener('load',function(){try{if(this.responseURL)add(this.responseURL,false);if((this.getResponseHeader('content-type')||'').indexOf('application/json')>=0){if(this.responseType==='json')data(this.response,0);else if((!this.responseType||this.responseType==='text')&&this.responseText.length<131072)data(JSON.parse(this.responseText),0);}}catch(e){}});return send.apply(this,arguments);};
  window.__taysonDownloads={q:q,add:add,scan:scan};
 }
 var s=window.__taysonDownloads;s.scan(document);
 var items=s.q.splice(0,24);return items.map(encodeURIComponent).join('|');
})()";

    public static async Task<string> ResolveAsync(Page owner, Uri source, DownloadClient client)
    {
        var finish = new TaskCompletionSource<string>();
        using var stopped = new CancellationTokenSource();
        var pending = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var browser = new WebView();
        var status = new Label { Text = "Toque no download do site e conclua a verificação, se aparecer.", FontSize = 13, TextColor = Colors.Black };
        var address = new Label { Text = source.Host, FontSize = 12, TextColor = Colors.DarkGray };
        var direct = new Entry { Placeholder = "Ou cole aqui o link direto liberado pelo site", TextColor = Colors.Black, BackgroundColor = Colors.White, FontSize = 13 };
        var use = new Button { Text = "Validar link colado", FontSize = 13 };
        var back = new Button { Text = "Voltar", FontSize = 12 };
        var reload = new Button { Text = "Recarregar", FontSize = 12 };
        var cancel = new Button { Text = "Cancelar", FontSize = 12 };
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitionCollection {
            new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Star } } };
        buttons.Add(back, 0, 0); buttons.Add(reload, 1, 0); buttons.Add(cancel, 2, 0);
        var layout = new Grid { Padding = 10, RowSpacing = 4, RowDefinitions = new RowDefinitionCollection {
            new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto },
            new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto },
            new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = GridLength.Auto } } };
        layout.Add(address, 0, 0); layout.Add(status, 0, 1); layout.Add(direct, 0, 2);
        layout.Add(use, 0, 3); layout.Add(browser, 0, 4); layout.Add(buttons, 0, 5);
        string title = ProviderResolvers.IsAkiraBox(source) ? "AkiraBox" : LinkPolicy.Detect(source).ToString();
        var page = new ContentPage { Title = title, Content = layout, BackgroundColor = Colors.White };
        var validation = new SemaphoreSlim(1, 1);

        void Queue(string text)
        {
            if (finish.Task.IsCompleted || seen.Count >= 384) return;
            try
            {
                Uri uri = LinkPolicy.Parse(text);
                bool sourceIsDownload = ProviderResolvers.IsAkiraDownloadCandidate(source) || LinkPolicy.DownloadHint(source);
                if ((!sourceIsDownload && uri.AbsoluteUri == source.AbsoluteUri) || pending.Count >= 24 || !seen.Add(uri.AbsoluteUri)) return;
                pending.Enqueue(uri.AbsoluteUri);
            }
            catch (InvalidDataException) { }
        }

        async Task Validate(string text, bool manual)
        {
            await validation.WaitAsync(stopped.Token);
            try
            {
                if (finish.Task.IsCompleted) return;
                status.Text = "Validando o arquivo para gerar o payload…";
                Uri candidate = LinkPolicy.Parse(text);
                DownloadInfo found = ProviderResolvers.IsAkiraBox(candidate)
                    ? await AkiraDownloadProbe.ProbeAsync(text, 128, stopped.Token)
                    : await client.ProbeAsync(text, 128, stopped.Token);
                finish.TrySetResult(found.Url.AbsoluteUri);
            }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            catch (Exception e)
            {
                status.Text = manual ? e.Message : "Aguardando um PKG direto. Toque no botão de download do site.";
            }
            finally { validation.Release(); }
        }

        browser.Navigating += (sender, e) =>
        {
            if (e.Url == "about:blank") return;
            if (!Uri.TryCreate(e.Url, UriKind.Absolute, out Uri u) || (u.Scheme != "http" && u.Scheme != "https"))
            { e.Cancel = true; return; }
            address.Text = u.Host;
            bool candidate = LinkPolicy.DownloadHint(u) || ProviderResolvers.IsAkiraDownloadCandidate(u) ||
                u.AbsolutePath.IndexOf("proxy-download", StringComparison.OrdinalIgnoreCase) >= 0 ||
                !u.Host.Equals(source.Host, StringComparison.OrdinalIgnoreCase);
            if (candidate) Queue(e.Url);
            if (u.AbsolutePath.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };
        browser.Navigated += (sender, e) =>
        {
            if (e.Result != WebNavigationResult.Success)
                status.Text = "A página não abriu. Recarregue ou cole o link direto liberado no seu navegador.";
            if (Uri.TryCreate(e.Url, UriKind.Absolute, out Uri u) &&
                (LinkPolicy.DownloadHint(u) || ProviderResolvers.IsAkiraDownloadCandidate(u) ||
                 u.AbsolutePath.IndexOf("proxy-download", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 !u.Host.Equals(source.Host, StringComparison.OrdinalIgnoreCase)))
                Queue(e.Url);
        };
        use.Clicked += async (sender, e) =>
        {
            use.IsEnabled = false;
            try { await Validate(direct.Text, true); }
            catch (OperationCanceledException) { }
            finally { use.IsEnabled = true; }
        };
        cancel.Clicked += (sender, e) => finish.TrySetCanceled();
        back.Clicked += (sender, e) => { if (browser.CanGoBack) browser.GoBack(); };
        reload.Clicked += (sender, e) => { seen.Clear(); pending.Clear(); browser.Reload(); };
        page.Disappearing += (sender, e) => { finish.TrySetCanceled(); stopped.Cancel(); };

        async Task Poll()
        {
            while (!stopped.IsCancellationRequested && !finish.Task.IsCompleted)
            {
                try
                {
                    string encoded = await browser.EvaluateJavaScriptAsync(CaptureScript);
                    if (!string.IsNullOrEmpty(encoded) && encoded != "null")
                        foreach (string item in encoded.Trim('"').Split('|'))
                            if (item.Length > 0 && item.Length <= 24576) Queue(Uri.UnescapeDataString(item));
                    if (pending.Count > 0) await Validate(pending.Dequeue(), false);
                    await Task.Delay(650, stopped.Token);
                }
                catch (OperationCanceledException) when (stopped.IsCancellationRequested) { break; }
                catch { await Task.Delay(650); }
            }
        }

        bool opened = false;
        Task poll = Task.CompletedTask;
        try
        {
            await owner.Navigation.PushModalAsync(page);
            opened = true;
            browser.Source = source.AbsoluteUri;
            poll = Poll();
            return await finish.Task;
        }
        catch (OperationCanceledException)
        {
            throw new InvalidDataException("Análise cancelada. Nenhum payload foi gerado.");
        }
        finally
        {
            stopped.Cancel();
            browser.Source = "about:blank";
            if (opened && owner.Navigation.ModalStack.Count > 0 &&
                ReferenceEquals(owner.Navigation.ModalStack[owner.Navigation.ModalStack.Count - 1], page))
                await owner.Navigation.PopModalAsync();
            await poll;
            browser.Handler?.DisconnectHandler();
        }
    }
}
