using System;
using System.Collections.Generic;

namespace Tayson.Providers;

/// <summary>
/// Session-only artwork cache used by the Tayson 3.4.0 UI.
///
/// Artwork is extracted from the already-downloaded PKG metadata prefix. It is
/// never persisted to Preferences and never stores provider URLs, cookies or
/// tokens. The cache is keyed by Content ID and capped to a small number of
/// images so the redesign stays lightweight.
/// </summary>
public static class UiArtworkStore
{
    static readonly Dictionary<string, byte[][]> Cache = new Dictionary<string, byte[][]>(StringComparer.Ordinal);
    const int MaxImages = 5;
    const int MaxImageBytes = 1572864; // 1.5 MiB per artwork

    static readonly byte[] PngStart = { 137, 80, 78, 71, 13, 10, 26, 10 };
    static readonly byte[] PngEnd = { 73, 69, 78, 68, 174, 66, 96, 130 };

    public static byte[][] Extract(byte[] prefix)
    {
        if (prefix == null || prefix.Length < 16) return Array.Empty<byte[]>();
        var result = new List<byte[]>();

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

        // Some packages expose JPEG artwork alongside PNG icon assets.
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
