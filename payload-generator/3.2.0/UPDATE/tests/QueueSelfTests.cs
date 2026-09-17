using System.Runtime.CompilerServices;
using Tayson.Providers;

internal static class QueueSelfTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string first = UiSendQueue.LoadClipboard(
            "https://example.test/a.pkg\nhttps://example.test/b.pkg\nhttps://example.test/b.pkg\ntexto ignorado\nhttps://example.test/c.pkg",
            out int total);
        if (first != "https://example.test/a.pkg") throw new Exception("Queue first item mismatch");
        if (total != 3) throw new Exception("Queue must de-duplicate and count valid URLs");
        if (UiSendQueue.PendingCount != 2) throw new Exception("Queue pending count mismatch");

        if (!UiSendQueue.TryPeekNext(out string second, out int afterSecond) ||
            second != "https://example.test/b.pkg" || afterSecond != 1)
            throw new Exception("Queue peek mismatch");
        if (UiSendQueue.PendingCount != 2)
            throw new Exception("Peek must not consume the next PKG");
        if (UiSendQueue.CommitNext("https://example.test/wrong.pkg", out _))
            throw new Exception("Commit must reject a mismatched expected URL");
        if (UiSendQueue.PendingCount != 2)
            throw new Exception("Rejected commit must preserve the queue");
        if (!UiSendQueue.CommitNext(second, out int remaining) || remaining != 1)
            throw new Exception("Queue commit mismatch");

        if (!UiSendQueue.TryPeekNext(out string third, out int afterThird) ||
            third != "https://example.test/c.pkg" || afterThird != 0)
            throw new Exception("Queue third item peek mismatch");
        if (!UiSendQueue.CommitNext(third, out remaining) || remaining != 0)
            throw new Exception("Queue third item commit mismatch");
        if (UiSendQueue.TryPeekNext(out _, out _)) throw new Exception("Queue should be empty");

        string single = UiSendQueue.LoadClipboard("https://example.test/only.pkg", out total);
        if (single != "https://example.test/only.pkg" || total != 1 || UiSendQueue.PendingCount != 0)
            throw new Exception("Queue single-link behavior mismatch");

        UiSendQueue.Clear();
        Console.WriteLine("PASS session queue transactional self-test");
    }
}
