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
        var workerHint = new Label
        {
            Text = "HTTPS sem query string. É usado para VikingFile/Rootz R2 e AkiraBox quando o BGFT precisa da camada de transporte.",
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

        // Opt-in staging configuration. Never echo the credential or persist it in Preferences.
        var stagingKey = Input("", "Chave de homologação (somente staging)");
        stagingKey.IsPassword = true;
        stagingKey.MaxLength = 512;
        var keyStatus = new Label { Text = "Chave apenas nesta sessão: informe novamente ao reiniciar o aplicativo.",
                                    TextColor = Muted, FontSize = 10, LineBreakMode = LineBreakMode.WordWrap };
        var clearKey = Outline("Apagar chave de homologação");
        clearKey.Clicked += (_, _) =>
        {
            StagingRouteAuth.Clear();
            stagingKey.Text = "";
            keyStatus.Text = "Chave desta sessão apagada.";
        };
        var authCheckStatus = new Label
        {
            Text = "Teste isolado: verifica a chave sem criar rota, baixar PKG ou contatar o PS4.",
            TextColor = Muted, FontSize = 10, LineBreakMode = LineBreakMode.WordWrap
        };
        var checkAuth = Outline("Testar autenticação (sem PS4)");
        checkAuth.Clicked += async (_, _) =>
        {
            checkAuth.IsEnabled = false;
            try
            {
                // Only send credentials to the exact, saved HTTPS staging host.
                string target = UiStateStore.LoadWorkerUrl();
                if (!StagingRouteAuth.IsStagingWorker(target))
                {
                    authCheckStatus.Text = "Salve primeiro o endereço do Worker de homologação em Ajustes.";
                    authCheckStatus.TextColor = Red;
                    return;
                }
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var request = new HttpRequestMessage(HttpMethod.Post, target.TrimEnd('/') + "/api/v12/auth-check");
                await StagingRouteAuth.AttachAsync(request);
                using HttpResponseMessage result = await http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, System.Threading.CancellationToken.None);
                if (result.StatusCode == HttpStatusCode.NoContent)
                {
                    authCheckStatus.Text = "✓ Autenticação aprovada. Nenhum PKG ou payload enviado.";
                    authCheckStatus.TextColor = Green;
                }
                else
                {
                    authCheckStatus.Text = result.StatusCode == HttpStatusCode.Unauthorized ||
                        result.StatusCode == HttpStatusCode.Forbidden ?
                        "Chave recusada pelo Worker de homologação (HTTP " + (int)result.StatusCode + ")." :
                        "Falha no teste de autenticação (HTTP " + (int)result.StatusCode + ").";
                    authCheckStatus.TextColor = Red;
                }
            }
            catch (Exception)
            {
                authCheckStatus.Text = "Teste indisponível. Verifique conexão, chave da sessão e Worker.";
                authCheckStatus.TextColor = Red;
            }
            finally { checkAuth.IsEnabled = true; }
        };

        var stagingCard = Card();
        stagingCard.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children = { Title("Autenticação de homologação", Cyan), stagingKey, clearKey,
                         keyStatus, checkAuth, authCheckStatus }
        };
        body.Children.Add(stagingCard);

        body.Children.Add(StatusCard("Validação de PKG", "Sempre ativa", Green,
            "Magic, Content ID, tamanho e comportamento HTTP são validados antes da geração do ELF."));
        body.Children.Add(StatusCard("Navegador de provedor", "Automático", Cyan,
            "O fluxo de navegador é acionado somente quando o provedor exige contexto de sessão, como AkiraBox."));
        body.Children.Add(StatusCard("Dados sensíveis", "Não persistidos", Blue,
            "URLs e cookies não são gravados no histórico. A chave de testes fica só na memória."));

        var saveStatus = new Label { TextColor = Muted, FontSize = 11, HorizontalTextAlignment = TextAlignment.Center };
        var save = Action("Salvar configurações", Blue);
        save.Clicked += async (_, _) =>
        {
            string workerValue = (worker.Text ?? "").Trim().TrimEnd('/');
            if (!Uri.TryCreate(workerValue, UriKind.Absolute, out Uri parsed) ||
                parsed.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(parsed.Query))
            {
                saveStatus.Text = "Worker inválido: use um endereço HTTPS sem query string.";
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

            if (parsed.IdnHost.Equals(StagingRouteAuth.Host, StringComparison.OrdinalIgnoreCase) &&
                !StagingRouteAuth.IsStagingWorker(workerValue))
            {
                saveStatus.Text = "Use somente o endereço HTTPS raiz do Worker de homologação.";
                saveStatus.TextColor = Red;
                return;
            }
            if (StagingRouteAuth.IsStagingWorker(workerValue))
            {
                try
                {
                    if (!string.IsNullOrEmpty(stagingKey.Text))
                        await StagingRouteAuth.SaveAsync(stagingKey.Text);
                    if (!await StagingRouteAuth.IsConfiguredAsync())
                    {
                        saveStatus.Text = "Configure uma chave válida para o Worker de homologação.";
                        saveStatus.TextColor = Red;
                        return;
                    }
                    stagingKey.Text = "";
                    keyStatus.Text = "Chave desta sessão configurada.";
                }
                catch
                {
                    saveStatus.Text = "Falha ao configurar chave desta sessão.";
                    saveStatus.TextColor = Red;
                    return;
                }
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
