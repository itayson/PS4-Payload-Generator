using System;
using System.IO;
using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Home dashboard assembled from the original MainPage controls.
/// The controls are re-parented instead of recreated so the original, tested
/// MainPage handlers remain authoritative for validation, ELF generation and
/// BinLoader delivery.
/// </summary>
public static class UiHomeScreen
{
    static readonly Color Background = Color.FromArgb("#030B14");
    static readonly Color Surface = Color.FromArgb("#071827");
    static readonly Color Surface2 = Color.FromArgb("#0A2238");
    static readonly Color Blue = Color.FromArgb("#008CFF");
    static readonly Color Cyan = Color.FromArgb("#00D9FF");
    static readonly Color Green = Color.FromArgb("#00E6A7");
    static readonly Color Purple = Color.FromArgb("#8D4DFF");
    static readonly Color Text = Color.FromArgb("#F4F8FF");
    static readonly Color Muted = Color.FromArgb("#8FAFCD");
    static readonly Color Stroke = Color.FromArgb("#135982");
    static readonly Color StrokeBright = Color.FromArgb("#078EE3");
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static T Field<T>(MainPage page, string name) where T : class
        => typeof(MainPage).GetField(name, PrivateInstance)?.GetValue(page) as T;

    public static View Build(MainPage page)
    {
        var url = Field<Entry>(page, "txtUrl");
        var ip = Field<Entry>(page, "txtIp");
        var scan = Field<Button>(page, "btnScan");
        var clear = Field<Button>(page, "btnLimpar");
        var generate = Field<Button>(page, "btnGerar");
        var inject = Field<Button>(page, "btnInjetar");
        var local = Field<Button>(page, "btnArquivoLocal");
        var icon = Field<Image>(page, "imgIcon");
        var title = Field<Label>(page, "lblTitulo");
        var info = Field<Label>(page, "lblInfo");
        var bottom = Field<Label>(page, "lblBottomStatus");

        Detach(url); Detach(ip); Detach(scan); Detach(clear); Detach(generate);
        Detach(inject); Detach(local); Detach(icon); Detach(title); Detach(info); Detach(bottom);

        StyleEntry(url, "Cole ou compartilhe o link do PKG");
        StyleEntry(ip, "192.168.0.10");
        StyleAction(scan, Green, Color.FromArgb("#031A16"));
        StyleOutline(clear);
        StyleAction(generate, Blue, Text);
        StyleAction(inject, Purple, Text);
        StyleOutline(local);

        if (title != null)
        {
            title.TextColor = Text;
            title.FontSize = 19;
            title.FontAttributes = FontAttributes.Bold;
            title.LineBreakMode = LineBreakMode.TailTruncation;
        }
        if (info != null)
        {
            info.TextColor = Muted;
            info.FontSize = 12;
            info.LineBreakMode = LineBreakMode.WordWrap;
        }
        if (bottom != null)
        {
            bottom.TextColor = Cyan;
            bottom.FontSize = 11;
            bottom.LineBreakMode = LineBreakMode.WordWrap;
        }
        if (icon != null)
        {
            icon.HeightRequest = 92;
            icon.WidthRequest = 92;
            icon.Aspect = Aspect.AspectFit;
        }

        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(14, 14, 14, 24),
            BackgroundColor = Background
        };

        var ps4Status = new Label
        {
            Text = string.IsNullOrWhiteSpace(ip?.Text) ? "● PS4 não configurado" : "● PS4 configurado",
            TextColor = string.IsNullOrWhiteSpace(ip?.Text) ? Muted : Green,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold
        };
        var ps4Value = new Label
        {
            Text = string.IsNullOrWhiteSpace(ip?.Text) ? "Informe o IP abaixo" : ip.Text,
            TextColor = Text,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold
        };
        if (ip != null)
        {
            ip.TextChanged += (_, _) =>
            {
                bool configured = !string.IsNullOrWhiteSpace(ip.Text);
                ps4Status.Text = configured ? "● PS4 configurado" : "● PS4 não configurado";
                ps4Status.TextColor = configured ? Green : Muted;
                ps4Value.Text = configured ? ip.Text : "Informe o IP abaixo";
            };
        }

