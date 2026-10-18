using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tayson.Providers;

/// <summary>
/// Portable clipboard backup of the existing non-secret UI history.
/// Not encrypted. Never stores routes, signed download URLs, headers,
/// cookies, authentication material, or anything beyond UiHistoryItem.
/// </summary>
public static class UiHistoryBackupCodec
{
    const string Magic = "TAYSON-HISTORY-BACKUP-V1";
    const string CheckPrefix = "FNV32:";
    public const int MaxItems = 20;
    public const int MaxBackupChars = 32768;
    static readonly int[] Limits = { 192, 160, 32, 52, 32, 72, 64, 32 };

    static string[] Fields(UiHistoryItem x) => new[]
    {
        x.Key, x.Title, x.TitleId, x.Provider, x.Kind, x.Status, x.Ps4Ip, x.SizeText
    };

    static void Validate(string[] fields)
    {
        if (fields.Length != 8 || string.IsNullOrWhiteSpace(fields[0]))
            throw new InvalidDataException("Registro sem identificador.");
        for (int i = 0; i < fields.Length; i++)
        {
            string value = fields[i] ?? "";
            if (value.Length > Limits[i])
                throw new InvalidDataException("Campo do histórico ultrapassa o limite.");
            if (value.IndexOf("://", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("sig=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("token=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("cookie=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("x-api-key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("bearer ", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidDataException("Metadado contém possível informação sensível.");
            foreach (char ch in value)
                if (char.IsControl(ch))
                    throw new InvalidDataException("Campo com caracteres de controle.");
        }
    }

    static string Encode(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? ""));

    static string Decode(string s)
    {
        if (s.Length > 1024) throw new InvalidDataException("Campo codificado grande demais.");
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    // FNV-1a is an accidental-corruption check, NOT a digital signature.
    static string Checksum(string s)
    {
        uint h = 2166136261u;
        byte[] data = Encoding.UTF8.GetBytes(s);
        foreach (byte b in data) h = unchecked((h ^ b) * 16777619u);
        return h.ToString("x8");
    }

    public static string Export(IReadOnlyList<UiHistoryItem> items)
    {
        if (items == null || items.Count > MaxItems)
            throw new InvalidDataException("Quantidade de registros inválida.");
        var lines = new List<string> { Magic, items.Count.ToString() };
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (UiHistoryItem item in items)
        {
            if (item == null) throw new InvalidDataException("Registro inválido.");
            var fields = Fields(item);
            Validate(fields);
            if (!keys.Add(fields[0]))
                throw new InvalidDataException("Registro duplicado.");
            for (int i = 0; i < fields.Length; i++) fields[i] = Encode(fields[i]);
            lines.Add(string.Join("|", fields));
        }
        string body = string.Join("\n", lines);
        string result = body + "\n" + CheckPrefix + Checksum(body);
        if (result.Length > MaxBackupChars)
            throw new InvalidDataException("Backup ultrapassa o tamanho permitido.");
        return result;
    }

    public static IReadOnlyList<UiHistoryItem> Import(string backup)
    {
        if (string.IsNullOrWhiteSpace(backup) || backup.Length > MaxBackupChars)
            throw new InvalidDataException("Backup ausente ou grande demais.");
        string normalized = backup.Replace("\r\n", "\n").Trim();
        string[] lines = normalized.Split('\n');
        if (lines.Length < 3 || lines[0] != Magic ||
            !int.TryParse(lines[1], out int n) || n < 0 || n > MaxItems ||
            lines.Length != n + 3)
            throw new InvalidDataException("Formato ou versão de backup inválida.");
        string body = string.Join("\n", lines, 0, lines.Length - 1);
        if (lines[lines.Length - 1] != CheckPrefix + Checksum(body))
            throw new InvalidDataException("Backup alterado ou incompleto.");
        var output = new List<UiHistoryItem>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int j = 0; j < n; j++)
        {
            string[] encoded = lines[j + 2].Split('|');
            if (encoded.Length != 8) throw new InvalidDataException("Registro incompleto.");
            var fields = new string[8];
            try
            {
                for (int i = 0; i < fields.Length; i++) fields[i] = Decode(encoded[i]);
            }
            catch (FormatException)
            {
                throw new InvalidDataException("Codificação de backup inválida.");
            }
            Validate(fields);
            if (!keys.Add(fields[0])) throw new InvalidDataException("Registro duplicado.");
            output.Add(new UiHistoryItem
            {
                Key = fields[0],
                Title = fields[1],
                TitleId = fields[2],
                Provider = fields[3],
                Kind = fields[4],
                Status = fields[5],
                Ps4Ip = fields[6],
                SizeText = fields[7]
            });
        }
        return output;
    }

    /// <summary>Existing device records win duplicate keys; newest 20 retained.</summary>
    public static List<UiHistoryItem> Merge(
        IReadOnlyList<UiHistoryItem> existing,
        IReadOnlyList<UiHistoryItem> imported)
    {
        if (existing == null || imported == null)
            throw new InvalidDataException("Histórico inválido.");
        var items = new List<UiHistoryItem>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (IReadOnlyList<UiHistoryItem> source in new[] { existing, imported })
        {
            foreach (UiHistoryItem item in source)
            {
                if (item == null) continue;
                Validate(Fields(item));
                if (!keys.Add(item.Key)) continue;
                items.Add(item);
                if (items.Count == MaxItems) return items;
            }
        }
        return items;
    }
}
