using System;
using System.Reflection;
using System.Threading;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>Explicit prepare/check/import workflow; never invokes the injector.</summary>
public static class StudioWorkerScreen
{
    static readonly Color Ink = Color.FromArgb("#F4F8FF");
    static readonly Color Muted = Color.FromArgb("#A6B3CE");
    static readonly Color Panel = Color.FromArgb("#101C30");
    static readonly Color Accent = Color.FromArgb("#67E8C5");

    static Label Text(string value, double size = 13) => new Label
    { Text = value, FontSize = size, TextColor = Ink, LineBreakMode = LineBreakMode.WordWrap };
    static Button Action(string value) => new Button
    { Text = value, BackgroundColor = Accent, TextColor = Color.FromArgb("#07120F"),
      CornerRadius = 14, HeightRequest = 50, FontAttributes = FontAttributes.Bold };
    static Frame Card(View content) => new Frame
    { Content = content, BackgroundColor = Panel, BorderColor = Color.FromArgb("#29415F"),
      CornerRadius = 18, HasShadow = false, Padding = new Thickness(16) };

    public static View Build(MainPage page)
    {
        var source = new Entry { Placeholder = "Link público ou direto do PKG", TextColor = Ink,
            PlaceholderColor = Muted, BackgroundColor = Panel, FontSize = 14 };
        var status = Text("Pronto. Preparar valida a rota antes de você gerar ou enviar um ELF.");
        var result = Text("Nenhum arquivo preparado.");
        var check = Action("1 · Testar Worker");
        var prepare = Action("2 · Resolver e validar PKG");
        var use = Action("3 · Usar na tela Início");
        use.IsEnabled = false;
        var cancel = Action("Cancelar operação");
        cancel.IsEnabled = false;
        StudioPreparedPackage prepared = null;
        CancellationTokenSource running = null;
        bool busy = false;
        bool attached = false;
        var body = new VerticalStackLayout { Spacing = 14, Padding = new Thickness(18) };
        body.Children.Add(Text("CENTRAL DE TRANSPORTE", 11));
        body.Children.Add(Text("Prepare com confiança", 26));
        body.Children.Add(Text("Worker → PKG verificado → ELF → GoldHEN/BGFT", 12));
        body.Children.Add(Card(new VerticalStackLayout { Spacing = 8, Children =
        {
            Text("Worker configurado", 17),
            Text(StudioTransport.WorkerHost(UiStateStore.LoadWorkerUrl())),
            Text("Configure em Ajustes a base privada completa /app/… do Tayson Universal Proxy. O caminho de acesso fica oculto nesta tela.", 12),
            check
        }}));
        body.Children.Add(Card(new VerticalStackLayout { Spacing = 10, Children =
        {
            Text("Preparação do arquivo", 17), source, prepare, cancel, status
        }}));
        body.Children.Add(Card(new VerticalStackLayout { Spacing = 10, Children =
        {
            Text("Resultado verificável", 17), result, use,
            Text("Range e integridade são verificados em pequenas amostras do cabeçalho. Isso não equivale ao hash do arquivo inteiro nem confirma instalação no PS4.", 12)
        }}));
        var view = new ScrollView { Content = body, BackgroundColor = Color.FromArgb("#080F1D") };

        view.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != "Parent") return;
            if (view.Parent != null) { attached = true; return; }
            if (attached) running?.Cancel();
        };
        void Busy(bool value)
        {
            busy = value;
            source.IsEnabled = !value;
            check.IsEnabled = !value;
            prepare.IsEnabled = !value;
            cancel.IsEnabled = value;
            use.IsEnabled = !value && prepared != null;
        }
        source.TextChanged += (_, _) =>
        {
            prepared = null;
            use.IsEnabled = false;
            result.Text = "Link alterado. Prepare novamente para validar este arquivo.";
        };
        cancel.Clicked += (_, _) => running?.Cancel();

        check.Clicked += async (_, _) =>
        {
            if (busy) return;
            using var operation = new CancellationTokenSource();
            running = operation;
            Busy(true);
            status.Text = "Consultando /health…";
            try
            {
                int code = await new StudioTransport().CheckWorkerAsync(UiStateStore.LoadWorkerUrl(), operation.Token);
                status.Text = "Worker respondeu HTTP " + code + ". Agora prepare um PKG para testar o transporte real.";
            }
            catch (Exception error) { status.Text = StudioTransport.SafeError(error); }
            finally { running = null; Busy(false); }
        };
        prepare.Clicked += async (_, _) =>
        {
            if (busy) return;
            prepared = null;
            result.Text = "Aguardando validação…";
            using var operation = new CancellationTokenSource();
            running = operation;
            Busy(true);
            status.Text = "Resolvendo no Worker e verificando os bytes do PKG…";
            try
            {
                StudioPreparedPackage candidate = await new StudioTransport().PrepareAsync(
                    UiStateStore.LoadWorkerUrl(), source.Text, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                prepared = candidate;
                result.Text = "PKG reconhecido\nContent ID: " + candidate.File.ContentId +
                    "\nTamanho: " + candidate.File.Length + " bytes\nRange: amostras 0–127 e 127–127 coerentes";
                status.Text = "Pronto para importar. Nenhum payload foi enviado ao console.";
            }
            catch (Exception error)
            {
                status.Text = StudioTransport.SafeError(error);
                result.Text = "Preparação interrompida. Gere uma nova rota quando estiver pronto.";
            }
            finally { running = null; Busy(false); }
        };
        use.Clicked += (_, _) =>
        {
            if (busy || prepared == null) return;
            var entry = typeof(MainPage).GetField("txtUrl", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(page) as Entry;
            if (entry == null) { status.Text = "Campo principal indisponível. Volte ao Início."; return; }
            entry.Text = prepared.Route;
            status.Text = "Link importado. Vá ao Início, valide o PKG e use Gerar ELF / Enviar ao PS4.";
        };
        return view;
    }
}
