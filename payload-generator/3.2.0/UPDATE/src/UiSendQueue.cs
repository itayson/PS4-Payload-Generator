using System;
using System.Collections.Generic;

namespace Tayson.Providers;

/// <summary>
/// Session-only queue for one-touch delivery. The queue stores only URLs in
/// memory and is cleared when the app process exits; signed URLs are never
/// persisted to Preferences/history.
///
/// Queue advancement is transactional: callers peek the next item first and
/// commit it only after the next validation has actually been started. This
/// prevents a transient busy UI from silently dropping a queued PKG.
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

    public static bool TryPeekNext(out string url, out int remainingAfterCurrent)
    {
        lock (Gate)
        {
            if (Pending.Count == 0)
            {
                url = "";
                remainingAfterCurrent = 0;
                return false;
            }

            url = Pending.Peek();
            remainingAfterCurrent = Math.Max(0, Pending.Count - 1);
            return true;
        }
    }

    public static bool CommitNext(string expectedUrl, out int remaining)
    {
        lock (Gate)
        {
            if (Pending.Count == 0 ||
                !string.Equals(Pending.Peek(), expectedUrl ?? "", StringComparison.Ordinal))
            {
                remaining = Pending.Count;
                return false;
            }

            Pending.Dequeue();
            remaining = Pending.Count;
            return true;
        }
    }

    // Kept for compatibility with older callers/tests. New queue-flow code
    // should prefer TryPeekNext + CommitNext so failed starts cannot lose data.
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
