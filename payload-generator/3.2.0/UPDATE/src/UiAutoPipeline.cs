using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// One-touch pipeline for the 3.4.0 Home screen.
///
/// After Colar, the existing hardware-tested validation handler remains
/// authoritative. As soon as the PKG is valid, this coordinator generates the
/// ELF only in memory and streams it directly to GoldHEN BinLoader TCP 9090:
///
///   Colar -> Validar PKG -> Gerar ELF em memoria -> PS4:9090 -> BGFT
///
/// No Android file picker is opened and no ELF needs to be saved locally.
/// Manual buttons remain available as fallback. Connection establishment may
/// be retried, but once a TCP connection succeeds the payload itself is written
/// only once, preventing duplicate BGFT registrations.
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

            SetStatus(page, bottom, "PKG validado. Preparando o ELF...");

            byte[] payload = await Integration.GenerateAsync(page);
            if (payload == null || payload.Length == 0)
                throw new InvalidOperationException("O ELF gerado está vazio.");
            UiStateStore.Record(meta, "ELF GERADO", ps4Ip);

            // The ELF stays in memory. Nothing is written to Downloads and no
            // Storage Access Framework/file-picker flow is invoked.
            UiStateStore.Record(meta, "ENVIANDO ELF", ps4Ip);
            SetStatus(page, bottom, "Conectando ao GoldHEN BinLoader 9090...");
            await SendToBinLoaderAsync(ps4Ip, payload);

            // A completed TCP write is not an acknowledgement from BGFT.
            UiStateStore.Record(meta, "PAYLOAD ENVIADO", ps4Ip);
            SetStatus(page, bottom, "ELF transmitido ao BinLoader. Confira o download no PS4.");
            Reset();
        }
        catch (TimeoutException)
        {
            Fail(bottom, "O PS4:9090 não respondeu após várias tentativas. Confirme que o GoldHEN BinLoader está ativado.");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            Fail(bottom, "PS4 recusou a conexão na porta 9090 após várias tentativas. Reative o GoldHEN BinLoader e tente novamente.");
        }
        catch (SocketException ex)
        {
            Fail(bottom, "Falha de rede ao conectar ao PS4:9090 (" + ex.SocketErrorCode + ").");
        }
        catch (Exception ex)
        {
            // Do not echo exception messages: some providers include signed URLs.
            Fail(bottom, "Falha no fluxo automático (" + ex.GetType().Name + "). Confira o link, Worker e BinLoader.");
        }
        finally
        {
            lock (Gate) Busy = false;
        }
    }

    static async Task SendToBinLoaderAsync(string host, byte[] payload)
    {
        TcpClient client = null;
        SocketException lastSocket = null;
        bool timedOut = false;

        // Retry only the connection establishment. We never retry after a TCP
        // connection succeeds, because replaying a partially written ELF could
        // create duplicate or corrupted BinLoader/BGFT work.
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            client = new TcpClient();
            try
            {
                Task connect = client.ConnectAsync(host, 9090);
                Task completed = await Task.WhenAny(connect, Task.Delay(2500));
                if (!ReferenceEquals(completed, connect))
                {
                    timedOut = true;
                    client.Dispose();
                    client = null;
                    if (attempt < 4)
                    {
                        await Task.Delay(350 * attempt);
                        continue;
                    }
                    throw new TimeoutException("BinLoader connection timeout");
                }

                await connect;
                break;
            }
            catch (SocketException ex) when (IsRetryableConnectError(ex.SocketErrorCode) && attempt < 4)
            {
                lastSocket = ex;
                client.Dispose();
                client = null;
                await Task.Delay(350 * attempt);
            }
        }

        if (client == null)
        {
            if (lastSocket != null) throw lastSocket;
            if (timedOut) throw new TimeoutException("BinLoader connection timeout");
            throw new SocketException((int)SocketError.NotConnected);
        }

        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            await stream.WriteAsync(payload, 0, payload.Length);
            await stream.FlushAsync();
        }
    }

    static bool IsRetryableConnectError(SocketError error)
        => error == SocketError.ConnectionRefused ||
           error == SocketError.TimedOut ||
           error == SocketError.HostUnreachable ||
           error == SocketError.NetworkUnreachable;

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
        => Contains(text, "payload enviado") || Contains(text, "enviado ao ps4") ||
           Contains(text, "successfully sent") || Contains(text, "payload sent");

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
