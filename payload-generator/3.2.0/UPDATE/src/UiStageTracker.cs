using System;
using System.Net.Sockets;

namespace Tayson.Providers;

/// <summary>
/// Local, non-persistent evidence for the current link. Button.IsEnabled is NOT
/// evidence that an ELF exists. A BinLoader write is NOT BGFT acknowledgement.
/// </summary>
public static class UiStageTracker
{
    public enum Stage { AwaitingLink, LinkReceived, Validating, Validated, ElfGenerated, Sending, ElfSent, Failed }
    static readonly object Gate = new object();
    static string currentUrl = "";
    static Stage current = Stage.AwaitingLink;

    public static event Action Changed;

    static string Normalize(string url) => (url ?? "").Trim();

    public static Stage Current(string url)
    {
        string normalized = Normalize(url);
        if (normalized.Length == 0) return Stage.AwaitingLink;
        lock (Gate)
            return normalized == currentUrl ? current : Stage.LinkReceived;
    }

    public static void LinkChanged(string url)
    {
        string normalized = Normalize(url);
        bool changed = false;
        lock (Gate)
        {
            if (normalized != currentUrl)
            {
                currentUrl = normalized;
                current = normalized.Length == 0 ? Stage.AwaitingLink : Stage.LinkReceived;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    public static void BeginValidation(string url)
    {
        string normalized = Normalize(url);
        if (normalized.Length == 0) { LinkChanged(""); return; }
        lock (Gate) { currentUrl = normalized; current = Stage.Validating; }
        Changed?.Invoke();
    }

    public static void MarkValidated(string url) => Advance(url, Stage.Validated);
    public static void MarkGenerated(string url) => Advance(url, Stage.ElfGenerated);
    public static void MarkSending(string url) => Advance(url, Stage.Sending);
    public static void MarkSent(string url) => Advance(url, Stage.ElfSent);

    public static void MarkFailed(string url)
    {
        string normalized = Normalize(url);
        if (normalized.Length == 0) return;
        lock (Gate) { if (normalized != currentUrl) return; current = Stage.Failed; }
        Changed?.Invoke();
    }

    static void Advance(string url, Stage stage)
    {
        string normalized = Normalize(url);
        if (normalized.Length == 0) return;
        bool changed = false;
        lock (Gate)
        {
            if (normalized != currentUrl) return; // Out-of-order async response for an older link.
            if ((current == Stage.Validating || current == Stage.Validated ||
                 current == Stage.ElfGenerated || current == Stage.Sending) &&
                stage > current && stage <= Stage.ElfSent)
            {
                current = stage;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    public static string Title(Stage stage) => stage switch
    {
        Stage.AwaitingLink => "○ Aguardando link",
        Stage.LinkReceived => "○ Link recebido",
        Stage.Validating => "◌ Validando PKG",
        Stage.Validated => "✓ PKG validado",
        Stage.ElfGenerated => "✓ ELF gerado para este link",
        Stage.Sending => "◌ Enviando ELF ao PS4",
        Stage.ElfSent => "✓ ELF transmitido ao BinLoader",
        Stage.Failed => "× Operação não concluída",
        _ => "○ Estado desconhecido"
    };

    public static string Detail(Stage stage) => stage switch
    {
        Stage.AwaitingLink => "Cole um link compatível para iniciar.",
        Stage.LinkReceived => "Valide o link antes de gerar um novo ELF.",
        Stage.Validating => "Aguardando validação e metadados do pacote.",
        Stage.Validated => "Metadados validados; gere o ELF para este link.",
        Stage.ElfGenerated => "ELF gerado nesta sessão; ainda não enviado.",
        Stage.Sending => "Transmissão TCP 9090 em andamento. Não reenvie em paralelo.",
        Stage.ElfSent => "Bytes transmitidos; o progresso BGFT deve ser confirmado no PS4.",
        Stage.Failed => "Veja o diagnóstico no Registro. Não reenvie sem verificar.",
        _ => ""
    };
}

/// <summary>
/// Stable error categories without leaking signed provider URLs or credentials.
/// </summary>
public static class UiTransportDiagnostics
{
    public static string Describe(Exception error, bool startedSending)
    {
        if (error is TimeoutException)
            return startedSending
                ? "[PS4-9090-TIMEOUT] BinLoader não respondeu. Verifique IP e GoldHEN."
                : "[PREPARO-TIMEOUT] Tempo esgotado ao preparar PKG/ELF. Confira provedor e Worker.";
        if (error is SocketException socket)
            return socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused =>
                    "[PS4-9090-RECUSADO] BinLoader recusou TCP 9090. Ative-o no GoldHEN.",
                SocketError.HostUnreachable or SocketError.NetworkUnreachable =>
                    "[PS4-REDE] Console inacessível na LAN. Confirme IP e Wi-Fi.",
                SocketError.TimedOut =>
                    "[PS4-9090-TIMEOUT] Tentativas TCP esgotadas. Confira o BinLoader.",
                _ => "[PS4-SOCKET] Falha TCP 9090 (" + socket.SocketErrorCode + ")."
            };
        if (error is System.IO.IOException)
            return startedSending
                ? "[PS4-ENVIO-INCERTO] A conexão caiu durante o ELF. Verifique o PS4 antes de reenviar; parte dos bytes pode ter sido recebida."
                : "[PKG-LEITURA] Não foi possível acessar os dados do pacote.";
        if (error is System.Net.Http.HttpRequestException)
            return "[WORKER-REDE] Falha HTTP ao preparar a rota. Confira Worker e provedor.";
        if (error is FormatException or ArgumentException)
            return "[PKG-FORMATO] Link ou metadados inválidos. Valide outro link.";
        if (error is System.Threading.Tasks.TaskCanceledException)
            return "[WORKER-TIMEOUT] Solicitação cancelada ou expirada. Confira rede e Worker.";
        return "[FLUXO-" + (error?.GetType().Name ?? "Erro") +
            "] Não foi possível concluir. Consulte Worker, link e estado do PS4.";
    }
}