        var connection = Card(true);
        var connectionGrid = new Grid { ColumnSpacing = 10, RowSpacing = 6 };
        connectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        connectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        connectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        connectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        connectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        connectionGrid.Children.Add(new Label { Text = "CONEXÃO PS4", TextColor = Cyan, FontSize = 10, FontAttributes = FontAttributes.Bold, CharacterSpacing = 1.2 });
        Grid.SetRow(ps4Value, 1); connectionGrid.Children.Add(ps4Value);
        Grid.SetRow(ps4Status, 2); connectionGrid.Children.Add(ps4Status);
        var port = StatusPill("RX 9091", Green);
        Grid.SetColumn(port, 1); Grid.SetRowSpan(port, 3); connectionGrid.Children.Add(port);
        connection.Content = connectionGrid;
        body.Children.Add(connection);

        var link = Card();
        var linkBody = new VerticalStackLayout { Spacing = 9 };
        linkBody.Children.Add(SectionTitle("Link do PKG", Cyan));

        var linkRow = new Grid { ColumnSpacing = 8 };
        linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (url != null)
        {
            Grid.SetColumn(url, 0);
            linkRow.Children.Add(url);
        }
        var paste = new Button
        {
            Text = "Colar",
            WidthRequest = 86,
            HeightRequest = 50,
            Padding = new Thickness(12, 0),
            FontSize = 13
        };
        StyleOutline(paste);
        paste.Clicked += (_, _) =>
        {
            try
            {
                if (url == null || url.Handler?.PlatformView == null)
                {
                    if (bottom != null) bottom.Text = "Campo de link indisponível.";
                    return;
                }

                url.Focus();
                url.CursorPosition = 0;
                url.SelectionLength = (url.Text ?? "").Length;
                object platform = url.Handler.PlatformView;
                MethodInfo method = platform.GetType().GetMethod("OnTextContextMenuItem", new[] { typeof(int) });
                object result = method?.Invoke(platform, new object[] { 16908322 }); // android.R.id.paste
                bool pasted = result is bool ok && ok;
                if (bottom != null)
                    bottom.Text = pasted ? "Link colado. Toque em Validar PKG." : "Não foi possível colar automaticamente.";
            }
            catch
            {
                if (bottom != null) bottom.Text = "Não foi possível ler a área de transferência.";
            }
        };
        Grid.SetColumn(paste, 1);
        linkRow.Children.Add(paste);
        linkBody.Children.Add(linkRow);

        var actions = new Grid { ColumnSpacing = 8 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (scan != null) { Grid.SetColumn(scan, 0); actions.Children.Add(scan); }
        if (clear != null) { Grid.SetColumn(clear, 1); actions.Children.Add(clear); }
        linkBody.Children.Add(actions);

        var provider = new Label
        {
            Text = "Provedor: aguardando link",
            TextColor = Muted,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold
        };
        if (url != null)
        {
            void RefreshProvider()
            {
                string name = ProviderName(url.Text);
                provider.Text = "Provedor: " + name;
                provider.TextColor = name == "Aguardando link" ? Muted : Cyan;
            }
            url.TextChanged += (_, _) => RefreshProvider();
            RefreshProvider();
        }
        linkBody.Children.Add(provider);
        link.Content = linkBody;
        body.Children.Add(link);

        body.Children.Add(ProviderCard());

        var package = Card();
        var packageBody = new VerticalStackLayout { Spacing = 8 };
        packageBody.Children.Add(SectionTitle("Pacote validado", Cyan));
        var packageGrid = new Grid { ColumnSpacing = 12 };
        packageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        packageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (icon != null) packageGrid.Children.Add(icon);
        var packageText = new VerticalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center };
        if (title != null) packageText.Children.Add(title);
        if (info != null) packageText.Children.Add(info);
        Grid.SetColumn(packageText, 1);
        packageGrid.Children.Add(packageText);
        packageBody.Children.Add(packageGrid);

