using System;
using System.Net.Http;
using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Runtime UI shell for the original MAUI MainPage.
/// Provider resolution, PKG validation, ELF generation and BinLoader delivery
/// remain owned by the original page + Integration hooks.
/// </summary>
public static class UiRedesign
{
    const string WorkerEndpoint = "https://drive.itaysondaniel.workers.dev";

    static readonly Color Background = Color.FromArgb("#030B14");
    static readonly Color Surface = Color.FromArgb("#071827");
    static readonly Color Surface2 = Color.FromArgb("#0A2238");
    static readonly Color Surface3 = Color.FromArgb("#0C2944");
    static readonly Color Blue = Color.FromArgb("#008CFF");
    static readonly Color Cyan = Color.FromArgb("#00D9FF");
    static readonly Color Green = Color.FromArgb("#00E6A7");
    static readonly Color Purple = Color.FromArgb("#8D4DFF");
    static readonly Color Red = Color.FromArgb("#FF4D6D");
    static readonly Color Text = Color.FromArgb("#F4F8FF");
    static readonly Color Muted = Color.FromArgb("#8FAFCD");
    static readonly Color Stroke = Color.FromArgb("#135982");
    static readonly Color StrokeBright = Color.FromArgb("#078EE3");

    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    sealed class Handles
    {
        public Entry Url;
        public Entry Ip;
        public Button Scan;
        public Button Clear;
        public Button Generate;
        public Button Inject;
        public Button LocalFile;
        public Image Icon;
        public Label Info;
        public Label Title;
        public Label BottomStatus;
    }

    static T Field<T>(MainPage page, string name) where T : class
        => typeof(MainPage).GetField(name, PrivateInstance)?.GetValue(page) as T;

    static Handles Resolve(MainPage page) => new Handles
    {
        Url = Field<Entry>(page, "txtUrl"),
        Ip = Field<Entry>(page, "txtIp"),
        Scan = Field<Button>(page, "btnScan"),
        Clear = Field<Button>(page, "btnLimpar"),
        Generate = Field<Button>(page, "btnGerar"),
        Inject = Field<Button>(page, "btnInjetar"),
        LocalFile = Field<Button>(page, "btnArquivoLocal"),
        Icon = Field<Image>(page, "imgIcon"),
        Info = Field<Label>(page, "lblInfo"),
        Title = Field<Label>(page, "lblTitulo"),
        BottomStatus = Field<Label>(page, "lblBottomStatus")
    };

