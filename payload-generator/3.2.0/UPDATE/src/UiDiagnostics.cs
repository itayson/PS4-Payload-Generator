using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Tayson.Providers;

/// <summary>
/// Small in-memory diagnostic journal. It intentionally avoids persisting
/// provider URLs, cookies or tokens. Query strings found in arbitrary exception
/// messages are redacted before entering the journal.
/// </summary>
public static class UiDiagnostics
{
    static readonly object Gate = new object();
    static readonly Queue<string> Lines = new Queue<string>();
    const int MaxLines = 96;

    public static void Add(string stage, string message)
    {
        string safeStage = Clean(stage, 40);
        string safeMessage = Clean(message, 240);
        string line = DateTime.UtcNow.ToString("HH:mm:ss") + "Z  " + safeStage + "  " + safeMessage;
        lock (Gate)
        {
            Lines.Enqueue(line);
            while (Lines.Count > MaxLines) Lines.Dequeue();
        }
    }

    public static string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tayson PS4 Payload Generator 3.5.0");
        sb.AppendLine("Diagnóstico gerado em " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
        sb.AppendLine("Fila pendente: " + UiSendQueue.PendingCount);
        sb.AppendLine("Worker: " + UiStateStore.LoadWorkerUrl(BgftRoutePlanner.DefaultWorkerBase));
        sb.AppendLine();
        sb.AppendLine("Eventos:");
        lock (Gate)
        {
            if (Lines.Count == 0) sb.AppendLine("(nenhum evento registrado nesta sessão)");
            else foreach (string line in Lines) sb.AppendLine(line);
        }
        return sb.ToString();
    }

    public static void Clear()
    {
        lock (Gate) Lines.Clear();
    }

    static string Clean(string value, int limit)
    {
        string text = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        text = Regex.Replace(text, @"(https?://[^\s?]+)\?[^\s]+", "$1?<redacted>", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?i)(token|cookie|authorization|signature|sig)=([^\s&]+)", "$1=<redacted>");
        if (text.Length > limit) text = text.Substring(0, limit) + "…";
        return text;
    }
}