        var galleryTitle = new Label
        {
            Text = "Galeria do PKG",
            TextColor = Cyan,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            IsVisible = false
        };
        var galleryHost = new HorizontalStackLayout { Spacing = 8 };
        var galleryScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = galleryHost,
            IsVisible = false
        };
        packageBody.Children.Add(galleryTitle);
        packageBody.Children.Add(galleryScroll);

        void RefreshArtwork()
        {
            galleryHost.Children.Clear();
            var meta = Field<PkgMetadata>(page, "_currentMeta");
            byte[][] artworks = meta == null ? Array.Empty<byte[]>() : UiArtworkStore.Get(meta.ContentId);
            if (artworks.Length <= 1)
            {
                galleryTitle.IsVisible = false;
                galleryScroll.IsVisible = false;
                return;
            }

            galleryTitle.Text = "Galeria do PKG • " + artworks.Length + " imagens";
            for (int i = 0; i < artworks.Length; i++)
            {
                byte[] bytes = artworks[i];
                var thumb = new Image
                {
                    WidthRequest = i == 0 ? 92 : 132,
                    HeightRequest = 78,
                    Aspect = Aspect.AspectFill,
                    Source = ImageSource.FromStream(() => new MemoryStream(bytes, false))
                };
                var thumbFrame = new Frame
                {
                    Padding = 2,
                    CornerRadius = 12,
                    BorderColor = i == 0 ? Cyan : Stroke,
                    BackgroundColor = Surface2,
                    HasShadow = false,
                    Content = thumb
                };
                galleryHost.Children.Add(thumbFrame);
            }
            galleryTitle.IsVisible = true;
            galleryScroll.IsVisible = true;
        }

        if (generate != null)
            generate.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "IsEnabled" && generate.IsEnabled) RefreshArtwork();
            };
        if (title != null)
            title.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Text") RefreshArtwork();
            };
        if (url != null)
            url.TextChanged += (_, _) =>
            {
                galleryHost.Children.Clear();
                galleryTitle.IsVisible = false;
                galleryScroll.IsVisible = false;
            };

        package.Content = packageBody;
        body.Children.Add(package);

        var stageText = new Label { TextColor = Text, FontSize = 14, FontAttributes = FontAttributes.Bold };
        var stageDetail = new Label { TextColor = Muted, FontSize = 11 };
        void RefreshStage()
        {
            string status = bottom?.Text ?? "";
            if (status.IndexOf("enviado", StringComparison.OrdinalIgnoreCase) >= 0 ||
                status.IndexOf("sent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                stageText.Text = "✓ Receiver conectado";
                stageText.TextColor = Purple;
                stageDetail.Text = "Canal local pronto; BGFT não é usado nesta linha 4.0.";
            }
            else if (inject != null && inject.IsEnabled)
            {
                stageText.Text = "○ Receiver aguardando";
                stageText.TextColor = Blue;
                stageDetail.Text = "O app iniciará o Receiver automaticamente pelo BinLoader.";
            }
            else if (generate != null && generate.IsEnabled)
            {
                stageText.Text = "✓ PKG validado";
                stageText.TextColor = Green;
                stageDetail.Text = "Metadados confirmados; iniciando Receiver local.";
            }
            else if (!string.IsNullOrWhiteSpace(url?.Text))
            {
                stageText.Text = "○ Link recebido";
                stageText.TextColor = Cyan;
                stageDetail.Text = "Valide o PKG antes de iniciar a transferência local.";
            }
            else
            {
                stageText.Text = "○ Aguardando link";
                stageText.TextColor = Muted;
                stageDetail.Text = "Cole um link compatível para iniciar.";
            }
        }
        if (generate != null) generate.PropertyChanged += (_, e) => { if (e.PropertyName == "IsEnabled") RefreshStage(); };
        if (inject != null) inject.PropertyChanged += (_, e) => { if (e.PropertyName == "IsEnabled") RefreshStage(); };
        if (bottom != null) bottom.PropertyChanged += (_, e) => { if (e.PropertyName == "Text") RefreshStage(); };
        if (url != null) url.TextChanged += (_, _) => RefreshStage();
        RefreshStage();

        var progress = Card();
        progress.Content = new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                SectionTitle("Status da atividade", Cyan),
                StepsRow(),
                stageText,
                stageDetail
            }
        };
        body.Children.Add(progress);

        var payloadActions = Card();
        var payloadBody = new VerticalStackLayout { Spacing = 9 };
        payloadBody.Children.Add(SectionTitle("BGFT legado desativado", Cyan));
        if (generate != null) payloadBody.Children.Add(generate);
        if (inject != null) payloadBody.Children.Add(inject);
        if (local != null) payloadBody.Children.Add(local);
        payloadActions.Content = payloadBody;
        body.Children.Add(payloadActions);

        if (ip != null)
        {
            var ipCard = Card();
            ipCard.Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    SectionTitle("Destino", Cyan),
                    new Label { Text = "IP do PS4 · BinLoader 9090 / Receiver 9091", TextColor = Muted, FontSize = 10 },
                    ip
                }
            };
            body.Children.Add(ipCard);
        }

        if (bottom != null)
        {
            var log = Card();
            log.Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    SectionTitle("Registro", Cyan),
                    bottom
                }
            };
            body.Children.Add(log);
        }

        body.Children.Add(new Label
        {
            Text = "Dev. By TaySon  •  Tayson 4.0.0-alpha2  •  Transferência local",
            TextColor = Muted,
            FontSize = 10,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 2)
        });

        return new ScrollView { BackgroundColor = Background, Content = body };
    }

    static void Detach(View view)
    {
        if (view == null || view.Parent == null) return;
        if (view.Parent is Layout layout)
        {
            layout.Children.Remove(view);
            return;
        }
        if (view.Parent is ContentView content && ReferenceEquals(content.Content, view))
        {
            content.Content = null;
            return;
        }
        if (view.Parent is ScrollView scroll && ReferenceEquals(scroll.Content, view))
            scroll.Content = null;
    }

    static Frame Card(bool bright = false) => new Frame
    {
        Padding = new Thickness(14),
        CornerRadius = 18,
        BorderColor = bright ? StrokeBright : Stroke,
        BackgroundColor = Surface,
        HasShadow = false,
        HorizontalOptions = LayoutOptions.Fill
    };

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
            Padding = new Thickness(9, 6),
            CornerRadius = 12,
            BorderColor = color,
            BackgroundColor = Surface2,
            HasShadow = false,
            VerticalOptions = LayoutOptions.Center
        };
        frame.Content = new Label { Text = text, TextColor = color, FontSize = 10, FontAttributes = FontAttributes.Bold };
        return frame;
    }

    static View ProviderCard()
    {
        var frame = Card();
        var body = new VerticalStackLayout { Spacing = 8 };
        body.Children.Add(SectionTitle("Provedores suportados", Cyan));
        var flex = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap, JustifyContent = FlexJustify.Start };
        foreach (string name in new[] { "Mocha", "VikingFile", "FileKeeper", "Rootz", "AkiraBox" })
            flex.Children.Add(Chip(name));
        body.Children.Add(flex);
        frame.Content = body;
        return frame;
    }

    static View Chip(string text)
    {
        var frame = new Frame
        {
            Padding = new Thickness(9, 5),
            Margin = new Thickness(0, 0, 7, 7),
            CornerRadius = 11,
            BorderColor = Stroke,
            BackgroundColor = Surface2,
            HasShadow = false
        };
        frame.Content = new Label { Text = text, TextColor = Cyan, FontSize = 10, FontAttributes = FontAttributes.Bold };
        return frame;
    }

    static View StepsRow()
    {
        var grid = new Grid { ColumnSpacing = 5 };
        string[] steps = { "Link", "Validar", "Receiver", "Transferir", "Instalar" };
        for (int i = 0; i < steps.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new Label
            {
                Text = steps[i],
                TextColor = i == 0 ? Cyan : Muted,
                FontSize = 9,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center
            };
            var box = new Frame
            {
                Padding = new Thickness(4, 6),
                CornerRadius = 9,
                BorderColor = i == 0 ? Blue : Stroke,
                BackgroundColor = Surface2,
                HasShadow = false,
                Content = label
            };
            Grid.SetColumn(box, i);
            grid.Children.Add(box);
        }
        return grid;
    }

    static string ProviderName(string value)
    {
        string url = (value ?? "").ToLowerInvariant();
        if (url.Length == 0) return "Aguardando link";
        if (url.Contains("akirabox")) return "AkiraBox";
        if (url.Contains("vikingfile")) return "VikingFile";
        if (url.Contains("rootz")) return "Rootz";
        if (url.Contains("mocha")) return "Mocha";
        if (url.Contains("filekeeper") || url.Contains("dlproxy")) return "FileKeeper";
        return "Link direto";
    }

    static void StyleEntry(Entry entry, string placeholder)
    {
        if (entry == null) return;
        entry.Placeholder = placeholder;
        entry.BackgroundColor = Surface2;
        entry.TextColor = Text;
        entry.PlaceholderColor = Muted;
        entry.FontSize = Math.Max(entry.FontSize, 14);
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
}
