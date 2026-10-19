using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Persistent history screen used by the neon shell.
/// </summary>
public static class UiHistoryScreen
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

    public static View Build(MainPage page, string ps4Ip)
    {
        var history = UiStateStore.ReadHistory();
        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(14, 14, 14, 22),
            BackgroundColor = Background
        };

        var hero = Card();
        hero.BorderColor = StrokeBright;
        hero.Content = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = "Histórico", TextColor = Text, FontSize = 25, FontAttributes = FontAttributes.Bold },
                new Label { Text = "Transporte local e confirmações manuais • BGFT sem telemetria automática", TextColor = Cyan, FontSize = 12 }
            }
        };
        body.Children.Add(hero);

        string latest = history.Count == 0 ? "—" : ShortStatus(history[0].Status);
        Label countKpi = null;
        Label latestKpi = null;
        body.Children.Add(KpiRow((index, label) =>
            {
                if (index == 0) countKpi = label;
                if (index == 1) latestKpi = label;
            },
            ("REGISTROS", history.Count.ToString(), Cyan),
            ("ÚLTIMO", latest, Green),
            ("PS4", string.IsNullOrWhiteSpace(ps4Ip) ? "—" : ps4Ip, Blue)));

        void RefreshKpis()
        {
            var current = UiStateStore.ReadHistory();
            if (countKpi != null) countKpi.Text = current.Count.ToString();
            if (latestKpi != null) latestKpi.Text =
                current.Count == 0 ? "—" : ShortStatus(current[0].Status);
        }

        if (history.Count == 0)
        {
            body.Children.Add(InfoCard(
                "Nenhum histórico ainda",
                "Valide um PKG na tela Início. As etapas de geração e envio do ELF podem aparecer aqui. Download e instalação dependem de confirmação no PS4."));
        }
        else
        {
            body.Children.Add(new Label
            {
                Text = "ATIVIDADE RECENTE",
                TextColor = Muted,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                CharacterSpacing = 1.2,
                Margin = new Thickness(3, 4, 0, 0)
            });

            int shown = history.Count < 10 ? history.Count : 10;
            for (int i = 0; i < shown; i++)
                body.Children.Add(HistoryCard(history[i], i, page, RefreshKpis));

            var clear = new Button
            {
                Text = "Limpar histórico",
                BackgroundColor = Surface2,
                TextColor = Muted,
                BorderColor = Stroke,
                BorderWidth = 1,
                CornerRadius = 14,
                HeightRequest = 48,
                FontAttributes = FontAttributes.Bold
            };
            clear.Clicked += async (_, _) =>
            {
                bool yes = await page.DisplayAlert(
                    "Histórico",
                    "Apagar os registros salvos neste aparelho?",
                    "Apagar",
                    "Cancelar");
                if (!yes) return;
                UiStateStore.ClearHistory();
                RefreshKpis();
                clear.Text = "Histórico apagado";
                clear.IsEnabled = false;
            };
            body.Children.Add(clear);
        }

        body.Children.Add(InfoCard(
            "Significado dos estados",
            "VALIDADO: metadados. ELF: gerado para o link atual. ENVIADO: bytes entregues ao BinLoader TCP 9090. BAIXANDO (MANUAL): download visto por você no PS4. MANUAL OK: instalação e execução confirmadas por você. O BinLoader 9090 não fornece progresso automático BGFT."));

        body.Children.Add(InfoCard(
            "Privacidade",
            "O histórico guarda apenas nome, Title ID, tamanho, provedor, etapa e IP do PS4. URLs assinadas, cookies e tokens não são gravados."));

        return new ScrollView { BackgroundColor = Background, Content = body };
    }

    static View HistoryCard(UiHistoryItem item, int index, MainPage page, Action onHistoryChanged)
    {
        var frame = Card();
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new Label
        {
            Text = string.IsNullOrWhiteSpace(item.Title) ? "Pacote PS4" : item.Title,
            TextColor = Text,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        grid.Children.Add(title);

        var status = StatusPill(item.Status, StatusColor(item.Status));
        Grid.SetColumn(status, 1);
        grid.Children.Add(status);

        var parts = new List<string>();
        parts.Add(string.IsNullOrWhiteSpace(item.TitleId) ? "PKG" : item.TitleId);
        parts.Add(string.IsNullOrWhiteSpace(item.SizeText) ? "—" : item.SizeText);
        if (!string.IsNullOrWhiteSpace(item.Kind)) parts.Add(item.Kind);
        var metadata = new Label
        {
            Text = string.Join("  •  ", parts),
            TextColor = Muted,
            FontSize = 11
        };
        Grid.SetRow(metadata, 1);
        Grid.SetColumnSpan(metadata, 2);
        grid.Children.Add(metadata);

        string footerText = (index == 0 ? "Mais recente  •  " : "") + item.Provider;
        if (!string.IsNullOrWhiteSpace(item.Ps4Ip)) footerText += "  •  " + item.Ps4Ip;
        var footer = new Label { Text = footerText, TextColor = Cyan, FontSize = 10 };
        Grid.SetRow(footer, 2);
        Grid.SetColumnSpan(footer, 2);
        grid.Children.Add(footer);

        var cardBody = new VerticalStackLayout { Spacing = 9 };
        cardBody.Children.Add(grid);
        if (string.Equals(item.Status, "PAYLOAD ENVIADO", StringComparison.OrdinalIgnoreCase))
        {
            var downloading = new Button
            {
                Text = "Marcar download visto no PS4 (manual)",
                FontSize = 11,
                BackgroundColor = Surface2,
                TextColor = Cyan,
                BorderColor = Stroke,
                BorderWidth = 1,
                CornerRadius = 10,
                HeightRequest = 38
            };
            downloading.Clicked += async (_, _) =>
            {
                bool yes = await page.DisplayAlert(
                    "Download observado no console",
                    "O PS4 realmente mostrou este pacote em Notificações > Downloads? Isso registra apenas sua confirmação, sem consultar o BGFT.",
                    "Sim, está baixando",
                    "Cancelar");
                if (!yes || !UiStateStore.ConfirmDownloadingManually(item.Key)) return;
                onHistoryChanged?.Invoke();
                downloading.IsEnabled = false;
                downloading.Text = "✓ Baixando (confirmação manual)";
                if (status is Frame badge && badge.Content is Label badgeText)
                {
                    badgeText.Text = "BAIXANDO";
                    badgeText.TextColor = Cyan;
                }
            };
            cardBody.Children.Add(downloading);
        }
        if ((item.Status ?? "").IndexOf("CONCLUÍDO", StringComparison.OrdinalIgnoreCase) < 0)
        {
            var confirm = new Button
            {
                Text = "Confirmar conclusão no PS4",
                FontSize = 11,
                BackgroundColor = Surface2,
                TextColor = Cyan,
                BorderColor = Stroke,
                BorderWidth = 1,
                CornerRadius = 10,
                HeightRequest = 38
            };
            confirm.Clicked += async (_, _) =>
            {
                bool yes = await page.DisplayAlert(
                    "Confirmação manual",
                    "O download terminou, o pacote foi instalado e abriu no PS4? Essa confirmação não consulta o BGFT.",
                    "Sim, confirmei",
                    "Cancelar");
                if (!yes) return;
                if (!UiStateStore.ConfirmCompletedManually(item.Key)) return;
                onHistoryChanged?.Invoke();
                confirm.IsEnabled = false;
                confirm.Text = "✓ Confirmado manualmente";
                if (status is Frame badge)
                {
                    badge.BorderColor = Green;
                    if (badge.Content is Label badgeText)
                    {
                        badgeText.Text = "MANUAL OK";
                        badgeText.TextColor = Green;
                    }
                }
            };
            cardBody.Children.Add(confirm);
        }
        frame.Content = cardBody;
        return frame;
    }

    static Frame Card() => new Frame
    {
        Padding = new Thickness(14),
        CornerRadius = 18,
        BorderColor = Stroke,
        BackgroundColor = Surface,
        HasShadow = false,
        HorizontalOptions = LayoutOptions.Fill
    };

    static View InfoCard(string title, string text)
    {
        var frame = Card();
        frame.Content = new VerticalStackLayout
        {
            Spacing = 5,
            Children =
            {
                new Label { Text = title, TextColor = Cyan, FontSize = 15, FontAttributes = FontAttributes.Bold },
                new Label { Text = text, TextColor = Muted, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap }
            }
        };
        return frame;
    }

    static View KpiRow(Action<int, Label> onValueCreated, params (string Title, string Value, Color Color)[] items)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var frame = Card();
            frame.Padding = new Thickness(10, 11);
            var value = new Label
            {
                Text = items[i].Value,
                TextColor = items[i].Color,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            onValueCreated?.Invoke(i, value);
            frame.Content = new VerticalStackLayout
            {
                Spacing = 3,
                Children =
                {
                    new Label { Text = items[i].Title, TextColor = Muted, FontSize = 9, FontAttributes = FontAttributes.Bold },
                    value
                }
            };
            Grid.SetColumn(frame, i);
            grid.Children.Add(frame);
        }
        return grid;
    }

    static View StatusPill(string text, Color color)
    {
        var frame = new Frame
        {
            Padding = new Thickness(8, 5),
            CornerRadius = 11,
            BorderColor = color,
            BackgroundColor = Surface2,
            HasShadow = false,
            HorizontalOptions = LayoutOptions.End
        };
        frame.Content = new Label
        {
            Text = ShortStatus(text),
            TextColor = color,
            FontSize = 9,
            FontAttributes = FontAttributes.Bold
        };
        return frame;
    }

    static Color StatusColor(string status)
    {
        string value = (status ?? "").ToUpperInvariant();
        if (value.Contains("CONCLUÍDO")) return Green;
        if (value.Contains("FALHA")) return Color.FromArgb("#FF4D6D");
        if (value.Contains("BAIXANDO")) return Cyan;
        if (value.Contains("ENVIANDO")) return Cyan;
        if (value.Contains("ENVIADO")) return Purple;
        if (value.Contains("GERADO")) return Blue;
        if (value.Contains("VALIDADO")) return Green;
        return Cyan;
    }

    static string ShortStatus(string status)
    {
        string value = (status ?? "").Trim().ToUpperInvariant();
        if (value.Length == 0) return "—";
        if (value.Contains("CONCLUÍDO")) return "MANUAL OK";
        if (value.Contains("FALHA")) return "FALHA";
        if (value.Contains("BAIXANDO")) return "BAIXANDO";
        if (value.Contains("ENVIANDO")) return "ENVIANDO";
        if (value.Contains("PAYLOAD ENVIADO")) return "ENVIADO";
        if (value.Contains("ELF GERADO")) return "ELF";
        if (value.Contains("VALIDADO")) return "VALIDADO";
        return value.Length > 12 ? value.Substring(0, 12) : value;
    }
}