    public static void Apply(MainPage page)
    {
        if (page == null || page.Content == null) return;

        var h = Resolve(page);
        ApplyKnownControlText(h);
        ApplyVisualTree(page.Content);
        page.BackgroundColor = Background;

        View home = page.Content;
        page.Content = null;

        var host = new ContentView
        {
            BackgroundColor = Background,
            Content = home,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        var root = new Grid { BackgroundColor = Background };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        Grid.SetRow(host, 1);
        root.Children.Add(host);

        var navigation = BuildNavigation(page, host, home, h);
        Grid.SetRow(navigation, 2);
        root.Children.Add(navigation);

        page.Content = root;
    }

    static void ApplyKnownControlText(Handles h)
    {
        if (h.Url != null)
            h.Url.Placeholder = "VikingFile, AkiraBox, Rootz, Mocha, FileKeeper ou link direto";
        if (h.Ip != null)
            h.Ip.Placeholder = "IP do PS4 · ex.: 192.168.0.10";
        if (h.Scan != null) h.Scan.Text = "Validar PKG";
        if (h.Generate != null) h.Generate.Text = "Gerar ELF";
        if (h.Inject != null) h.Inject.Text = "Enviar ao PS4";
        if (h.Clear != null) h.Clear.Text = "Limpar";
        if (h.LocalFile != null) h.LocalFile.Text = "Arquivo local";
        if (h.BottomStatus != null)
            h.BottomStatus.Text = "Tayson 3.3.1 • BGFT universal • BinLoader 9090";

        StyleAction(h.Scan, Green, Color.FromArgb("#031A16"));
        StyleAction(h.Generate, Blue, Text);
        StyleAction(h.Inject, Purple, Text);
        StyleOutline(h.Clear);
        StyleOutline(h.LocalFile);

        if (h.Title != null)
        {
            h.Title.TextColor = Text;
            h.Title.FontAttributes = FontAttributes.Bold;
            h.Title.FontSize = Math.Max(h.Title.FontSize, 21);
        }
        if (h.Info != null) h.Info.TextColor = Muted;
        if (h.BottomStatus != null) h.BottomStatus.TextColor = Cyan;
        if (h.Icon != null)
        {
            h.Icon.HeightRequest = 96;
            h.Icon.Aspect = Aspect.AspectFit;
        }
    }

    static void ApplyVisualTree(View view)
    {
        if (view == null) return;

        switch (view)
        {
            case ScrollView scroll:
                scroll.BackgroundColor = Background;
                if (scroll.Content != null) ApplyVisualTree(scroll.Content);
                break;
            case Frame frame:
                frame.BackgroundColor = Surface;
                frame.BorderColor = Stroke;
                frame.CornerRadius = 18;
                frame.HasShadow = false;
                if (frame.Content != null) ApplyVisualTree(frame.Content);
                break;
            case Entry entry:
                entry.BackgroundColor = Surface2;
                entry.TextColor = Text;
                entry.PlaceholderColor = Muted;
                entry.FontSize = Math.Max(entry.FontSize, 14);
                break;
            case Button button:
                if (button.BackgroundColor == null || button.BackgroundColor == Colors.Transparent)
                    StyleOutline(button);
                else
                {
                    button.CornerRadius = 14;
                    button.TextColor = Text;
                    button.FontAttributes = FontAttributes.Bold;
                }
                break;
            case Label label:
                if (label.TextColor == null || label.TextColor == Colors.Black)
                    label.TextColor = label.FontSize > 0 && label.FontSize <= 12 ? Muted : Text;
                break;
            case Layout layout:
                foreach (var child in layout.Children)
                    if (child is View childView) ApplyVisualTree(childView);
                break;
            case ContentView content:
                if (content.Content != null) ApplyVisualTree(content.Content);
                break;
        }
    }

    static View BuildHeader()
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(16, 11),
            BackgroundColor = Surface
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new VerticalStackLayout { Spacing = 0 };
        brand.Children.Add(new Label
        {
            Text = "TAYSON",
            TextColor = Text,
            FontSize = 23,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 2.2
        });
        brand.Children.Add(new Label
        {
            Text = "PS4 PAYLOAD GENERATOR",
            TextColor = Cyan,
            FontSize = 10,
            CharacterSpacing = 1.8
        });
        grid.Children.Add(brand);

        var badge = new Frame
        {
            Padding = new Thickness(10, 6),
            CornerRadius = 12,
            BorderColor = StrokeBright,
            BackgroundColor = Surface2,
            HasShadow = false,
            Content = new Label
            {
                Text = "BGFT • 9090",
                TextColor = Cyan,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold
            }
        };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        return grid;
    }

