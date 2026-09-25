using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// One-touch pipeline for Tayson 4.0 receiver architecture.
///
/// Worker Pull Engine:
///
///   Colar -> Validar PKG -> criar rota curta no Worker
///         -> bootstrap Receiver 9091 -> enviar job pequeno -> app pode fechar
///
/// The Android app never stores or proxies the PKG. After QUEUED, the PS4 pulls
/// the package directly from the Worker using its own native HTTP stack.
/// </summary>
public static class UiAutoPipeline
{
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly object Gate = new object();

    static bool Pending;
    static bool Busy;
    static bool SendStarted;
    static string PendingUrl = "";

    static T Field<T>(MainPage page, string name) where T : class
        => typeof(MainPage).GetField(name, PrivateInstance)?.GetValue(page) as T;

    public static void Apply(MainPage page)
    {
        if (page?.Content == null) return;

        Button paste = FindPasteButton(page.Content);
        var url = Field<Entry>(page, "txtUrl");
        var generate = Field<Button>(page, "btnGerar");
        var bottom = Field<Label>(page, "lblBottomStatus");

        if (paste != null)
        {
            // UiRuntimeHotfixes is attached first. It writes the clipboard URL
            // and starts validation; this handler only arms the direct-send step.
            paste.Clicked += (_, _) => Arm(url);
        }

        if (generate != null)
        {
            generate.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(Button.IsEnabled) && generate.IsEnabled)
                    await ContinueAsync(page);
            };
        }

        if (bottom != null)
        {
            bottom.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(Label.Text)) return;
                string text = bottom.Text ?? "";

                if (LooksSuccessful(text) || LooksLikeFailure(text))
                {
                    bool active;
                    lock (Gate) active = Pending;
                    if (active) Reset();
                    return;
                }

                if (LooksValidated(text))
                    _ = ContinueAsync(page);
            };
        }
    }

    static void Arm(Entry url)
    {
        string value = (url?.Text ?? "").Trim();
        if (value.Length == 0) return;

        lock (Gate)
        {
            Pending = true;
            Busy = false;
            SendStarted = false;
            PendingUrl = value;
        }
    }

    static async Task ContinueAsync(MainPage page)
    {
        Entry url = Field<Entry>(page, "txtUrl");
        Entry ip = Field<Entry>(page, "txtIp");
        Button generate = Field<Button>(page, "btnGerar");
        Label bottom = Field<Label>(page, "lblBottomStatus");

        lock (Gate)
        {
            if (!Pending || Busy || SendStarted) return;
            Busy = true;
        }

        try
        {
            string currentUrl = (url?.Text ?? "").Trim();
            lock (Gate)
            {
                if (!Pending) return;
                if (PendingUrl.Length == 0 || !string.Equals(PendingUrl, currentUrl, StringComparison.Ordinal))
                {
                    Pending = false;
                    return;
                }
            }

            // Validation is complete only after MainPage has committed metadata
            // and enabled its existing Gerar button. We use the state as a signal
            // only; we do NOT click Gerar, because that handler opens the Android
            // save-file picker.
            PkgMetadata meta = Field<PkgMetadata>(page, "_currentMeta");
            if (meta == null || generate == null || !generate.IsEnabled) return;

            string ps4Ip = (ip?.Text ?? "").Trim();
            if (ps4Ip.Length == 0)
            {
                Fail(bottom, "Informe o IP do PS4 antes de usar o envio automático.");
                return;
            }

            lock (Gate)
            {
                if (!Pending || SendStarted) return;
                SendStarted = true;
            }

            SetStatus(page, bottom, "PKG validado. Preparando job no Worker...");

            Provider provider = Integration.ProviderFor(meta);
            string worker = UiStateStore.LoadWorkerUrl(BgftRoutePlanner.DefaultWorkerBase);
            string jobUrl = await WorkerPullJobClient.CreateAsync(
                worker,
                meta.Url,
                provider);

            SetStatus(page, bottom, "Job criado. Iniciando Tayson Pull Engine no PS4...");
            string receiverVersion = await ReceiverClient.EnsureReadyAsync(ps4Ip);

            string fileName = ReceiverClient.SafeFileName(
                meta.TitleId + "-" + meta.PkgType + ".pkg");

            await ReceiverClient.QueuePullAsync(
                ps4Ip,
                jobUrl,
                fileName,
                meta.ContentLength);

            SetStatus(
                page,
                bottom,
                "Job aceito pelo PS4 (" + receiverVersion + "). O aplicativo pode ser fechado; o PS4 continuará o download.");
            UiStateStore.Record(meta, "PULL ENFILEIRADO", ps4Ip);
            Reset();
        }
        catch (TimeoutException)
        {
            Fail(bottom, "O PS4 não respondeu. Confirme o IP, o GoldHEN e o BinLoader 9090.");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            Fail(bottom, "PS4 recusou a conexão. Reative o GoldHEN BinLoader e tente novamente.");
        }
        catch (SocketException ex)
        {
            Fail(bottom, "Falha de rede ao conectar ao PS4 (" + ex.SocketErrorCode + ").");
        }
        catch (Exception ex)
        {
            Fail(bottom, "Falha no envio automático: " + ex.Message);
        }
        finally
        {
            lock (Gate) Busy = false;
        }
    }

    static void SetStatus(MainPage page, Label bottom, string message)
    {
        if (bottom == null) return;
        Dispatch(page, () => bottom.Text = message);
    }

    static void Dispatch(MainPage page, Action action)
    {
        if (page?.Dispatcher != null) page.Dispatcher.Dispatch(action);
        else action();
    }

    static void Fail(Label bottom, string message)
    {
        Reset();
        if (bottom == null) return;
        if (bottom.Dispatcher != null) bottom.Dispatcher.Dispatch(() => bottom.Text = message);
        else bottom.Text = message;
    }

    static void Reset()
    {
        lock (Gate)
        {
            Pending = false;
            Busy = false;
            SendStarted = false;
            PendingUrl = "";
        }
    }

    static bool LooksSuccessful(string text)
        => Contains(text, "job aceito pelo ps4") ||
           Contains(text, "pull enfileirado");

    static bool LooksValidated(string text)
        => Contains(text, "analisado com sucesso") || Contains(text, "validado com sucesso") ||
           Contains(text, "pkg validado");

    static bool LooksLikeFailure(string text)
        => Contains(text, "erro") || Contains(text, "falha") || Contains(text, "inválid") ||
           Contains(text, "sem conexão") || Contains(text, "recusad") || Contains(text, "expirad") ||
           Contains(text, "não foi possível") || Contains(text, "indisponível") || Contains(text, "timeout");

    static bool Contains(string text, string value)
        => (text ?? "").IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    static Button FindPasteButton(View view)
    {
        if (view is Button button &&
            (string.Equals(button.AutomationId, "TaysonPasteDirect", StringComparison.Ordinal) ||
             string.Equals(button.Text, "Colar", StringComparison.OrdinalIgnoreCase)))
            return button;

        foreach (View child in Children(view))
        {
            Button found = FindPasteButton(child);
            if (found != null) return found;
        }
        return null;
    }

    static IEnumerable<View> Children(View view)
    {
        if (view is ScrollView scroll && scroll.Content != null) yield return scroll.Content;
        else if (view is Frame frame && frame.Content != null) yield return frame.Content;
        else if (view is ContentView content && content.Content != null) yield return content.Content;
        else if (view is Layout layout)
            foreach (var child in layout.Children)
                if (child is View childView) yield return childView;
    }
}
