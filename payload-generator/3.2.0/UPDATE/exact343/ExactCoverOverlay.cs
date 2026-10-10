using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using PS4RemotePkgMobile;
using Tayson.Providers;

namespace Tayson.Exact343Cover;

public static class ExactCoverOverlay
{
    const int IconCapacity = 1048576;
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task<byte[]> GenerateAsync(MainPage page)
    {
        byte[] payload = await Integration.GenerateAsync(page).ConfigureAwait(false);
        byte[] icon = ResolveIcon(page);
        InjectIcon(payload, icon);
        return payload;
    }

    static byte[] ResolveIcon(MainPage page)
    {
        object meta = typeof(MainPage).GetField("_currentMeta", PrivateInstance)?.GetValue(page);
        if (meta == null) return null;

        string contentId = meta.GetType().GetProperty("ContentId")?.GetValue(meta) as string;
        if (!string.IsNullOrWhiteSpace(contentId))
        {
            byte[][] artworks = UiArtworkStore.Get(contentId);
            if (artworks != null && artworks.Length > 0 && artworks[0] != null)
                return artworks[0];
        }

        return meta.GetType().GetProperty("IconBytes")?.GetValue(meta) as byte[];
    }

    static void InjectIcon(byte[] payload, byte[] icon)
    {
        if (payload == null) throw new InvalidDataException("Payload ausente.");

        byte[] iconMarker = Encoding.ASCII.GetBytes("###ICON_PLACEHOLDER###");
        byte[] sizeMarker = Encoding.ASCII.GetBytes("###ICON_SIZE###");
        int iconOffset = Unique(payload, iconMarker);
        int sizeOffset = Unique(payload, sizeMarker);

        int length = icon?.Length ?? 0;
        if (length > IconCapacity)
            throw new InvalidDataException("A capa do jogo excede 1 MiB.");

        if (iconOffset < 0 || sizeOffset < 0 || iconOffset + IconCapacity > payload.Length)
            throw new InvalidDataException("Template ELF 3.5.3 sem área válida para capa.");

        Array.Clear(payload, iconOffset, IconCapacity);
        if (length > 0)
            Array.Copy(icon, 0, payload, iconOffset, length);

        Array.Clear(payload, sizeOffset, 16);
        payload[sizeOffset] = (byte)length;
        payload[sizeOffset + 1] = (byte)(length >> 8);
        payload[sizeOffset + 2] = (byte)(length >> 16);
        payload[sizeOffset + 3] = (byte)(length >> 24);
    }

    static int Unique(byte[] data, byte[] needle)
    {
        int found = -1;
        for (int i = 0; i <= data.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && data[i + j] == needle[j]) j++;
            if (j != needle.Length) continue;
            if (found != -1) throw new InvalidDataException("Marcador de capa duplicado.");
            found = i;
        }
        return found;
    }
}
