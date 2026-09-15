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
        // application/x-www-form-urlencoded accepts percent encoding; spaces as
        // %20 are valid and avoid relying on HttpUtility in the Android runtime.
        return Uri.EscapeDataString(value ?? "");
    }
}
