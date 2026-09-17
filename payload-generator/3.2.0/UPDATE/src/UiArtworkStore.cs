using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tayson.Providers;

/// <summary>
/// Session-only artwork cache used by the Tayson 3.4.0 UI.
///
/// The first pass scans the bounded PKG metadata prefix. The extended pass also
/// reads the PS4 PKG file table and, when available, asks the already-validated
/// provider URL for small ranges containing icon0/pic0/pic1 artwork. Artwork is
/// never persisted and provider credentials/URLs are never stored here.
/// </summary>
public static class UiArtworkStore
{
    sealed class ArtworkEntry
    {
        public uint Id;
        public long Offset;
        public int Size;
    }

    static readonly Dictionary<string, byte[][]> Cache = new Dictionary<string, byte[][]>(StringComparer.Ordinal);
    const int MaxImages = 5;
    const int MaxImageBytes = 2097152; // 2 MiB per decoded source image

    static readonly byte[] PngStart = { 137, 80, 78, 71, 13, 10, 26, 10 };
    static readonly byte[] PngEnd = { 73, 69, 78, 68, 174, 66, 96, 130 };

    // PS4 system artwork IDs, in the order we prefer to show them.
    static readonly uint[] PreferredIds =
    {
        0x1200, // icon0.png
        0x1220, // pic0.png
        0x1006, // pic1.png
        0x100C, // shareoverlayimage.png
        0x100D, // save_data.png
        0x100E  // shareprivacyguardimage.png
    };

    public static byte[][] Extract(byte[] prefix)
    {
        if (prefix == null || prefix.Length < 16) return Array.Empty<byte[]>();
        var result = new List<byte[]>();
        ExtractEmbedded(prefix, result);
        return result.ToArray();
    }

    public static async Task<byte[][]> ExtractExtendedAsync(byte[] prefix, Func<long, int, Task<byte[]>> rangeReader)
    {
        if (prefix == null || prefix.Length < 0x20) return Array.Empty<byte[]>();
        var result = new List<byte[]>();
        ExtractEmbedded(prefix, result);
        if (result.Count >= MaxImages || rangeReader == null) return result.ToArray();

        List<ArtworkEntry> entries = ReadArtworkEntries(prefix);
        foreach (uint id in PreferredIds)
        {
            if (result.Count >= MaxImages) break;
            ArtworkEntry entry = null;
            foreach (ArtworkEntry item in entries)
                if (item.Id == id) { entry = item; break; }
            if (entry == null || entry.Size <= 32 || entry.Size > MaxImageBytes || entry.Offset < 0) continue;

            try
            {
                byte[] image;
                long end = entry.Offset + entry.Size;
                if (end >= entry.Offset && end <= prefix.Length)
                {
                    image = new byte[entry.Size];
                    Array.Copy(prefix, (int)entry.Offset, image, 0, entry.Size);
                }
                else
                {
                    image = await rangeReader(entry.Offset, entry.Size).ConfigureAwait(false);
                }

                if (IsImage(image) && !ContainsEquivalent(result, image))
                    result.Add(image);
            }
            catch
            {
                // Artwork is optional. A provider refusing one auxiliary range
                // must never turn a valid PKG into a validation failure.
            }
        }

        return result.ToArray();
    }

