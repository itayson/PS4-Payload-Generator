using System;
using System.Net;
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

        body.Children.Add(Hero("Ajustes", "Conexão e transporte BGFT"));

        var ip = Input(UiStateStore.LoadPs4Ip(realIp?.Text ?? ""), "192.168.0.10");
        var ps4Status = new Label
        {
            Text = "A porta 9090 será aberta somente no envio, para não consumir o listener do GoldHEN.",
            TextColor = Muted,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap
        };
        var preparePs4 = Action("Usar este PS4", Blue);
        preparePs4.Clicked += (_, _) =>
        {
            string host = (ip.Text ?? "").Trim();
            if (!IPAddress.TryParse(host, out _))
            {
                ps4Status.Text = "Informe um endereço IP válido do PS4.";
                ps4Status.TextColor = Red;
                return;
            }

            UiStateStore.SavePs4Ip(host);
            if (realIp != null) realIp.Text = host;
            ps4Status.Text = "✓ PS4 configurado. O BinLoader 9090 será usado apenas no envio do payload.";
            ps4Status.TextColor = Green;
        };

        var ps4 = Card();
        ps4.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Title("Conexão com PS4", Cyan),
                new Label { Text = "IP do console", TextColor = Muted, FontSize = 10 },
                ip,
                new Label { Text = "GoldHEN PayLoader/BinLoader • TCP 9090", TextColor = Cyan, FontSize = 11 },
                preparePs4,
                ps4Status
            }
        };
        body.Children.Add(ps4);

        var worker = Input(UiStateStore.LoadWorkerUrl(), BgftRoutePlanner.DefaultWorkerBase);
        worker.IsPassword = true;
        var workerHint = new Label
        {
            Text = "Cole a base HTTPS completa, incluindo /app/… quando fornecido. A Central de Transporte usa /api/link para preparar o PKG antes da análise local.",
            TextColor = Muted,
            FontSize = 10,
            LineBreakMode = LineBreakMode.WordWrap
        };
        var restore = Outline("Restaurar Worker padrão");
        restore.Clicked += (_, _) => worker.Text = BgftRoutePlanner.DefaultWorkerBase;

        var workerCard = Card();
        workerCard.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Title("Cloudflare Worker", Cyan),
                worker,
                workerHint,
                restore
            }
        };
        body.Children.Add(workerCard);

        var workerSaved = new Label { TextColor = Muted, FontSize = 11 };
        var saveWorker = Action("Salvar somente o Worker", Blue);
        saveWorker.Clicked += (_, _) =>
        {
            try
            {
                UiStateStore.SaveWorkerUrl(StudioTransport.WorkerBase(worker.Text));
                workerSaved.Text = "Worker salvo. O IP do PS4 pode ser configurado depois.";
                workerSaved.TextColor = Green;
            }
            catch (StudioTransportException error)
            {
                workerSaved.Text = error.Message;
                workerSaved.TextColor = Red;
            }
        };
        body.Children.Add(saveWorker);
        body.Children.Add(workerSaved);

        var reveal = Outline("Mostrar / ocultar endereço do Worker");
        reveal.Clicked += (_, _) => worker.IsPassword = !worker.IsPassword;
        body.Children.Add(reveal);
        var autoSend = Outline(UiStateStore.LoadAutoSend() ? "Envio automático: ATIVADO" : "Envio automático: DESATIVADO");
        autoSend.Clicked += (_, _) =>
        {
            bool enabled = !UiStateStore.LoadAutoSend();
            UiStateStore.SaveAutoSend(enabled);
            autoSend.Text = enabled ? "Envio automático: ATIVADO" : "Envio automático: DESATIVADO";
        };
        body.Children.Add(autoSend);
        body.Children.Add(new Label { Text = "Com o envio automático desativado, colar um link não inicia envio ao PS4. O envio manual continua disponível.", TextColor = Muted, FontSize = 11 });

        body.Children.Add(StatusCard("Validação de PKG", "Sempre ativa", Green,
            "Magic, Content ID, tamanho e comportamento HTTP são validados antes da geração do ELF."));
        body.Children.Add(StatusCard("Navegador de provedor", "Automático", Cyan,
            "O fluxo de navegador é acionado somente quando o provedor exige contexto de sessão, como AkiraBox."));
        body.Children.Add(StatusCard("Histórico", "Sem links de download", Blue,
            "URLs assinadas e cookies não são gravados no histórico. A base privada do Worker permanece nas configurações locais do aplicativo."));

        var saveStatus = new Label { TextColor = Muted, FontSize = 11, HorizontalTextAlignment = TextAlignment.Center };
        var save = Action("Salvar configurações", Blue);
        save.Clicked += (_, _) =>
        {
            string workerValue;
            try { workerValue = StudioTransport.WorkerBase(worker.Text); }
            catch (StudioTransportException error)
            {
                saveStatus.Text = error.Message;
                saveStatus.TextColor = Red;
                return;
            }

            string ipValue = (ip.Text ?? "").Trim();
            if (!IPAddress.TryParse(ipValue, out _))
            {
                saveStatus.Text = "IP do PS4 inválido.";
                saveStatus.TextColor = Red;
                return;
            }

            UiStateStore.SavePs4Ip(ipValue);
            UiStateStore.SaveWorkerUrl(workerValue);
            if (realIp != null) realIp.Text = ipValue;
            saveStatus.Text = "✓ Configurações salvas";
            saveStatus.TextColor = Green;
        };
        body.Children.Add(save);
        body.Children.Add(saveStatus);

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