    static View BuildNavigation(MainPage page, ContentView host, View home, Handles h)
    {
        var bar = new Grid
        {
            BackgroundColor = Surface,
            ColumnSpacing = 6,
            Padding = new Thickness(8, 7, 8, 9)
        };
        for (int i = 0; i < 4; i++)
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Button homeButton = NavButton("Início");
        Button historyButton = NavButton("Histórico");
        Button workerButton = NavButton("Worker");
        Button settingsButton = NavButton("Ajustes");
        Button[] buttons = { homeButton, historyButton, workerButton, settingsButton };

        void Select(Button selected, View content)
        {
            host.Content = content;
            foreach (Button button in buttons)
            {
                bool active = ReferenceEquals(button, selected);
                button.TextColor = active ? Cyan : Muted;
                button.BackgroundColor = active ? Surface3 : Colors.Transparent;
                button.BorderColor = active ? Blue : Colors.Transparent;
                button.BorderWidth = active ? 1 : 0;
            }
        }

        homeButton.Clicked += (_, _) => Select(homeButton, home);
        historyButton.Clicked += (_, _) => Select(historyButton, BuildHistoryPage(page, h));
        workerButton.Clicked += (_, _) => Select(workerButton, BuildWorkerPage(page));
        settingsButton.Clicked += (_, _) => Select(settingsButton, BuildSettingsPage(page, h));

        for (int i = 0; i < buttons.Length; i++)
        {
            Grid.SetColumn(buttons[i], i);
            bar.Children.Add(buttons[i]);
        }
        Select(homeButton, home);
        return bar;
    }

    static Button NavButton(string text) => new Button
    {
        Text = text,
        BackgroundColor = Colors.Transparent,
        TextColor = Muted,
        FontSize = 11,
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 12,
        HeightRequest = 44,
        Padding = new Thickness(4, 2)
    };

