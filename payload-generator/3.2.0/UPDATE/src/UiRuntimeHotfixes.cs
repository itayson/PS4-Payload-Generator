using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Runtime fixes derived from the 3.4.0 hardware smoke test:
///  - retry the native Android paste action after the Entry receives focus;
///  - refresh artwork after the original MainPage has committed _currentMeta;
///  - fall back to the official PS4 title metadata service when a provider PKG
///    does not expose enough artwork through the bounded PKG ranges.
/// </summary>
public static class UiRuntimeHotfixes
{
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Color Cyan = Color.FromArgb("#00D9FF");
    static readonly Color Stroke = Color.FromArgb("#135982");
    static readonly Color Surface2 = Color.FromArgb("#0A2238");
    static readonly HashSet<string> Enriching = new HashSet<string>(StringComparer.Ordinal);

    static T Field<T>(MainPage page, string name) where T : class
        => typeof(MainPage).GetField(name, PrivateInstance)?.GetValue(page) as T;

    public static void Apply(MainPage page)
    {
        if (page?.Content == null) return;
        var url = Field<Entry>(page, "txtUrl");
        var bottom = Field<Label>(page, "lblBottomStatus");
        var generate = Field<Button>(page, "btnGerar");

        Button paste = FindButton(page.Content, "Colar");
        if (paste != null && paste.AutomationId != "TaysonPasteDirect")
        {
            paste.AutomationId = "TaysonPasteDirect";
            paste.Clicked += async (_, _) => await RetryNativePasteAsync(url, bottom);
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

    static async Task RetryNativePasteAsync(Entry url, Label bottom)
    {
        if (url == null) return;
        try
        {
            string before = url.Text ?? "";
            url.Focus();
            url.CursorPosition = 0;
            url.SelectionLength = before.Length;

            // The first 3.4.0 build invoked paste immediately while the Button
            // still owned input focus. On real Android hardware that could be
            // rejected. Give the native EditText a short window to own focus,
            // then invoke android.R.id.paste through the existing platform view.
            await Task.Delay(140);
            object platform = url.Handler?.PlatformView;
            if (platform == null)
            {
                if (bottom != null) bottom.Text = "Campo de link indisponível.";
                return;
            }

            MethodInfo method = null;
            Type type = platform.GetType();
            while (type != null && method == null)
            {
                method = type.GetMethod(
                    "OnTextContextMenuItem",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int) },
                    null);
                type = type.BaseType;
            }

            object result = method?.Invoke(platform, new object[] { 16908322 }); // android.R.id.paste
            await Task.Delay(60);
            string after = url.Text ?? "";
            bool pasted = after.Length > 0 && (!string.Equals(after, before, StringComparison.Ordinal) || before.Length == 0) &&
                          (!(result is bool ok) || ok);

            if (pasted)
            {
                url.CursorPosition = after.Length;
                url.SelectionLength = 0;
                url.Unfocus();
                if (bottom != null) bottom.Text = "Link colado. Toque em Validar PKG.";
            }
            else if (bottom != null)
            {
                bottom.Text = "Não foi possível colar automaticamente.";
            }
        }
        catch
        {
            if (bottom != null) bottom.Text = "Não foi possível ler a área de transferência.";
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
            if (merged.Count < 2)
                AddUnique(merged, await TmdbArtworkClient.FetchAsync(meta.TitleId));

            if (merged.Count > 0)
            {
                UiArtworkStore.Set(meta.ContentId, merged.ToArray());
                page.Dispatcher.Dispatch(() => Render(page, merged.ToArray()));
            }
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
                Aspect = Aspect.AspectFill,
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
