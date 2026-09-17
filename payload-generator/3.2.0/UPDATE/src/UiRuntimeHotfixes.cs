using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Android.Content;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Runtime fixes derived from the 3.4.0 hardware smoke test:
///  - replace the inherited paste Button so the old handler cannot run;
///  - read Android's clipboard service directly, without focus/context-menu tricks;
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
            // UiHomeScreen's first 3.4.0 button owns an older Clicked handler.
            // Replace the visual control entirely so only this direct Android
            // clipboard implementation can run on hardware.
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

            paste.Clicked += (_, _) => PasteFromAndroidClipboard(url, bottom);
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

    static void PasteFromAndroidClipboard(Entry url, Label bottom)
    {
        if (url == null) return;
        try
        {
            Context context = Android.App.Application.Context;
            var clipboard = context.GetSystemService(Context.ClipboardService) as Android.Content.ClipboardManager;
            ClipData clip = clipboard?.PrimaryClip;
            if (clip == null || clip.ItemCount < 1)
            {
                if (bottom != null) bottom.Text = "A área de transferência está vazia.";
                return;
            }

            ClipData.Item item = clip.GetItemAt(0);
            string text = item?.CoerceToText(context)?.ToString()?.Trim() ?? "";
            if (text.Length == 0)
            {
                if (bottom != null) bottom.Text = "A área de transferência não contém texto.";
                return;
            }

            url.Text = text;
            url.CursorPosition = text.Length;
            url.SelectionLength = 0;
            url.Unfocus();
            if (bottom != null) bottom.Text = "Link colado. Toque em Validar PKG.";
        }
        catch (Exception ex)
        {
            if (bottom != null) bottom.Text = "Falha ao colar: " + ex.GetType().Name;
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
                byte[][] images = merged.ToArray();
                UiArtworkStore.Set(meta.ContentId, images);
                page.Dispatcher.Dispatch(() => Render(page, images));
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