    static View BuildHistoryPage(MainPage page, Handles h)
    {
        var body = PageBody("Histórico", "Sessão atual, validações e envios recentes");
        body.Children.Add(KpiRow(
            ("SESSÃO", "ATIVA", Cyan),
            ("PKG", CurrentMetadata(page) == null ? "—" : "VALIDADO", Green),
            ("PS4", string.IsNullOrWhiteSpace(h.Ip?.Text) ? "NÃO DEFINIDO" : h.Ip.Text, Blue)));

        PkgMetadata meta = CurrentMetadata(page);
        if (meta != null)
        {
            var current = Card();
            current.Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    SectionTitle("Pacote atual", Green),
                    new Label { Text = meta.DisplayName ?? "Pacote PS4", TextColor = Text, FontSize = 19, FontAttributes = FontAttributes.Bold },
                    new Label { Text = (meta.TitleId ?? "") + "  •  " + HumanSize(meta.ContentLength), TextColor = Muted, FontSize = 12 },
                    new Label { Text = ProviderName(meta.Url) + "  •  " + PackageKind(meta.PkgType), TextColor = Cyan, FontSize = 12 },
                    StatusPill("Pronto para gerar/enviar", Green)
                }
            };
            body.Children.Add(current);
        }
        else
        {
            body.Children.Add(InfoCard("Nenhum PKG validado nesta sessão", "Valide um link na tela Início para que o pacote apareça aqui."));
        }

        body.Children.Add(InfoCard("Fluxo preservado", "Android → valida PKG → gera ELF → GoldHEN BinLoader 9090 → BGFT."));
        body.Children.Add(InfoCard("Provedores", "Mocha • VikingFile • FileKeeper • Rootz • AkiraBox"));
        return Wrap(body);
    }

    static View BuildWorkerPage(MainPage page)
    {
        var body = PageBody("Worker", "Roteamento BGFT e transporte universal");
        var endpointStatus = new Label { Text = "● Aguardando teste", TextColor = Muted, FontSize = 12, FontAttributes = FontAttributes.Bold };

        var endpoint = Card();
        endpoint.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                SectionTitle("Endpoint Cloudflare", Cyan),
                new Label { Text = WorkerEndpoint, TextColor = Text, FontSize = 13 },
                endpointStatus
            }
        };
        body.Children.Add(endpoint);

        body.Children.Add(KpiRow(
            ("KV", "ROUTES", Green),
            ("WORKER", "v8", Cyan),
            ("RANGE", "206", Blue)));

        body.Children.Add(InfoCard("Rotas suportadas", "VikingFile R2 • Rootz R2 • AkiraBox sessão • Mocha fragment • FileKeeper direto"));
        body.Children.Add(InfoCard("Normalização BGFT", "HEAD → GET Range bytes=0-0 quando necessário, mantendo GET/Range e URLs assinadas intactas."));

        var test = new Button
        {
            Text = "Testar Worker",
            BackgroundColor = Blue,
            TextColor = Text,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 54
        };
        test.Clicked += async (_, _) =>
        {
            test.IsEnabled = false;
            endpointStatus.Text = "● Testando conectividade…";
            endpointStatus.TextColor = Cyan;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                string health = await client.GetStringAsync(WorkerEndpoint + "/health");
                bool ok = health.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0;
                endpointStatus.Text = ok ? "● Online • health OK" : "● Resposta recebida";
                endpointStatus.TextColor = ok ? Green : Cyan;
            }
            catch (Exception ex)
            {
                endpointStatus.Text = "● Offline • " + ex.GetType().Name;
                endpointStatus.TextColor = Red;
            }
            finally { test.IsEnabled = true; }
        };
        body.Children.Add(test);
        return Wrap(body);
    }

    static View BuildSettingsPage(MainPage page, Handles h)
    {
        var body = PageBody("Ajustes", "Conexão, preferências e segurança");
        var ip = new Entry
        {
            Text = h.Ip?.Text ?? "",
            Placeholder = "192.168.0.10",
            Keyboard = Keyboard.Numeric,
            BackgroundColor = Surface2,
            TextColor = Text,
            PlaceholderColor = Muted
        };
        var connection = Card();
        connection.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                SectionTitle("Conexão com PS4", Blue),
                new Label { Text = "IP do PS4", TextColor = Muted, FontSize = 11 },
                ip,
                new Label { Text = "BinLoader • TCP 9090", TextColor = Cyan, FontSize = 12, FontAttributes = FontAttributes.Bold }
            }
        };
        body.Children.Add(connection);

        body.Children.Add(InfoCard("Cloudflare Worker", WorkerEndpoint + "\nWorker v8 • KV binding ROUTES"));
        body.Children.Add(ToggleCard("Tema escuro", "Interface Tayson neon", true));
        body.Children.Add(ToggleCard("Validação automática de PKG", "Confirma magic, Content ID, tamanho e Range antes do ELF", true));
        body.Children.Add(ToggleCard("Navegador interno", "Usado em fluxos AkiraBox/Rootz quando necessário", true));

        body.Children.Add(KpiRow(
            ("BINLOADER", "9090", Green),
            ("BGFT", "PRONTO", Cyan),
            ("KV", "ROUTES", Blue)));

        var save = new Button
        {
            Text = "Salvar configurações",
            BackgroundColor = Blue,
            TextColor = Text,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 54
        };
        save.Clicked += async (_, _) =>
        {
            if (h.Ip != null) h.Ip.Text = (ip.Text ?? "").Trim();
            await page.DisplayAlert("Tayson", "IP aplicado à sessão atual. Porta BinLoader mantida em 9090.", "OK");
        };
        body.Children.Add(save);
        return Wrap(body);
    }

    static VerticalStackLayout PageBody(string title, string subtitle)
    {
        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(14, 14, 14, 20),
            BackgroundColor = Background
        };
        var hero = Card();
        hero.BorderColor = StrokeBright;
        hero.Content = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = title, TextColor = Text, FontSize = 25, FontAttributes = FontAttributes.Bold },
                new Label { Text = subtitle, TextColor = Cyan, FontSize = 12 }
            }
        };
        body.Children.Add(hero);
        return body;
    }

    static ScrollView Wrap(View content) => new ScrollView
    {
        BackgroundColor = Background,
        Content = content
    };

    static Frame Card() => new Frame
    {
        Padding = new Thickness(14),
        CornerRadius = 18,
        BorderColor = Stroke,
        BackgroundColor = Surface,
        HasShadow = false,
        HorizontalOptions = LayoutOptions.Fill
    };

    static Frame InfoCard(string title, string text)
    {
        var frame = Card();
        frame.Content = new VerticalStackLayout
        {
            Spacing = 5,
            Children =
            {
                SectionTitle(title, Cyan),
                new Label { Text = text, TextColor = Muted, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap }
            }
        };
        return frame;
    }

    static View ToggleCard(string title, string detail, bool enabled)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new VerticalStackLayout
        {
            Spacing = 1,
            Children =
            {
                new Label { Text = title, TextColor = Text, FontSize = 14, FontAttributes = FontAttributes.Bold },
                new Label { Text = detail, TextColor = Muted, FontSize = 10 }
            }
        };
        grid.Children.Add(text);
        var toggle = new Switch { IsToggled = enabled, OnColor = Blue, ThumbColor = Text };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        var frame = Card();
        frame.Content = grid;
        return frame;
    }

    static View KpiRow(params (string Title, string Value, Color Color)[] items)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var frame = Card();
            frame.Padding = new Thickness(10, 11);
            frame.Content = new VerticalStackLayout
            {
                Spacing = 3,
                Children =
                {
                    new Label { Text = items[i].Title, TextColor = Muted, FontSize = 9, FontAttributes = FontAttributes.Bold },
                    new Label { Text = items[i].Value, TextColor = items[i].Color, FontSize = 13, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation }
                }
            };
            Grid.SetColumn(frame, i);
            grid.Children.Add(frame);
        }
        return grid;
    }

    static Label SectionTitle(string text, Color color) => new Label
    {
        Text = text,
        TextColor = color,
        FontSize = 15,
        FontAttributes = FontAttributes.Bold
    };

    static View StatusPill(string text, Color color)
    {
        var frame = new Frame
        {
            Padding = new Thickness(10, 6),
            CornerRadius = 12,
            BorderColor = color,
            BackgroundColor = Surface2,
            HasShadow = false,
            HorizontalOptions = LayoutOptions.Start
        };
        frame.Content = new Label { Text = "● " + text, TextColor = color, FontSize = 11, FontAttributes = FontAttributes.Bold };
        return frame;
    }

    static void StyleAction(Button button, Color background, Color foreground)
    {
        if (button == null) return;
        button.BackgroundColor = background;
        button.TextColor = foreground;
        button.BorderColor = background;
        button.BorderWidth = 1;
        button.CornerRadius = 14;
        button.FontAttributes = FontAttributes.Bold;
        button.HeightRequest = Math.Max(button.HeightRequest, 50);
    }

    static void StyleOutline(Button button)
    {
        if (button == null) return;
        button.BackgroundColor = Surface2;
        button.TextColor = Cyan;
        button.BorderColor = StrokeBright;
        button.BorderWidth = 1;
        button.CornerRadius = 14;
        button.FontAttributes = FontAttributes.Bold;
    }

    static PkgMetadata CurrentMetadata(MainPage page)
        => typeof(MainPage).GetField("_currentMeta", PrivateInstance)?.GetValue(page) as PkgMetadata;

    static string ProviderName(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Link direto";
        string value = url.ToLowerInvariant();
        if (value.Contains("akirabox")) return "AkiraBox";
        if (value.Contains("vikingfile") || value.Contains("r2.cloudflarestorage.com")) return "VikingFile / R2";
        if (value.Contains("rootz")) return "Rootz";
        if (value.Contains("mocha")) return "Mocha";
        if (value.Contains("filekeeper")) return "FileKeeper";
        return "Link direto";
    }

    static string PackageKind(string type)
        => type == "PS4PATCH" ? "Atualização" : type == "PS4AC" ? "DLC" : "Jogo";

    static string HumanSize(long bytes)
    {
        if (bytes <= 0) return "Tamanho desconhecido";
        double gb = bytes / 1024d / 1024d / 1024d;
        return gb >= 1 ? gb.ToString("0.00") + " GB" : (bytes / 1024d / 1024d).ToString("0.0") + " MB";
    }
}
