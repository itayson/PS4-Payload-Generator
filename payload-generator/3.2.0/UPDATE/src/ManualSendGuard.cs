using System;
using System.Reflection;
using Microsoft.Maui.Controls;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Defensive preflight for the legacy manual "Enviar ao PS4" button.
/// Called BEFORE the original async-void handler allocates its state machine.
/// Does not connect to the console, generate an ELF, persist a credential or
/// bypass the user's explicit manual-send action.
/// </summary>
public static class ManualSendGuard
{
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    public static bool Allow(MainPage page)
    {
        if (page == null) return false;
        try
        {
            var entry = typeof(MainPage).GetField("txtIp", Fields)?.GetValue(page) as Entry;
            if (Ps4LanAddressPolicy.TryNormalize(entry?.Text, out _))
                return true;

            SetStatus(page, Ps4LanAddressPolicy.RejectionMessage);
            return false;
        }
        catch
        {
            SetStatus(page, "Não foi possível validar o destino. O envio manual foi cancelado.");
            return false; // Fail closed: never send to an unchecked endpoint.
        }
    }

    static void SetStatus(MainPage page, string message)
    {
        try
        {
            var label = typeof(MainPage).GetField("lblBottomStatus", Fields)?.GetValue(page) as Label;
            if (label != null) label.Text = message;
        }
        catch { /* Diagnostic UI cannot make the guard fail open. */ }
    }
}
