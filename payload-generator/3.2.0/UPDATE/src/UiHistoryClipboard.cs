using System;
using Android.App;
using Android.Content;

namespace Tayson.Providers;

/// <summary>Android clipboard bridge compatible with the trimmed MAUI base.</summary>
public static class UiHistoryClipboard
{
    static ClipboardManager Manager()
    {
        var context = Application.Context;
        if (context == null)
            throw new InvalidOperationException("Contexto Android indisponível.");
        var service = context.GetSystemService(Context.ClipboardService) as ClipboardManager;
        if (service == null)
            throw new InvalidOperationException("Área de transferência indisponível.");
        return service;
    }

    public static void Copy(string backup)
    {
        // Never persist backups to app-private files: they would be removed by uninstall.
        var clip = ClipData.NewPlainText("Tayson PS4 - backup do histórico", backup ?? "");
        Manager().PrimaryClip = clip;
    }

    public static string Paste()
    {
        var manager = Manager();
        var clip = manager.PrimaryClip;
        if (clip == null || clip.ItemCount < 1) return "";
        return clip.GetItemAt(0)?.CoerceToText(Application.Context)?.ToString() ?? "";
    }
}
