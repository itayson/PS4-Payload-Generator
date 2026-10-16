// Compiles the production UiStateStore.cs without .NET MAUI workload.
// All Android Preferences and metadata dependencies below are in-memory fakes.
using System;
using System.Collections.Generic;
using Tayson.Providers;
using Microsoft.Maui.Storage;
using PS4RemotePkgMobile;

namespace UiStateRegression
{
    internal static class Program
    {
        static int passed;
        static int failed;

        static void Check(string title, Action action)
        {
            Preferences.Default.Clear();
            try
            {
                action();
                passed++;
                Console.WriteLine("PASS " + title);
            }
            catch (Exception e)
            {
                failed++;
                Console.Error.WriteLine("FAIL " + title + ": " + e.Message);
            }
        }

        static void Expect(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static PkgMetadata Pkg(string contentId = "", string titleId = "",
            string name = "Teste", string url = "https://host.invalid/file.pkg",
            string kind = "PS4GD", long bytes = 1073741824)
            => new PkgMetadata
            {
                ContentId = contentId, TitleId = titleId, DisplayName = name,
                Url = url, PkgType = kind, ContentLength = bytes
            };

        public static int Main()
        {
            Check("empty history", () =>
                Expect(UiStateStore.ReadHistory().Count == 0, "new install must be empty"));

            Check("blank content id falls back to title id", () =>
            {
                UiStateStore.Record(Pkg("  ", "  CUSA00042  "), "VALIDADO", "192.168.1.2");
                Expect(UiStateStore.ReadHistory()[0].Key == "CUSA00042", "TitleId fallback");
            });

            Check("blank ids fall back to display name", () =>
            {
                UiStateStore.Record(Pkg(" ", "\t", "  Pacote teste  "), "VALIDADO", "");
                Expect(UiStateStore.ReadHistory()[0].Key == "Pacote teste", "DisplayName fallback");
            });

            Check("content id has priority", () =>
            {
                UiStateStore.Record(Pkg("  UP00001 ", "CUSA00001", "Outro"), "VALIDADO", "");
                Expect(UiStateStore.ReadHistory()[0].Key == "UP00001", "ContentId priority");
            });

            Check("all metadata absent uses compatible fallback", () =>
            {
                UiStateStore.Record(Pkg(" ", "", ""), "VALIDADO", "");
                Expect(UiStateStore.ReadHistory()[0].Key == "pkg", "fallback key changed");
            });

            Check("duplicate key replaces older record", () =>
            {
                UiStateStore.Record(Pkg("UP001", name: "Antigo"), "VALIDADO", "");
                UiStateStore.Record(Pkg("UP001", name: "Atualizado"), "ELF GERADO", "");
                var items = UiStateStore.ReadHistory();
                Expect(items.Count == 1, "duplicate history");
                Expect(items[0].Title == "Atualizado" && items[0].Status == "ELF GERADO",
                    "newest status must win");
            });

            Check("history bounded to 20 and newest first", () =>
            {
                for (int i = 0; i < 25; i++)
                    UiStateStore.Record(Pkg("K" + i), "VALIDADO", "");
                var items = UiStateStore.ReadHistory();
                Expect(items.Count == 20, "unexpected number of items");
                Expect(items[0].Key == "K24" && items[19].Key == "K5",
                    "newest-first retention order");
            });

            Check("unicode and delimiters roundtrip", () =>
            {
                string title = "Jogo ação | versão 1\n edição";
                UiStateStore.Record(Pkg("TXT", name: title), "VALIDADO", "192.168.1.7");
                var item = UiStateStore.ReadHistory()[0];
                Expect(item.Title == title && item.Ps4Ip == "192.168.1.7", "history codec");
                Expect(Preferences.Default.Get("tayson.ui.history.v2", "").Split('|').Length == 8,
                    "persisted record format must remain v2/8 fields");
            });

            Check("provider URL tokens never written to history", () =>
            {
                UiStateStore.Record(
                    Pkg("TOKEN", url: "https://filekeeper.net/file.pkg?token=never-store-me"),
                    "VALIDADO", "");
                string stored = Preferences.Default.Get("tayson.ui.history.v2", "");
                Expect(!stored.Contains("never-store-me", StringComparison.Ordinal),
                    "signed token stored");
                Expect(UiStateStore.ReadHistory()[0].Provider == "FileKeeper",
                    "provider compatibility changed");
            });

            Check("corrupt saved line skipped", () =>
            {
                UiStateStore.Record(Pkg("VALID"), "VALIDADO", "");
                string valid = Preferences.Default.Get("tayson.ui.history.v2", "");
                Preferences.Default.Set("tayson.ui.history.v2", "corrupted-record\n" + valid);
                var items = UiStateStore.ReadHistory();
                Expect(items.Count == 1 && items[0].Key == "VALID", "bad line handling");
            });

            Check("history clearing does not erase preferences", () =>
            {
                UiStateStore.SavePs4Ip(" 192.168.0.30 ");
                UiStateStore.SaveWorkerUrl("https://test.invalid");
                UiStateStore.Record(Pkg("CLEAR"), "VALIDADO", "");
                UiStateStore.ClearHistory();
                Expect(UiStateStore.ReadHistory().Count == 0, "clear failed");
                Expect(UiStateStore.LoadPs4Ip() == "192.168.0.30", "PS4 IP lost");
                Expect(UiStateStore.LoadWorkerUrl() == "https://test.invalid", "Worker URL lost");
            });

            Check("pkg types are preserved", () =>
            {
                UiStateStore.Record(Pkg("PATCH",kind: "PS4PATCH"), "VALIDADO", "");
                UiStateStore.Record(Pkg("DLC",kind: "PS4AC"), "VALIDADO", "");
                var items = UiStateStore.ReadHistory();
                Expect(items[0].Kind == "DLC" && items[1].Kind == "Atualização",
                    "package type mapping changed");
            });

            Console.WriteLine($"RESULT: {passed} checks passed; {failed} failed");
            return failed == 0 ? 0 : 1;
        }
    }
}

namespace Microsoft.Maui.Storage
{
    public static class Preferences
    {
        public static InMemoryPreferences Default { get; } = new InMemoryPreferences();
    }

    public sealed class InMemoryPreferences
    {
        readonly Dictionary<string, string> values = new();
        public string Get(string key, string fallback)
            => values.TryGetValue(key, out string? value) ? value : fallback;
        public void Set(string key, string value) => values[key] = value;
        public void Clear() => values.Clear();
    }
}

namespace PS4RemotePkgMobile
{
    public static class BgftRoutePlanner
    {
        public const string DefaultWorkerBase = "https://worker.example.invalid";
    }

    public sealed class PkgMetadata
    {
        public string ContentId { get; set; } = "";
        public string TitleId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Url { get; set; } = "";
        public string PkgType { get; set; } = "";
        public long ContentLength { get; set; }
    }
}
