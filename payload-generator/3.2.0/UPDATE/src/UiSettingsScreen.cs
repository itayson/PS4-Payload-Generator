using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

public static class UiSettingsScreen
{
    static readonly Color Background = Color.FromArgb("#030B14");
    static readonly Color Surface = Color.FromArgb("#071827");
    static readonly Color Surface2 = Color.FromArgb("#0A2238");
    static readonly Color Blue = Color.FromArgb("#008CFF");
    static readonly Color Cyan = Color.FromArgb("#00D9FF");
    static readonly Color Green = Color.FromArgb("#00E6A7");
    static readonly Color Red = Color.FromArgb("#FF4D6D");
    static readonly Color Text = Color.FromArgb("#F4F8FF");
    static readonly Color Muted = Color.FromArgb("#8FAFCD");
    static readonly Color Stroke = Color.FromArgb("#135982");
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static View Build(MainPage page)
    {
        var realIp = typeof(MainPage).GetField("txtIp", PrivateInstance)?.GetValue(page) as Entry;
        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(14, 14, 14, 24),
            BackgroundColor = Background
        };
        body.Children.Add(Hero("Ajustes", "PS4 e transporte automático"));

        var ip = Input(UiStateStore.LoadPs4Ip(realIp?.Text ?? ""), "192.168.0.7");
        var status = new Label
        {
            Text = "Descoberta UDP 987. Não acessa o BinLoader TCP 9090.",
            TextColor = Muted, FontSize = 11, LineBreakMode = LineBreakMode.WordWrap
        };
        void SaveIp(string value)
        {
            if (!Ps4LanAddressPolicy.TryNormalize(value, out string normalized))
            {
                status.Text = Ps4LanAddressPolicy.RejectionMessage;
                status.TextColor = Red;
                return;
            }
            ip.Text = normalized;
            UiStateStore.SavePs4Ip(normalized);
            if (realIp != null) realIp.Text = normalized;
            status.Text = "✓ Console configurado: " + normalized;
            status.TextColor = Green;
        }

        var discover = Action("Localizar PS4 automaticamente", Blue);
        discover.Clicked += async (_, _) =>
        {
            discover.IsEnabled = false;
            status.Text = "Procurando PS4 na rede Wi-Fi…";
            status.TextColor = Muted;
            try
            {
                var candidates = await Ps4Discovery.DiscoverAsync();
                if (candidates.Count == 0)
                {
                    status.Text = "PS4 não encontrado por UDP. Digite o IP manualmente; a porta 9090 não foi utilizada.";
                    status.TextColor = Muted;
                }
                else
                {
                    string selected = candidates.Count == 1 ? candidates[0] :
                        await page.DisplayActionSheet("Selecione o PS4", "Cancelar", null,
                            new System.Collections.Generic.List<string>(candidates).ToArray());
                    if (!string.IsNullOrWhiteSpace(selected) && selected != "Cancelar")
                        SaveIp(selected);
                }
            }
            catch (Exception)
            {
                status.Text = "Descoberta indisponível nesta rede; utilize o IP manual.";
                status.TextColor = Red;
            }
            finally { discover.IsEnabled = true; }
        };
        var save = Outline("Usar IP informado");
        save.Clicked += (_, _) => SaveIp(ip.Text);

        var ps4 = Card();
        ps4.Content = new VerticalStackLayout
        {
            Spacing = 9,
            Children = {
                Title("Meu PS4", Cyan),
                discover,
                new Label { Text = "Ou informe o IP do console:", TextColor = Muted, FontSize = 11 },
                ip,
                save,
                status
            }
        };
        body.Children.Add(ps4);

        var worker = Card();
        worker.Content = new VerticalStackLayout
        {
            Spacing = 7,
            Children = {
                Title("Worker conectado", Cyan),
                new Label {
                    Text = BgftRoutePlanner.DefaultWorkerBase,
                    TextColor = Green, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap
                },
                new Label {
                    Text = "Pré-configurado, sem chave de homologação. O código publicado no mesmo endereço entra em vigor automaticamente; atualizações do APK continuam sendo instaladas separadamente.",
                    TextColor = Muted, FontSize = 11, LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };
        body.Children.Add(worker);
        body.Children.Add(StatusCard("Transporte", "Automático", Green,
            "Validação de PKG e envio BGFT preservados. Confirme o download no PS4."));
        return new ScrollView { BackgroundColor = Background, Content = body };
    }

    static View Hero(string title, string subtitle)
    {
        var card = Card();
        card.BorderColor = Blue;
        card.Content = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = title, TextColor = Text, FontSize = 25, FontAttributes = FontAttributes.Bold },
                new Label { Text = subtitle, TextColor = Cyan, FontSize = 12 }
            }
        };
        return card;
    }

    static Frame Card() => new Frame
    {
        Padding = new Thickness(14),
        CornerRadius = 18,
        BorderColor = Stroke,
        BackgroundColor = Surface,
        HasShadow = false
    };

    static Label Title(string text, Color color) => new Label
    {
        Text = text,
        TextColor = color,
        FontSize = 15,
        FontAttributes = FontAttributes.Bold
    };

    static Entry Input(string value, string placeholder) => new Entry
    {
        Text = value,
        Placeholder = placeholder,
        BackgroundColor = Surface2,
        TextColor = Text,
        PlaceholderColor = Muted,
        FontSize = 14
    };

    static Button Action(string text, Color color) => new Button
    {
        Text = text,
        BackgroundColor = color,
        TextColor = Text,
        BorderColor = color,
        BorderWidth = 1,
        CornerRadius = 14,
        HeightRequest = 52,
        FontAttributes = FontAttributes.Bold
    };

    static Button Outline(string text) => new Button
    {
        Text = text,
        BackgroundColor = Surface2,
        TextColor = Cyan,
        BorderColor = Stroke,
        BorderWidth = 1,
        CornerRadius = 14,
        HeightRequest = 48,
        FontAttributes = FontAttributes.Bold
    };

    static View StatusCard(string title, string state, Color color, string detail)
    {
        var card = Card();
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 5 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(Title(title, Text));
        var badge = new Label { Text = state, TextColor = color, FontSize = 10, FontAttributes = FontAttributes.Bold };
        Grid.SetColumn(badge, 1); grid.Children.Add(badge);
        var note = new Label { Text = detail, TextColor = Muted, FontSize = 10, LineBreakMode = LineBreakMode.WordWrap };
        Grid.SetRow(note, 1); Grid.SetColumnSpan(note, 2); grid.Children.Add(note);
        card.Content = grid;
        return card;
    }
}
