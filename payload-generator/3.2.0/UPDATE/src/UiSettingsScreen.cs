using System;
using System.Net;
using System.Reflection;
using Android.Runtime;
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

        body.Children.Add(Hero("Ajustes", "Conexão, automação e diagnóstico"));

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
            UiDiagnostics.Add("CONFIG", "IP do PS4 atualizado.");
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

        var queueStatus = new Label
        {
            Text = UiSendQueue.PendingCount == 0 ? "Nenhum item pendente." : UiSendQueue.PendingCount + " item(ns) aguardando envio.",
            TextColor = UiSendQueue.PendingCount == 0 ? Muted : Cyan,
            FontSize = 11,
            LineBreakMode = LineBreakMode.WordWrap
        };
        var clearQueue = Outline("Limpar fila pendente");
        clearQueue.Clicked += (_, _) =>
        {
            UiSendQueue.Clear();
            UiDiagnostics.Add("FILA", "Fila limpa manualmente.");
            queueStatus.Text = "Fila limpa. Nenhum item pendente.";
            queueStatus.TextColor = Green;
        };
        var queueCard = Card();
        queueCard.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Title("Fila automática", Cyan),
                new Label { Text = "Cole vários links, um por linha. O app valida e envia em sequência, um payload por vez.", TextColor = Muted, FontSize = 10, LineBreakMode = LineBreakMode.WordWrap },
                queueStatus,
                clearQueue
            }
        };
        body.Children.Add(queueCard);

        body.Children.Add(StatusCard("Validação de PKG", "Sempre ativa", Green,
            "Magic, Content ID, tamanho e comportamento HTTP são validados antes da geração do ELF."));
        body.Children.Add(StatusCard("Navegador de provedor", "Automático", Cyan,
            "O fluxo de navegador é acionado somente quando o provedor exige contexto de sessão, como AkiraBox."));
        body.Children.Add(StatusCard("Dados sensíveis", "Não persistidos", Blue,
            "URLs assinadas, cookies e tokens não são gravados no histórico nem no diagnóstico."));

        var diagStatus = new Label { Text = "O relatório remove query strings e credenciais antes de copiar.", TextColor = Muted, FontSize = 10, LineBreakMode = LineBreakMode.WordWrap };
        var copyDiag = Action("Copiar diagnóstico", Blue);
        copyDiag.Clicked += (_, _) =>
        {
            try
            {
                CopyToAndroidClipboard(UiDiagnostics.BuildReport());
                diagStatus.Text = "✓ Diagnóstico copiado para a área de transferência.";
                diagStatus.TextColor = Green;
            }
            catch (Exception ex)
            {
                diagStatus.Text = "Falha ao copiar diagnóstico: " + ex.GetType().Name;
                diagStatus.TextColor = Red;
            }
        };
        var clearDiag = Outline("Limpar diagnóstico da sessão");
        clearDiag.Clicked += (_, _) =>
        {
            UiDiagnostics.Clear();
            diagStatus.Text = "Diagnóstico da sessão limpo.";
            diagStatus.TextColor = Green;
        };
        var diagCard = Card();
        diagCard.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Title("Diagnóstico", Cyan),
                diagStatus,
                copyDiag,
                clearDiag
            }
        };
        body.Children.Add(diagCard);

        var saveStatus = new Label { TextColor = Muted, FontSize = 11, HorizontalTextAlignment = TextAlignment.Center };
        var save = Action("Salvar configurações", Blue);
        save.Clicked += (_, _) =>
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

            UiStateStore.SavePs4Ip(ipValue);
            UiStateStore.SaveWorkerUrl(workerValue);
            if (realIp != null) realIp.Text = ipValue;
            UiDiagnostics.Add("CONFIG", "Configurações salvas.");
            saveStatus.Text = "✓ Configurações salvas";
            saveStatus.TextColor = Green;
        };
        body.Children.Add(save);
        body.Children.Add(saveStatus);

        return new ScrollView { BackgroundColor = Background, Content = body };
    }

    static void CopyToAndroidClipboard(string text)
    {
        IntPtr serviceClass = IntPtr.Zero;
        IntPtr javaString = IntPtr.Zero;
        try
        {
            var context = Android.App.Application.Context;
            Java.Lang.Object service = context.GetSystemService("clipboard");
            if (service == null) throw new InvalidOperationException("Serviço de clipboard indisponível");

            serviceClass = JNIEnv.GetObjectClass(service.Handle);
            IntPtr setText = JNIEnv.GetMethodID(serviceClass, "setText", "(Ljava/lang/CharSequence;)V");
            if (setText == IntPtr.Zero) throw new InvalidOperationException("Clipboard setText indisponível");

            javaString = JNIEnv.NewString(text ?? "");
            JNIEnv.CallVoidMethod(service.Handle, setText, new JValue(javaString));
        }
        finally
        {
            if (javaString != IntPtr.Zero) JNIEnv.DeleteLocalRef(javaString);
            if (serviceClass != IntPtr.Zero) JNIEnv.DeleteLocalRef(serviceClass);
        }
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
