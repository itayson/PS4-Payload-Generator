using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace Tayson.Providers;

/// <summary>
/// Compatibility implementation for the trimmed Xamarin runtime embedded in
/// the original Payload Generator V3, which does not expose the framework
/// FormUrlEncodedContent type even though HttpClient/StringContent are present.
/// </summary>
public sealed class FormUrlEncodedContent : StringContent
{
    public FormUrlEncodedContent(IEnumerable<KeyValuePair<string, string>> fields)
        : base(Build(fields), Encoding.UTF8, "application/x-www-form-urlencoded") { }

    static string Build(IEnumerable<KeyValuePair<string, string>> fields)
    {
        if (fields == null) throw new ArgumentNullException(nameof(fields));
        var sb = new StringBuilder();
        bool first = true;
        foreach (var item in fields)
        {
            if (!first) sb.Append('&');
            first = false;
            sb.Append(Encode(item.Key));
            sb.Append('=');
            sb.Append(Encode(item.Value ?? ""));
        }
        return sb.ToString();
    }

    static string Encode(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
        var sb = new StringBuilder(bytes.Length * 3);
        const string Hex = "0123456789ABCDEF";
        foreach (byte b in bytes)
        {
            bool unreserved =
                (b >= (byte)'A' && b <= (byte)'Z') ||
                (b >= (byte)'a' && b <= (byte)'z') ||
                (b >= (byte)'0' && b <= (byte)'9') ||
                b == (byte)'-' || b == (byte)'.' ||
                b == (byte)'_' || b == (byte)'~';
            if (unreserved)
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('%');
                sb.Append(Hex[b >> 4]);
                sb.Append(Hex[b & 15]);
            }
        }
        return sb.ToString();
    }
}
