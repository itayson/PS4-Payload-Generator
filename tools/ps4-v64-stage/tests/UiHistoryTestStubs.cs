using System;
using System.Collections.Generic;
namespace Microsoft.Maui.Storage
{
    public sealed class MemoryPreferences
    {
        readonly Dictionary<string, string> items = new();
        public string Get(string key, string fallback) => items.TryGetValue(key, out string v) ? v : fallback;
        public void Set(string key, string value) => items[key] = value;
    }
    public static class Preferences { public static readonly MemoryPreferences Default = new(); }
}
namespace PS4RemotePkgMobile
{
    public sealed class PkgMetadata
    {
        public string ContentId, TitleId, DisplayName, Url, PkgType;
        public long ContentLength;
    }
}
namespace Tayson.Providers
{
    public static class BgftRoutePlanner
    {
        public const string DefaultWorkerBase = "https://seek.itayson.workers.dev";
    }
}
