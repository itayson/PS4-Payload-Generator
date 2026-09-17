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

        if (!UiSendQueue.TryTakeNext(out string second, out int remaining) || second != "https://example.test/b.pkg" || remaining != 1)
            throw new Exception("Queue second item mismatch");
        if (!UiSendQueue.TryTakeNext(out string third, out remaining) || third != "https://example.test/c.pkg" || remaining != 0)
            throw new Exception("Queue third item mismatch");
        if (UiSendQueue.TryTakeNext(out _, out _)) throw new Exception("Queue should be empty");

        string single = UiSendQueue.LoadClipboard("https://example.test/only.pkg", out total);
        if (single != "https://example.test/only.pkg" || total != 1 || UiSendQueue.PendingCount != 0)
            throw new Exception("Queue single-link behavior mismatch");

        UiSendQueue.Clear();
        Console.WriteLine("PASS session queue self-test");
    }
}
