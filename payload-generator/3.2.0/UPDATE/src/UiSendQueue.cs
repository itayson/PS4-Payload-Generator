using System;
using System.Collections.Generic;

namespace Tayson.Providers;

/// <summary>
/// Session-only queue for one-touch delivery. The queue stores only URLs in
/// memory and is cleared when the app process exits; signed URLs are never
/// persisted to Preferences/history.
/// </summary>
public static class UiSendQueue
{
    static readonly object Gate = new object();
    static readonly Queue<string> Pending = new Queue<string>();
    const int MaxItems = 20;

    public static int PendingCount
    {
        get { lock (Gate) return Pending.Count; }
    }

    public static string LoadClipboard(string clipboardText, out int total)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string raw = clipboardText ?? "";

        foreach (string rawLine in raw.Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = (rawLine ?? "").Trim();
            if (line.Length == 0) continue;
            if (!line.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                continue;
            if (seen.Add(line)) urls.Add(line);
            if (urls.Count >= MaxItems) break;
        }

        // Normal single-link clipboard text may have no line breaks.
        if (urls.Count == 0)
        {
            string single = raw.Trim();
            if (single.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                single.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                urls.Add(single);
        }

        lock (Gate)
        {
            Pending.Clear();
            for (int i = 1; i < urls.Count; i++) Pending.Enqueue(urls[i]);
        }

        total = urls.Count;
        return urls.Count == 0 ? "" : urls[0];
    }

    public static bool TryTakeNext(out string url, out int remaining)
    {
        lock (Gate)
        {
            if (Pending.Count == 0)
            {
                url = "";
                remaining = 0;
                return false;
            }
            url = Pending.Dequeue();
            remaining = Pending.Count;
            return true;
        }
    }

    public static void Clear()
    {
        lock (Gate) Pending.Clear();
    }
}
