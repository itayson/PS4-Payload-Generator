using System;
using System.Reflection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Final polish and runtime shell overrides for the prebuilt MainPage.
/// </summary>
public static class UiPolish
{
    static readonly Color Text = Color.FromArgb("#F4F8FF");
    static readonly Color Muted = Color.FromArgb("#8FAFCD");
    static readonly Color Cyan = Color.FromArgb("#00D9FF");
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Apply(MainPage page)
    {
        if (page?.Content == null) return;
        Visit(page.Content);
        AttachShellOverrides(page);
    }

    static void Visit(View view)
    {
        if (view == null) return;

        if (view is Label label)
        {
            PolishLabel(label);
            label.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Text") PolishLabel(label);
            };
        }
        else if (view is Button button)
        {
            PolishButton(button);
            button.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Text") PolishButton(button);
            };
        }
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

    static void AttachShellOverrides(MainPage page)
    {
        if (page.Content is not Grid root) return;

        ContentView host = null;
        Grid navigation = null;
        foreach (var child in root.Children)
        {
            if (child is ContentView content && Grid.GetRow(content) == 1) host = content;
            else if (child is Grid grid && Grid.GetRow(grid) == 2) navigation = grid;
        }
        if (host == null || navigation == null) return;

        Button home = null;
        Button history = null;
        Button worker = null;
        Button settings = null;
        foreach (var child in navigation.Children)
        {
            if (child is not Button button) continue;
            if (button.Text == "Início") home = button;
            else if (button.Text == "Histórico") history = button;
            else if (button.Text == "Worker") worker = button;
            else if (button.Text == "Ajustes") settings = button;
        }

        var bottom = typeof(MainPage).GetField("lblBottomStatus", PrivateInstance)?.GetValue(page) as Label;
        if (bottom != null)
            bottom.Text = "Tayson 3.3.0 • BGFT universal • BinLoader 9090";

        View modernHome = UiHomeScreen.Build(page);
        host.Content = modernHome;

        if (home != null)
            home.Clicked += (_, _) => host.Content = modernHome;

        if (history != null)
        {
            history.Clicked += (_, _) =>
            {
                var ip = typeof(MainPage).GetField("txtIp", PrivateInstance)?.GetValue(page) as Entry;
                host.Content = UiHistoryScreen.Build(page, ip?.Text ?? "");
            };
        }

        if (worker != null)
            worker.Clicked += (_, _) => host.Content = UiWorkerScreen.Build();

        if (settings != null)
            settings.Clicked += (_, _) => host.Content = UiSettingsScreen.Build(page);
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

    static void PolishButton(Button button)
    {
        string value = (button.Text ?? "").Trim();
        if (value.Length == 0) return;

        if (value.Equals("Scan PKG", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Analyze PKG", StringComparison.OrdinalIgnoreCase))
        {
            button.Text = "Validar PKG";
            return;
        }

        if (value.Equals("Scanning...", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Scanning…", StringComparison.OrdinalIgnoreCase))
        {
            button.Text = "Validando…";
            return;
        }

        if (value.Equals("Generate ELF", StringComparison.OrdinalIgnoreCase))
        {
            button.Text = "Gerar ELF";
            return;
        }

        if (value.Equals("Generating...", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Generating…", StringComparison.OrdinalIgnoreCase))
        {
            button.Text = "Gerando…";
            return;
        }

        if (value.Equals("Inject Directly to PS4", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Send to PS4", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Inject to PS4", StringComparison.OrdinalIgnoreCase))
        {
            button.Text = "Enviar ao PS4";
            return;
        }

        if (value.Equals("Sending...", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Sending…", StringComparison.OrdinalIgnoreCase))
        {
            button.Text = "Enviando…";
            return;
        }

        if (value.Equals("Clear", StringComparison.OrdinalIgnoreCase))
        {
            button.Text = "Limpar";
            return;
        }

        if (value.Equals("Local file", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Local File", StringComparison.OrdinalIgnoreCase))
            button.Text = "Arquivo local";
    }

    static void PolishLabel(Label label)
    {
        string value = (label.Text ?? "").Trim();
        if (value.Length == 0) return;

        if (value.IndexOf("Dev. by Phoenixx", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            label.Text = "Dev. By TaySon";
            label.TextColor = Text;
            return;
        }

        if (value.IndexOf("PS4 Payload Generator", StringComparison.OrdinalIgnoreCase) >= 0 &&
            value.IndexOf("Tayson 3.2.0", StringComparison.OrdinalIgnoreCase) >= 0)
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

        if (value.Equals("Waiting for link...", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Waiting for link…", StringComparison.OrdinalIgnoreCase))
        {
            label.Text = "Aguardando link…";
            label.TextColor = Text;
            return;
        }

        if (value.Equals("Reading metadata...", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Reading metadata…", StringComparison.OrdinalIgnoreCase))
        {
            label.Text = "Lendo metadados…";
            label.TextColor = Text;
            return;
        }

        if (value.Equals("Scan completed successfully!", StringComparison.OrdinalIgnoreCase))
        {
            label.Text = "PKG validado com sucesso.";
            return;
        }

        if (value.Equals("Payload sent; confirm download starts on TV.", StringComparison.OrdinalIgnoreCase))
        {
            label.Text = "Payload enviado; confirme o início do download na TV.";
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
