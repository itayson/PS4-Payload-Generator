using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Small post-processing pass for text/resources that belong to the original
/// prebuilt MainPage. Keeping this separate from UiRedesign avoids touching the
/// proven event handlers while removing legacy branding and untranslated copy.
/// </summary>
public static class UiPolish
{
    static readonly Color Text = Color.FromArgb("#F4F8FF");
    static readonly Color Muted = Color.FromArgb("#8FAFCD");
    static readonly Color Cyan = Color.FromArgb("#00D9FF");

    public static void Apply(MainPage page)
    {
        if (page?.Content == null) return;
        Visit(page.Content);
    }

    static void Visit(View view)
    {
        if (view == null) return;

        if (view is Label label)
            PolishLabel(label);
        else if (view is Entry entry)
            PolishEntry(entry);

        if (view is ScrollView scroll && scroll.Content != null)
            Visit(scroll.Content);
        else if (view is Frame frame && frame.Content != null)
            Visit(frame.Content);
        else if (view is ContentView content && content.Content != null)
            Visit(content.Content);
        else if (view is Layout layout)
            foreach (var child in layout.Children)
                if (child is View childView) Visit(childView);
    }

    static void PolishEntry(Entry entry)
    {
        string placeholder = entry.Placeholder ?? "";
        if (placeholder.IndexOf("VikingFile", StringComparison.OrdinalIgnoreCase) >= 0 ||
            placeholder.IndexOf("AkiraBox", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            entry.Placeholder = "Cole ou compartilhe o link do PKG";
            entry.PlaceholderColor = Muted;
        }
    }

    static void PolishLabel(Label label)
    {
        string value = (label.Text ?? "").Trim();
        if (value.Length == 0) return;

        // The new shell already owns app branding. Remove duplicated legacy
        // header/credit from the embedded Phoenixx page.
        if (value.IndexOf("Dev. by Phoenixx", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (value.IndexOf("PS4 Payload Generator", StringComparison.OrdinalIgnoreCase) >= 0 &&
             value.IndexOf("Tayson 3.2.0", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            label.IsVisible = false;
            label.HeightRequest = 0;
            label.Margin = 0;
            return;
        }

        if (value.Equals("PKG File Link:", StringComparison.OrdinalIgnoreCase))
        {
            label.Text = "Link do PKG";
            label.TextColor = Cyan;
            label.FontAttributes = FontAttributes.Bold;
            return;
        }

        if (value.Equals("Waiting for link...", StringComparison.OrdinalIgnoreCase))
        {
            label.Text = "Aguardando link…";
            label.TextColor = Text;
            return;
        }

        if (value.StartsWith("PS4 IP (Port 9090)", StringComparison.OrdinalIgnoreCase))
        {
            label.Text = "IP do PS4 · porta 9090";
            label.TextColor = Cyan;
            label.FontAttributes = FontAttributes.Bold;
            return;
        }

        if (value.StartsWith("Title ID: -", StringComparison.OrdinalIgnoreCase))
        {
            label.Text = "Title ID: —  •  Tipo: —";
            label.TextColor = Muted;
        }
    }
}
