using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Maui.Storage;
using PS4RemotePkgMobile;

namespace Tayson.Providers;

/// <summary>
/// Lightweight persistent UI state for the redesign.
///
/// Only non-sensitive package metadata is stored. Signed provider URLs,
/// cookies and tokens are intentionally excluded from history.
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
    public long Size { get; set; }
    public long Timestamp { get; set; }
}

public static class UiStateStore
{
    const string HistoryKey = "tayson.ui.history.v1";
    const string Ps4IpKey = "tayson.ui.ps4ip.v1";
    const int MaxItems = 20;

    public static string LoadPs4Ip(string fallback = "")
        => Preferences.Default.Get(Ps4IpKey, fallback ?? "");

    public static void SavePs4Ip(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 0) Preferences.Default.Set(Ps4IpKey, value);
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
                if (p.Length != 9) continue;
                if (!long.TryParse(p[0], out long timestamp)) continue;
                if (!long.TryParse(p[1], out long size)) continue;
                result.Add(new UiHistoryItem
                {
                    Timestamp = timestamp,
                    Size = size,
                    Key = Decode(p[2]),
                    Title = Decode(p[3]),
                    TitleId = Decode(p[4]),
                    Provider = Decode(p[5]),
                    Kind = Decode(p[6]),
                    Status = Decode(p[7]),
                    Ps4Ip = Decode(p[8])
                });
            }
            catch { }
        }
        return result.OrderByDescending(x => x.Timestamp).Take(MaxItems).ToArray();
    }

    public static void Record(PkgMetadata meta, string status, string ps4Ip)
    {
        if (meta == null) return;
        var items = ReadHistory().ToList();
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
            Size = meta.ContentLength,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        Write(items.Take(MaxItems));
    }

    public static void ClearHistory() => Preferences.Default.Remove(HistoryKey);

    static void Write(IEnumerable<UiHistoryItem> items)
    {
        string raw = string.Join("\n", items.Select(x => string.Join("|", new[]
        {
            x.Timestamp.ToString(),
            x.Size.ToString(),
            Encode(x.Key),
            Encode(x.Title),
            Encode(x.TitleId),
            Encode(x.Provider),
            Encode(x.Kind),
            Encode(x.Status),
            Encode(x.Ps4Ip)
        })));
        Preferences.Default.Set(HistoryKey, raw);
    }

    static string PackageKey(PkgMetadata meta)
        => (meta.ContentId ?? meta.TitleId ?? meta.DisplayName ?? "pkg") + ":" + meta.ContentLength;

    static string ProviderName(string url)
    {
        string value = (url ?? "").ToLowerInvariant();
        if (value.Contains("akirabox")) return "AkiraBox";
        if (value.Contains("vikingfile") || value.Contains("r2.cloudflarestorage.com")) return "VikingFile / R2";
        if (value.Contains("rootz")) return "Rootz";
        if (value.Contains("mocha")) return "Mocha";
        if (value.Contains("filekeeper")) return "FileKeeper";
        return "Link direto";
    }

    static string PackageKind(string type)
        => type == "PS4PATCH" ? "Atualização" : type == "PS4AC" ? "DLC" : "Jogo";

    static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));

    static string Decode(string value)
        => Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
}
