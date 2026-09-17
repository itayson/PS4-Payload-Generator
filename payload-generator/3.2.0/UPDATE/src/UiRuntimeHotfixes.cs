using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Android.Runtime;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Runtime fixes and one-touch helpers derived from hardware smoke tests:
///  - replace the inherited paste Button so the old handler cannot run;
///  - read Android's clipboard service directly through JNI;
///  - automatically start PKG validation after a successful paste;
///  - accept multiple newline-separated links and queue them in memory;
///  - refresh artwork after MainPage commits _currentMeta;
///  - optionally enrich sparse PKGs with PS4 title-database artwork.
/// </summary>
public static class UiRuntimeHotfixes
{
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Color Cyan = Color.FromArgb("#00D9FF");
    static readonly Color Stroke = Color.FromArgb("#135982");
    static readonly Color Surface2 = Color.FromArgb("#0A2238");
    static readonly HashSet<string> Enriching = new HashSet<string>(StringComparer.Ordinal);
    static bool AutoValidationPending;
    static int BatchTotal;
    static int BatchCompleted;

    static T Field<T>(MainPage page, string name) where T : class
        => typeof(MainPage).GetField(name, PrivateInstance)?.GetValue(page) as T;

    public static void Apply(MainPage page)
    {
        if (page?.Content == null) return;
        var url = Field<Entry>(page, "txtUrl");
        var bottom = Field<Label>(page, "lblBottomStatus");
        var generate = Field<Button>(page, "btnGerar");
        var scan = Field<Button>(page, "btnScan");

        Button paste = FindButton(page.Content, "Colar");
        if (paste != null && paste.AutomationId != "TaysonPasteDirect")
        {
            if (paste.Parent is Grid grid)
            {
                int column = Grid.GetColumn(paste);
                int row = Grid.GetRow(paste);
                int columnSpan = Grid.GetColumnSpan(paste);
                int rowSpan = Grid.GetRowSpan(paste);
                var replacement = new Button
                {
                    Text = paste.Text,
                    WidthRequest = paste.WidthRequest,
                    HeightRequest = paste.HeightRequest,
                    Padding = paste.Padding,
                    FontSize = paste.FontSize,
                    FontAttributes = paste.FontAttributes,
                    BackgroundColor = paste.BackgroundColor,
                    TextColor = paste.TextColor,
                    BorderColor = paste.BorderColor,
                    BorderWidth = paste.BorderWidth,
                    CornerRadius = paste.CornerRadius,
                    HorizontalOptions = paste.HorizontalOptions,
                    VerticalOptions = paste.VerticalOptions,
                    AutomationId = "TaysonPasteDirect"
                };
                Grid.SetColumn(replacement, column);
                Grid.SetRow(replacement, row);
                Grid.SetColumnSpan(replacement, columnSpan);
                Grid.SetRowSpan(replacement, rowSpan);
                grid.Children.Remove(paste);
                grid.Children.Add(replacement);
                paste = replacement;
            }
            else
            {
                paste.AutomationId = "TaysonPasteDirect";
            }

            paste.Clicked += async (_, _) => await PasteAndValidateAsync(url, bottom, scan);
        }

        if (generate != null && generate.AutomationId != "TaysonArtworkRefresh")
        {
            generate.AutomationId = "TaysonArtworkRefresh";
            generate.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == "IsEnabled" && generate.IsEnabled)
                    await RefreshArtworkAsync(page);
            };
        }

        if (bottom != null && bottom.AutomationId != "TaysonArtworkStatusRefresh")
        {
            bottom.AutomationId = "TaysonArtworkStatusRefresh";
            bottom.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName != "Text") return;
                string text = bottom.Text ?? "";
                if (text.IndexOf("analisado com sucesso", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("validado com sucesso", StringComparison.OrdinalIgnoreCase) >= 0)
                    await RefreshArtworkAsync(page);
            };
        }
    }

    static async Task PasteAndValidateAsync(Entry url, Label bottom, Button scan)
    {
        if (url == null || AutoValidationPending) return;

        string clipboard = ReadAndroidClipboard(bottom);
        if (string.IsNullOrWhiteSpace(clipboard)) return;

        string text = UiSendQueue.LoadClipboard(clipboard, out int total);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (bottom != null) bottom.Text = "A área de transferência não contém um link HTTP/HTTPS válido.";
            return;
        }

        BatchTotal = Math.Max(1, total);
        BatchCompleted = 0;
        UiDiagnostics.Add("FILA", BatchTotal > 1 ? "Fila carregada com " + BatchTotal + " links." : "Envio único carregado.");

        url.Text = text;
        url.CursorPosition = text.Length;
        url.SelectionLength = 0;
        url.Unfocus();

        if (scan == null)
        {
            if (bottom != null) bottom.Text = "Link colado. Toque em Validar PKG.";
            return;
        }

        AutoValidationPending = true;
        try
        {
            if (bottom != null)
                bottom.Text = BatchTotal > 1
                    ? "Fila " + 1 + "/" + BatchTotal + ": validando PKG automaticamente..."
                    : "Link colado. Validando PKG automaticamente...";

            // Give TextChanged/Invalidate one UI turn to finish before invoking
            // the original, hardware-tested validation handler.
            await Task.Delay(120);

            if (!scan.IsEnabled)
            {
                if (bottom != null) bottom.Text = "Link colado. Validação já está em andamento.";
                return;
            }

            if (!TriggerOriginalButton(scan))
            {
                if (bottom != null) bottom.Text = "Link colado. Toque em Validar PKG.";
            }
        }
        finally
        {
            AutoValidationPending = false;
        }
    }

    public static async Task<bool> MarkCurrentCompletedAndStartNextAsync(MainPage page)
    {
        if (page == null) return false;
        BatchCompleted++;

        if (!UiSendQueue.TryPeekNext(out string next, out int remainingAfterCurrent))
        {
            if (BatchTotal > 1)
            {
                Label bottomDone = Field<Label>(page, "lblBottomStatus");
                if (bottomDone != null)
                    bottomDone.Text = "Fila concluída: " + BatchCompleted + "/" + BatchTotal + " payloads enviados ao PS4.";
                UiDiagnostics.Add("FILA", "Fila concluída: " + BatchCompleted + "/" + BatchTotal + ".");
            }
            BatchTotal = 0;
            BatchCompleted = 0;
            return false;
        }

        Entry url = Field<Entry>(page, "txtUrl");
        Button scan = Field<Button>(page, "btnScan");
        Label bottom = Field<Label>(page, "lblBottomStatus");
        if (url == null || scan == null)
        {
            UiDiagnostics.Add("FILA", "Fila pausada porque os controles de validação não estão disponíveis.");
            return false;
        }

        int current = BatchCompleted + 1;
        url.Text = next;
        url.CursorPosition = next.Length;
        url.SelectionLength = 0;
        url.Unfocus();

        if (bottom != null)
            bottom.Text = "Fila " + current + "/" + BatchTotal + ": preparando próximo PKG...";

        // The original validation button may still be unwinding after the
        // previous item. Wait briefly instead of consuming the queued URL.
        for (int attempt = 0; attempt < 5 && !scan.IsEnabled; attempt++)
            await Task.Delay(180);

        if (!scan.IsEnabled)
        {
            if (bottom != null) bottom.Text = "Fila pausada: a validação anterior ainda está ocupada. O próximo link foi preservado.";
            UiDiagnostics.Add("FILA", "Próximo item preservado porque a validação permaneceu ocupada.");
            return false;
        }

        if (!TriggerOriginalButton(scan))
        {
            if (bottom != null) bottom.Text = "Fila pausada: não foi possível iniciar a próxima validação. O link foi preservado.";
            UiDiagnostics.Add("FILA", "Próximo item preservado porque o botão de validação não pôde ser acionado.");
            return false;
        }

        if (!UiSendQueue.CommitNext(next, out int remaining))
        {
            if (bottom != null) bottom.Text = "Fila pausada: o estado interno da fila mudou durante a validação.";
            UiDiagnostics.Add("FILA", "Falha ao confirmar avanço transacional da fila.");
            return false;
        }

        UiAutoPipeline.ArmExternal(next);
        UiDiagnostics.Add("FILA", "Iniciando item " + current + "/" + BatchTotal + "; pendentes após este: " + remaining + ".");
        if (bottom != null)
            bottom.Text = "Fila " + current + "/" + BatchTotal + ": validando próximo PKG...";
        return true;
    }

    static string ReadAndroidClipboard(Label bottom)
    {
        IntPtr serviceClass = IntPtr.Zero;
        IntPtr charsClass = IntPtr.Zero;
        IntPtr chars = IntPtr.Zero;
        try
        {
            var context = Android.App.Application.Context;
            Java.Lang.Object service = context.GetSystemService("clipboard");
            if (service == null)
            {
                if (bottom != null) bottom.Text = "Serviço da área de transferência indisponível.";
                return "";
            }

            serviceClass = JNIEnv.GetObjectClass(service.Handle);
            IntPtr getText = JNIEnv.GetMethodID(serviceClass, "getText", "()Ljava/lang/CharSequence;");
            if (getText == IntPtr.Zero)
            {
                if (bottom != null) bottom.Text = "Área de transferência incompatível.";
                return "";
            }

            chars = JNIEnv.CallObjectMethod(service.Handle, getText);
            if (chars == IntPtr.Zero)
            {
                if (bottom != null) bottom.Text = "A área de transferência está vazia.";
                return "";
            }

            charsClass = JNIEnv.GetObjectClass(chars);
            IntPtr toString = JNIEnv.GetMethodID(charsClass, "toString", "()Ljava/lang/String;");
            IntPtr javaString = JNIEnv.CallObjectMethod(chars, toString);
            string text = javaString == IntPtr.Zero ? "" : JNIEnv.GetString(javaString, JniHandleOwnership.TransferLocalRef);
            text = (text ?? "").Trim();
            if (text.Length == 0 && bottom != null) bottom.Text = "A área de transferência não contém texto.";
            return text;
        }
        catch (Exception ex)
        {
            if (bottom != null) bottom.Text = "Falha ao colar: " + ex.GetType().Name;
            UiDiagnostics.Add("CLIPBOARD", "Falha ao ler clipboard: " + ex.GetType().Name);
            return "";
        }
        finally
        {
            if (charsClass != IntPtr.Zero) JNIEnv.DeleteLocalRef(charsClass);
            if (chars != IntPtr.Zero) JNIEnv.DeleteLocalRef(chars);
            if (serviceClass != IntPtr.Zero) JNIEnv.DeleteLocalRef(serviceClass);
        }
    }

    static bool TriggerOriginalButton(Button button)
    {
        try
        {
            // Prefer MAUI's own event dispatcher when available. It reaches the
            // already-wired MainPage handler without duplicating validation code.
            Type type = button.GetType();
            while (type != null)
            {
                MethodInfo send = type.GetMethod("SendClicked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (send != null)
                {
                    send.Invoke(button, null);
                    return true;
                }
                type = type.BaseType;
            }

            // Fallback to the Android native view. performClick() travels back
            // through the MAUI handler and fires the same Clicked event.
            object platform = button.Handler?.PlatformView;
            if (platform == null) return false;
            MethodInfo perform = platform.GetType().GetMethod("PerformClick", Type.EmptyTypes);
            if (perform == null) return false;
            object result = perform.Invoke(platform, null);
            return !(result is bool ok) || ok;
        }
        catch (Exception ex)
        {
            UiDiagnostics.Add("UI", "Falha ao acionar botão original: " + ex.GetType().Name);
            return false;
        }
    }

    static async Task RefreshArtworkAsync(MainPage page)
    {
        var meta = Field<PkgMetadata>(page, "_currentMeta");
        if (meta == null || string.IsNullOrWhiteSpace(meta.ContentId)) return;

        lock (Enriching)
        {
            if (Enriching.Contains(meta.ContentId)) return;
            Enriching.Add(meta.ContentId);
        }

        try
        {
            var merged = new List<byte[]>();
            AddUnique(merged, UiArtworkStore.Get(meta.ContentId));
            if (merged.Count < 4)
                AddUnique(merged, await TmdbArtworkClient.FetchAsync(meta.TitleId));

            if (merged.Count > 0)
            {
                byte[][] images = merged.ToArray();
                UiArtworkStore.Set(meta.ContentId, images);
                page.Dispatcher.Dispatch(() => Render(page, images));
            }
        }
        catch (Exception ex)
        {
            UiDiagnostics.Add("ARTWORK", "Fallback de artwork falhou: " + ex.GetType().Name);
        }
        finally
        {
            lock (Enriching) Enriching.Remove(meta.ContentId);
        }
    }

    static void AddUnique(List<byte[]> target, byte[][] source)
    {
        if (source == null) return;
        foreach (byte[] candidate in source)
        {
            if (candidate == null || candidate.Length < 8) continue;
            bool duplicate = false;
            foreach (byte[] item in target)
            {
                if (item.Length != candidate.Length) continue;
                int probe = Math.Min(24, item.Length);
                bool same = true;
                for (int i = 0; i < probe; i++)
                {
                    if (item[i] != candidate[i] || item[item.Length - 1 - i] != candidate[candidate.Length - 1 - i])
                    {
                        same = false;
                        break;
                    }
                }
                if (same) { duplicate = true; break; }
            }
            if (!duplicate) target.Add(candidate);
            if (target.Count >= 5) return;
        }
    }

    static void Render(MainPage page, byte[][] images)
    {
        if (images == null || images.Length == 0) return;
        var icon = Field<Image>(page, "imgIcon");
        if (icon != null)
        {
            byte[] first = images[0];
            icon.Source = ImageSource.FromStream(() => new MemoryStream(first, false));
        }

        Label title = FindLabel(page.Content, "Galeria do PKG");
        if (title?.Parent is not Layout parent) return;
        int index = parent.Children.IndexOf(title);
        ScrollView scroll = null;
        for (int i = index + 1; i < parent.Children.Count; i++)
        {
            if (parent.Children[i] is ScrollView found) { scroll = found; break; }
            if (parent.Children[i] is Label) break;
        }
        if (scroll?.Content is not HorizontalStackLayout host) return;

        host.Children.Clear();
        if (images.Length <= 1)
        {
            title.IsVisible = false;
            scroll.IsVisible = false;
            return;
        }

        title.Text = "Galeria do PKG • " + images.Length + " imagens";
        for (int i = 0; i < images.Length; i++)
        {
            byte[] bytes = images[i];
            var image = new Image
            {
                WidthRequest = i == 0 ? 92 : 132,
                HeightRequest = 78,
                Aspect = Microsoft.Maui.Aspect.AspectFill,
                Source = ImageSource.FromStream(() => new MemoryStream(bytes, false))
            };
            host.Children.Add(new Frame
            {
                Padding = 2,
                CornerRadius = 12,
                BorderColor = i == 0 ? Cyan : Stroke,
                BackgroundColor = Surface2,
                HasShadow = false,
                Content = image
            });
        }
        title.IsVisible = true;
        scroll.IsVisible = true;
    }

    static Button FindButton(View view, string text)
    {
        if (view is Button button && string.Equals(button.Text, text, StringComparison.OrdinalIgnoreCase)) return button;
        foreach (View child in Children(view))
        {
            Button found = FindButton(child, text);
            if (found != null) return found;
        }
        return null;
    }

    static Label FindLabel(View view, string prefix)
    {
        if (view is Label label && (label.Text ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return label;
        foreach (View child in Children(view))
        {
            Label found = FindLabel(child, prefix);
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
