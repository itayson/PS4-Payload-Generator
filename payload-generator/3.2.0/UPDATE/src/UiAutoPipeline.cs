using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// One-touch pipeline for the 3.4.0 Home screen.
///
/// The already hardware-tested MainPage handlers remain authoritative. This
/// coordinator only presses those same controls in sequence after the user
/// taps Colar:
///
///   Colar -> Validar PKG -> Gerar ELF -> Enviar ao BinLoader 9090
///
/// Manual buttons remain available as a fallback, but are not required for the
/// normal flow. The coordinator is deliberately session-only and never retries
/// a failed send automatically, preventing duplicate BGFT registrations.
/// </summary>
public static class UiAutoPipeline
{
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly object Gate = new object();

    static bool Pending;
    static bool Busy;
    static bool GenerateTriggered;
    static bool InjectTriggered;
    static string PendingUrl = "";

    static T Field<T>(MainPage page, string name) where T : class
        => typeof(MainPage).GetField(name, PrivateInstance)?.GetValue(page) as T;

    public static void Apply(MainPage page)
    {
        if (page?.Content == null) return;

        Button paste = FindPasteButton(page.Content);
        var url = Field<Entry>(page, "txtUrl");
        var generate = Field<Button>(page, "btnGerar");
        var inject = Field<Button>(page, "btnInjetar");
        var bottom = Field<Label>(page, "lblBottomStatus");

        if (paste != null)
        {
            // UiRuntimeHotfixes wires its paste/validate handler before this
            // coordinator. By the time this handler runs, txtUrl already holds
            // the clipboard value and validation is about to start.
            paste.Clicked += (_, _) => Arm(url);
        }

        if (generate != null)
        {
            generate.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(Button.IsEnabled) && generate.IsEnabled)
                    await ContinueAsync(page).ConfigureAwait(false);
            };
        }

        if (inject != null)
        {
            inject.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(Button.IsEnabled) && inject.IsEnabled)
                    await ContinueAsync(page).ConfigureAwait(false);
            };
        }

        if (bottom != null)
        {
            bottom.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(Label.Text)) return;
                string text = bottom.Text ?? "";

                if (LooksSuccessful(text))
                {
                    Reset();
                    return;
                }

                if (LooksLikeFailure(text))
                {
                    bool active;
                    lock (Gate) active = Pending && (GenerateTriggered || InjectTriggered);
                    if (active) Reset();
                    return;
                }

                // Some inherited handlers update status text before toggling
                // button state. This gives the pipeline a second continuation
                // signal without depending on one exact localized sentence.
                if (LooksValidated(text) || LooksGenerated(text))
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
            GenerateTriggered = false;
            InjectTriggered = false;
            PendingUrl = value;
        }
    }

    static async Task ContinueAsync(MainPage page)
    {
        Entry url = Field<Entry>(page, "txtUrl");
        Button generate = Field<Button>(page, "btnGerar");
        Button inject = Field<Button>(page, "btnInjetar");
        Label bottom = Field<Label>(page, "lblBottomStatus");
        Entry ip = Field<Entry>(page, "txtIp");

        lock (Gate)
        {
            if (!Pending || Busy) return;
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

            PkgMetadata meta = Field<PkgMetadata>(page, "_currentMeta");
            bool shouldGenerate;
            lock (Gate) shouldGenerate = Pending && !GenerateTriggered;

            if (shouldGenerate && generate != null && generate.IsEnabled && meta != null)
            {
                lock (Gate)
                {
                    if (!Pending || GenerateTriggered) return;
                    GenerateTriggered = true;
                }

                Dispatch(page, () =>
                {
                    if (bottom != null) bottom.Text = "PKG validado. Gerando ELF automaticamente...";
                });
                await Task.Delay(140).ConfigureAwait(false);

                bool started = await TriggerOnUiAsync(page, generate).ConfigureAwait(false);
                if (!started)
                    Fail(bottom, "PKG validado, mas não foi possível iniciar a geração automática do ELF.");
                return;
            }

            bool shouldInject;
            lock (Gate) shouldInject = Pending && GenerateTriggered && !InjectTriggered;

            if (shouldInject && inject != null && inject.IsEnabled)
            {
                if (string.IsNullOrWhiteSpace(ip?.Text))
                {
                    Fail(bottom, "Informe o IP do PS4 antes de usar o envio automático.");
                    return;
                }

                lock (Gate)
                {
                    if (!Pending || InjectTriggered) return;
                    InjectTriggered = true;
                }

                Dispatch(page, () =>
                {
                    if (bottom != null) bottom.Text = "ELF pronto. Enviando automaticamente ao PS4:9090...";
                });
                await Task.Delay(140).ConfigureAwait(false);

                bool started = await TriggerOnUiAsync(page, inject).ConfigureAwait(false);
                if (!started)
                    Fail(bottom, "ELF pronto, mas não foi possível iniciar o envio automático ao PS4.");
            }
        }
        finally
        {
            lock (Gate) Busy = false;
        }
    }

    static Task<bool> TriggerOnUiAsync(MainPage page, Button button)
    {
        var result = new TaskCompletionSource<bool>();
        Dispatch(page, () =>
        {
            try { result.TrySetResult(TriggerOriginalButton(button)); }
            catch (Exception ex) { result.TrySetException(ex); }
        });
        return result.Task;
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
            GenerateTriggered = false;
            InjectTriggered = false;
            PendingUrl = "";
        }
    }

    static bool TriggerOriginalButton(Button button)
    {
        try
        {
            Type type = button.GetType();
            while (type != null)
            {
                MethodInfo send = type.GetMethod(
                    "SendClicked",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null);
                if (send != null)
                {
                    send.Invoke(button, null);
                    return true;
                }
                type = type.BaseType;
            }

            object platform = button.Handler?.PlatformView;
            if (platform == null) return false;
            MethodInfo perform = platform.GetType().GetMethod("PerformClick", Type.EmptyTypes);
            if (perform == null) return false;
            object nativeResult = perform.Invoke(platform, null);
            return !(nativeResult is bool ok) || ok;
        }
        catch
        {
            return false;
        }
    }

    static bool LooksSuccessful(string text)
        => Contains(text, "payload enviado") || Contains(text, "enviado ao ps4") || Contains(text, "successfully sent") || Contains(text, "payload sent");

    static bool LooksValidated(string text)
        => Contains(text, "analisado com sucesso") || Contains(text, "validado com sucesso") || Contains(text, "pkg validado");

    static bool LooksGenerated(string text)
        => Contains(text, "elf gerado") || Contains(text, "payload salvo") || Contains(text, "payload saved");

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
