using System;
using System.IO;
using System.Text;

namespace Tayson.Providers;

/// <summary>Byte limits and strict UTF-8 for portable history .txt documents.</summary>
public static class UiHistoryTextFile
{
    public const int MaxFileBytes = 131072;

    public static byte[] Encode(string backup)
    {
        // Validate before exporting. The SHA-less FNV protects accidental changes only.
        UiHistoryBackupCodec.Import(backup);
        var output = Encoding.UTF8.GetBytes(backup.Trim() + "\n");
        if (output.Length > MaxFileBytes)
            throw new InvalidDataException("Backup grande demais.");
        return output;
    }

    public static string Decode(Stream stream)
    {
        if (stream == null || !stream.CanRead) throw new InvalidDataException("Arquivo ilegível.");
        using var memory = new MemoryStream();
        byte[] buffer = new byte[4096];
        int total = 0;
        while (true)
        {
            int n = stream.Read(buffer, 0, buffer.Length);
            if (n <= 0) break;
            total += n;
            if (total > MaxFileBytes)
                throw new InvalidDataException("O arquivo excede o limite.");
            memory.Write(buffer, 0, n);
        }
        byte[] bytes = memory.ToArray();
        string data;
        try { data = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new InvalidDataException("UTF-8 inválido."); }
        if (data.Length != 0 && data[0] == '\uFEFF') data = data.Substring(1);
        // The backup codec verifies checksum, record count and restricted fields.
        UiHistoryBackupCodec.Import(data);
        return data;
    }
}
