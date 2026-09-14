using System;
using System.Text.RegularExpressions;

namespace Tayson.Providers;

public static class ProviderResolvers
{
    static readonly Regex RootzShort = new Regex(@"\A/d/([A-Za-z0-9_-]{1,128})(?:/)?\z", RegexOptions.IgnoreCase);

    public static bool IsAkiraBox(Uri uri)
    {
        if (uri == null) return false;
        string host = uri.IdnHost.TrimEnd('.');
        return LinkPolicy.Domain(host, "akirabox.com") || LinkPolicy.Domain(host, "akirabox.to");
    }

    public static bool IsRootz(Uri uri)
    {
        if (uri == null) return false;
        return LinkPolicy.Domain(uri.IdnHost.TrimEnd('.'), "rootz.so");
    }

    public static string RootzProxyUrl(Uri uri)
    {
        if (!IsRootz(uri)) return null;
        Match match = RootzShort.Match(uri.AbsolutePath);
        if (!match.Success) return null;
        // The regex already limits the short id to URL-safe ASCII.
        return "https://www.rootz.so/api/files/proxy-download/" + match.Groups[1].Value;
    }

    public static bool IsAkiraSignedDownload(Uri uri)
    {
        if (!IsAkiraBox(uri)) return false;
        if (!uri.AbsolutePath.StartsWith("/download/", StringComparison.OrdinalIgnoreCase)) return false;
        return uri.AbsolutePath.IndexOf(".pkg", StringComparison.OrdinalIgnoreCase) >= 0 ||
            uri.Query.IndexOf("signature=", StringComparison.OrdinalIgnoreCase) >= 0 ||
            uri.Query.IndexOf("expiration=", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
