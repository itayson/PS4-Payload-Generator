using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Tayson.Providers;

namespace Tayson.Providers
{
    // Test-only fixture; production uses the UiHistoryItem declared in UiStateStore.cs.
    public sealed class UiHistoryItem
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleId { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Status { get; set; } = "";
        public string Ps4Ip { get; set; } = "";
        public string SizeText { get; set; } = "";
    }
}

static class Tests
{
    static int passed;
    static void Check(bool yes, string name)
    {
        if (!yes) throw new Exception("FAIL: " + name);
        passed++;
        Console.WriteLine("PASS " + name);
    }
    static void Reject(Action a, string name)
    {
        try { a(); }
        catch (InvalidDataException) { Check(true, name); return; }
        throw new Exception("Unexpected acceptance: " + name);
    }
    static UiHistoryItem Item(string key, string status="VALIDADO") => new()
    {
        Key=key, Title="Jogo de Teste "+key, TitleId="CUSA12345",
        Provider="FileKeeper", Kind="Jogo", Status=status,
        Ps4Ip="192.168.0.7", SizeText="1.2 GB"
    };
    static void Main()
    {
        var original=new List<UiHistoryItem> { Item("UP0000-CUSA12345_00-DUMMYGAME0000001","CONCLUÍDO (MANUAL)"), Item("key2","VALIDADO") };
        string backup=UiHistoryBackupCodec.Export(original);
        Check(backup.StartsWith("TAYSON-HISTORY-BACKUP-V1\n"),"format version marker");
        var restored=UiHistoryBackupCodec.Import(backup);
        Check(restored.Count==2,"roundtrip count");
        Check(restored[0].Status=="CONCLUÍDO (MANUAL)","manual completion kept");
        Check(restored[0].Ps4Ip=="192.168.0.7","IP metadata roundtrip");
        Check(!backup.Contains("192.168.0.7"),"plaintext IP not exposed in encoded backup");
        var merged=UiHistoryBackupCodec.Merge(new[]{Item("key2","ELF GERADO")},restored);
        Check(merged.Count==2,"merge deduplicated");
        Check(merged[0].Status=="ELF GERADO","local status wins duplicate");
        Check(merged[1].Status=="CONCLUÍDO (MANUAL)","other imported status preserved");
        Check(UiHistoryBackupCodec.Import(UiHistoryBackupCodec.Export(new List<UiHistoryItem>())).Count==0,"empty backup roundtrip");
        Reject(()=>UiHistoryBackupCodec.Import(backup.Replace("CUSA","CUSZ")), "damaged payload rejected");
        Reject(()=>UiHistoryBackupCodec.Import(backup.Substring(0,backup.Length-3)), "truncated checksum rejected");
        Reject(()=>UiHistoryBackupCodec.Import("TAYSON-HISTORY-BACKUP-V999\n0\nFNV32:00000000"),"unknown version rejected");
        Reject(()=>UiHistoryBackupCodec.Import(new string('x', UiHistoryBackupCodec.MaxBackupChars+1)),"oversized backup rejected");
        Reject(()=>UiHistoryBackupCodec.Export(new[]{Item("key"),Item("key")}),"duplicate keys rejected");
        Reject(()=>UiHistoryBackupCodec.Export(Enumerable.Range(0,21).Select(x=>Item("key"+x)).ToArray()),"21 entries rejected");
        var url=Item("safe");url.Title="https://example.test/secret?sig=abc";
        Reject(()=>UiHistoryBackupCodec.Export(new[]{url}),"signed URL in title blocked");
        url=Item("safe");url.Provider="Bearer abc";
        Reject(()=>UiHistoryBackupCodec.Export(new[]{url}),"token-bearing field blocked");
        url=Item("safe");url.Title="abc\nline";
        Reject(()=>UiHistoryBackupCodec.Export(new[]{url}),"newlines rejected");
        url=Item("safe");url.Title=new string('x',170);
        Reject(()=>UiHistoryBackupCodec.Export(new[]{url}),"field overflow rejected");
        var twenty=Enumerable.Range(0,20).Select(x=>Item("key"+x)).ToArray();
        Check(UiHistoryBackupCodec.Import(UiHistoryBackupCodec.Export(twenty)).Count==20,"max-20 backup roundtrip");
        Check(UiHistoryBackupCodec.Merge(new[]{Item("now")},twenty).Count==20,"merge respects 20 cap");
        Check(UiHistoryBackupCodec.Merge(new[]{Item("now")},twenty)[0].Key=="now","existing newest retained");
        Console.WriteLine("RESULT: "+passed+" history-backup checks passed");
    }
}
