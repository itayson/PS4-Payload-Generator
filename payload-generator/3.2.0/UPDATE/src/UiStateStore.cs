using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Maui.Storage;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Lightweight persistent UI state for the redesign.
///
/// Only non-sensitive package metadata is stored. Signed provider URLs,
/// cookies and tokens are intentionally excluded from history.
/// The order in storage is newest-first, avoiding DateTime/parse APIs that are
/// trimmed from the original MAUI application.
/// </summary>
public sealed class UiHistoryItem
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string TitleId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string Ps4Ip { get; set; } = "";
    public string SizeText { get; set; } = "";
}

public static class UiStateStore
{
    const string HistoryKey = "tayson.ui.history.v2";
    const string Ps4IpKey = "tayson.ui.ps4ip.v1";
    const string WorkerUrlKey = "tayson.ui.worker.v1";
    const int MaxItems = 20;

    public static string LoadPs4Ip(string fallback = "")
        => Preferences.Default.Get(Ps4IpKey, fallback ?? "");

    public static void SavePs4Ip(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 0) Preferences.Default.Set(Ps4IpKey, value);
    }

    public static string LoadWorkerUrl(string fallback = BgftRoutePlanner.DefaultWorkerBase)
        => Preferences.Default.Get(WorkerUrlKey, fallback ?? BgftRoutePlanner.DefaultWorkerBase);

    public static void SaveWorkerUrl(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 0) Preferences.Default.Set(WorkerUrlKey, value);
    }

    public static IReadOnlyList<UiHistoryItem> ReadHistory()
    {
        string raw = Preferences.Default.Get(HistoryKey, "");
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<UiHistoryItem>();

        var result = new List<UiHistoryItem>();
        foreach (string line in raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string[] p = line.Split('|');
                if (p.Length != 8) continue;
                result.Add(new UiHistoryItem
                {
                    Key = Decode(p[0]),
                    Title = Decode(p[1]),
                    TitleId = Decode(p[2]),
                    Provider = Decode(p[3]),
                    Kind = Decode(p[4]),
                    Status = Decode(p[5]),
                    Ps4Ip = Decode(p[6]),
                    SizeText = Decode(p[7])
                });
            }
            catch { }
            if (result.Count >= MaxItems) break;
        }
        return result;
    }

    public static void Record(PkgMetadata meta, string status, string ps4Ip)
    {
        if (meta == null) return;
        var items = new List<UiHistoryItem>();
        foreach (UiHistoryItem item in ReadHistory()) items.Add(item);
        string key = PackageKey(meta);
        items.RemoveAll(x => x.Key == key);
        items.Insert(0, new UiHistoryItem
        {
            Key = key,
            Title = string.IsNullOrWhiteSpace(meta.DisplayName) ? "Pacote PS4" : meta.DisplayName,
            TitleId = meta.TitleId ?? "",
            Provider = ProviderName(meta.Url),
            Kind = PackageKind(meta.PkgType),
            Status = string.IsNullOrWhiteSpace(status) ? "VALIDADO" : status,
            Ps4Ip = (ps4Ip ?? "").Trim(),
            SizeText = HumanSize(meta.ContentLength)
        });
        if (items.Count > MaxItems) items.RemoveRange(MaxItems, items.Count - MaxItems);
        Write(items);
    }

    /// <summary>
    /// A user-entered attestation, NOT a BGFT status or cloud telemetry result.
    /// Keeps only existing non-sensitive metadata; signed URLs are never stored.
    /// </summary>
    public static bool ConfirmCompletedManually(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var items = new List<UiHistoryItem>();
        foreach (UiHistoryItem item in ReadHistory()) items.Add(item);
        int found = items.FindIndex(x => string.Equals(x.Key, key, StringComparison.Ordinal));
        if (found < 0) return false;
        UiHistoryItem completed = items[found];
        completed.Status = "CONCLUÍDO (MANUAL)";
        items.RemoveAt(found);
        items.Insert(0, completed);
        Write(items);
        return true;
    }

    public static string ExportHistoryBackup() =>
        UiHistoryBackupCodec.Export(ReadHistory());

    /// <summary>
    /// Validates the entire backup before changing local preferences.
    /// Existing local records take precedence; only non-sensitive history is merged.
    /// </summary>
    public static int RestoreHistoryBackup(string backup)
    {
        var imported = UiHistoryBackupCodec.Import(backup);
        var merged = UiHistoryBackupCodec.Merge(ReadHistory(), imported);
        Write(merged);
        return merged.Count;
    }

    public static void ClearHistory() => Preferences.Default.Set(HistoryKey, "");

    static void Write(IEnumerable<UiHistoryItem> items)
    {
        var lines = new List<string>();
        foreach (UiHistoryItem x in items)
        {
            lines.Add(string.Join("|", new[]
            {
                Encode(x.Key),
                Encode(x.Title),
                Encode(x.TitleId),
                Encode(x.Provider),
                Encode(x.Kind),
                Encode(x.Status),
                Encode(x.Ps4Ip),
                Encode(x.SizeText)
            }));
        }
        Preferences.Default.Set(HistoryKey, string.Join("\n", lines));
    }

    static string PackageKey(PkgMetadata meta)
        => meta.ContentId ?? meta.TitleId ?? meta.DisplayName ?? "pkg";

    static string ProviderName(string url)
    {
        string value = (url ?? "").ToLowerInvariant();
        if (value.Contains("akirabox")) return "AkiraBox";
        if (value.Contains("vikingfile") || value.Contains("r2.cloudflarestorage.com")) return "VikingFile / R2";
        if (value.Contains("rootz")) return "Rootz";
        if (value.Contains("mocha")) return "Mocha";
        if (value.Contains("filekeeper") || value.Contains("dlproxy")) return "FileKeeper";
        return "Link direto";
    }

    static string PackageKind(string type)
        => type == "PS4PATCH" ? "Atualização" : type == "PS4AC" ? "DLC" : "Jogo";

    static string HumanSize(long bytes)
    {
        if (bytes <= 0) return "—";
        double gb = bytes / 1024d / 1024d / 1024d;
        return gb >= 1 ? gb.ToString("0.00") + " GB" : (bytes / 1024d / 1024d).ToString("0.0") + " MB";
    }

    static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));

    static string Decode(string value)
        => Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
}