    public static void Set(string contentId, byte[][] images)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return;
        if (images == null || images.Length == 0)
        {
            Cache.Remove(contentId);
            return;
        }
        int count = Math.Min(MaxImages, images.Length);
        var copy = new byte[count][];
        for (int i = 0; i < count; i++) copy[i] = images[i];
        Cache[contentId] = copy;
    }

    public static byte[][] Get(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return Array.Empty<byte[]>();
        return Cache.TryGetValue(contentId, out byte[][] value) ? value : Array.Empty<byte[]>();
    }

    public static void Clear(string contentId)
    {
        if (!string.IsNullOrWhiteSpace(contentId)) Cache.Remove(contentId);
    }

    static void ExtractEmbedded(byte[] prefix, List<byte[]> result)
    {
        int position = 0;
        while (result.Count < MaxImages)
        {
            int start = Find(prefix, PngStart, position);
            if (start < 0) break;
            int end = Find(prefix, PngEnd, start + PngStart.Length);
            if (end < 0) break;
            int length = end + PngEnd.Length - start;
            if (length > 32 && length <= MaxImageBytes)
            {
                var image = new byte[length];
                Array.Copy(prefix, start, image, 0, length);
                if (!ContainsEquivalent(result, image)) result.Add(image);
            }
            position = end + PngEnd.Length;
        }

        position = 0;
        while (result.Count < MaxImages)
        {
            int start = FindJpegStart(prefix, position);
            if (start < 0) break;
            int end = FindJpegEnd(prefix, start + 3);
            if (end < 0) break;
            int length = end + 2 - start;
            if (length > 64 && length <= MaxImageBytes)
            {
                var image = new byte[length];
                Array.Copy(prefix, start, image, 0, length);
                if (!ContainsEquivalent(result, image)) result.Add(image);
            }
            position = end + 2;
        }
    }

    static List<ArtworkEntry> ReadArtworkEntries(byte[] prefix)
    {
        var result = new List<ArtworkEntry>();
        if (prefix.Length < 0x20) return result;
        uint count = ReadBe32(prefix, 0x10);
        uint table = ReadBe32(prefix, 0x18);
        if (count == 0 || count > 4096 || table > int.MaxValue) return result;
        long tableEnd = (long)table + (long)count * 0x20;
        if (tableEnd > prefix.Length) return result;

        for (uint i = 0; i < count; i++)
        {
            int p = checked((int)table + (int)i * 0x20);
            uint id = ReadBe32(prefix, p);
            if (!Interesting(id)) continue;
            uint offset = ReadBe32(prefix, p + 0x10);
            uint size = ReadBe32(prefix, p + 0x14);
            if (size == 0 || size > MaxImageBytes) continue;
            result.Add(new ArtworkEntry { Id = id, Offset = offset, Size = (int)size });
        }
        return result;
    }

    static bool Interesting(uint id)
    {
        foreach (uint preferred in PreferredIds) if (id == preferred) return true;
        // Language/region variants of icon0.png and pic1.png.
        return (id >= 0x1201 && id <= 0x121F) || (id >= 0x1241 && id <= 0x125F);
    }

    static uint ReadBe32(byte[] data, int offset)
        => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
           ((uint)data[offset + 2] << 8) | data[offset + 3];

    static bool IsImage(byte[] data)
    {
        if (data == null || data.Length < 8) return false;
        bool png = true;
        for (int i = 0; i < PngStart.Length; i++)
            if (data[i] != PngStart[i]) { png = false; break; }
        if (png) return true;
        return data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff;
    }

    static int Find(byte[] data, byte[] needle, int start)
    {
        for (int i = Math.Max(0, start); i <= data.Length - needle.Length; i++)
        {
            int n = 0;
            while (n < needle.Length && data[i + n] == needle[n]) n++;
            if (n == needle.Length) return i;
        }
        return -1;
    }

    static int FindJpegStart(byte[] data, int start)
    {
        for (int i = Math.Max(0, start); i + 2 < data.Length; i++)
            if (data[i] == 0xff && data[i + 1] == 0xd8 && data[i + 2] == 0xff) return i;
        return -1;
    }

    static int FindJpegEnd(byte[] data, int start)
    {
        for (int i = Math.Max(0, start); i + 1 < data.Length; i++)
            if (data[i] == 0xff && data[i + 1] == 0xd9) return i;
        return -1;
    }

    static bool ContainsEquivalent(List<byte[]> items, byte[] candidate)
    {
        foreach (byte[] item in items)
        {
            if (item == null || item.Length != candidate.Length) continue;
            int probe = Math.Min(32, item.Length);
            bool same = true;
            for (int i = 0; i < probe; i++)
                if (item[i] != candidate[i] || item[item.Length - 1 - i] != candidate[candidate.Length - 1 - i])
                {
                    same = false;
                    break;
                }
            if (same) return true;
        }
        return false;
    }
}
