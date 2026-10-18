using System;
using System.Net.Http;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

public static class UiWorkerScreen
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

    public static View Build()
    {
        string worker = UiStateStore.LoadWorkerUrl(BgftRoutePlanner.DefaultWorkerBase);
        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(14, 14, 14, 24),
            BackgroundColor = Background
        };

        body.Children.Add(Hero("Worker", "Roteamento BGFT e transporte universal"));

        var status = new Label { Text = "● Aguardando teste", TextColor = Muted, FontSize = 12, FontAttributes = FontAttributes.Bold };
        var response = new Label { Text = "Health ainda não consultado.", TextColor = Muted, FontSize = 10, LineBreakMode = LineBreakMode.WordWrap };

        var endpoint = Card();
        endpoint.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Title("Endpoint configurado", Cyan),
                new Label { Text = worker, TextColor = Text, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap },
                status,
                response
            }
        };
        body.Children.Add(endpoint);

        body.Children.Add(KpiRow(
            ("WORKER", StagingRouteAuth.IsStagingWorker(worker) ? "v12 STG" : "ROTA", Cyan),
            ("KV", "ROUTES", Green),
            ("BGFT", ".pkg", Blue)));

        var preflight = Card();
        var preflightLabel = new Label
        {
            Text = "Valida IP local e configuração de chave. Não abre TCP 9090, não envia ELF e não consulta o BGFT.",
            TextColor = Muted,
            FontSize = 11,
            LineBreakMode = LineBreakMode.WordWrap
        };
        var preflightButton = Action("Verificar preparo (sem PS4)", Blue);
        preflightButton.Clicked += async (_, _) =>
        {
            preflightButton.IsEnabled = false;
            try
            {
                string configuredIp = UiStateStore.LoadPs4Ip();
                if (!Ps4LanAddressPolicy.TryNormalize(configuredIp, out string destination))
                {
                    preflightLabel.Text = "IP do PS4 inválido. Confira Ajustes antes do envio.";
                    preflightLabel.TextColor = Red;
                    return;
                }
                if (StagingRouteAuth.IsStagingWorker(worker) &&
                    !await StagingRouteAuth.IsConfiguredAsync())
                {
                    preflightLabel.Text = "PS4 " + destination +
                        ": informe novamente a chave de homologação em Ajustes.";
                    preflightLabel.TextColor = Red;
                    return;
                }
                preflightLabel.Text = "Configuração local OK: " + destination +
                    ":9090. Isso NÃO confirma conectividade, execução ELF nem BGFT.";
                preflightLabel.TextColor = Green;
            }
            catch (Exception)
            {
                preflightLabel.Text = "Não foi possível verificar a configuração local.";
                preflightLabel.TextColor = Red;
            }
            finally { preflightButton.IsEnabled = true; }
        };
        preflight.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Title("Diagnóstico local (sem payload)", Cyan),
                preflightLabel,
                preflightButton
            }
        };
        body.Children.Add(preflight);

        body.Children.Add(InfoCard("Transporte R2",
            "VikingFile e Rootz usam rota curta no Worker quando o endereço final é AWS SigV4/Cloudflare R2."));
        body.Children.Add(InfoCard("AkiraBox",
            "A rota temporária pode manter cookies, Referer e User-Agent no KV sem expor esses dados dentro do ELF."));
        body.Children.Add(InfoCard("Normalização BGFT",
            "O transporte pode responder a HEAD por GET Range bytes=0-0 e preservar GET/Range; confirme o BGFT no PS4."));

        var test = Action("Testar Worker", Blue);
        test.Clicked += async (_, _) =>
        {
            test.IsEnabled = false;
            status.Text = "● Testando /health…";
            status.TextColor = Cyan;
            response.Text = worker + "/health";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                string health = await client.GetStringAsync(worker.TrimEnd('/') + "/health");
                string compact = (health ?? "").Trim();
                if (compact.Length > 160) compact = compact.Substring(0, 160) + "…";
                bool ok = compact.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0;
                status.Text = ok ? "● Online • health OK" : "● Resposta recebida";
                status.TextColor = ok ? Green : Cyan;
                response.Text = compact.Length == 0 ? "Resposta vazia." : compact;
                response.TextColor = ok ? Green : Muted;
            }
            catch (Exception ex)
            {
                status.Text = "● Worker indisponível";
                status.TextColor = Red;
                response.Text = ex.GetType().Name + ": " + ex.Message;
                response.TextColor = Red;
            }
            finally { test.IsEnabled = true; }
        };
        body.Children.Add(test);

        body.Children.Add(new Label
        {
            Text = "O teste /health confirma o endpoint, não confirma criação de rota, transferência completa ou instalação no PS4.",
            TextColor = Muted,
            FontSize = 9,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(4)
        });

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

    static View InfoCard(string title, string text)
    {
        var card = Card();
        card.Content = new VerticalStackLayout
        {
            Spacing = 5,
            Children =
            {
                Title(title, Cyan),
                new Label { Text = text, TextColor = Muted, FontSize = 11, LineBreakMode = LineBreakMode.WordWrap }
            }
        };
        return card;
    }

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
                    new Label { Text = items[i].Value, TextColor = items[i].Color, FontSize = 13, FontAttributes = FontAttributes.Bold }
                }
            };
            Grid.SetColumn(frame, i);
            grid.Children.Add(frame);
        }
        return grid;
    }
}
