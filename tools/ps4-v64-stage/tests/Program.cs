using System;
using System.IO;
using System.Net.Sockets;
using Tayson.Providers;

int count = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception("FAIL " + message);
    count++;
    Console.WriteLine("PASS " + message);
}
Check(UiStageTracker.Current("") == UiStageTracker.Stage.AwaitingLink, "empty link never ready");
Check(UiStageTracker.Title(UiStageTracker.Current("")) == "○ Aguardando link", "empty link text");
UiStageTracker.LinkChanged("https://example.invalid/a.pkg");
Check(UiStageTracker.Current("https://example.invalid/a.pkg") == UiStageTracker.Stage.LinkReceived, "new link awaiting validation");
UiStageTracker.MarkSent("https://example.invalid/a.pkg");
Check(UiStageTracker.Current("https://example.invalid/a.pkg") == UiStageTracker.Stage.LinkReceived, "cannot show sent without validation");
UiStageTracker.BeginValidation("https://example.invalid/a.pkg");
Check(UiStageTracker.Current("https://example.invalid/a.pkg") == UiStageTracker.Stage.Validating, "explicit validation attempt");
UiStageTracker.MarkValidated("https://example.invalid/a.pkg");
UiStageTracker.MarkGenerated("https://example.invalid/a.pkg");
Check(UiStageTracker.Current("https://example.invalid/a.pkg") == UiStageTracker.Stage.ElfGenerated, "ELF generated for validated link");
UiStageTracker.MarkSending("https://example.invalid/a.pkg");
UiStageTracker.MarkSent("https://example.invalid/a.pkg");
Check(UiStageTracker.Current("https://example.invalid/a.pkg") == UiStageTracker.Stage.ElfSent, "TCP send reflects local delivery only");
Check(UiStageTracker.Detail(UiStageTracker.Stage.ElfSent).Contains("BGFT"), "sent detail does not assert BGFT completion");
UiStageTracker.LinkChanged("https://example.invalid/b.pkg");
Check(UiStageTracker.Current("https://example.invalid/b.pkg") == UiStageTracker.Stage.LinkReceived, "new link invalidates older ELF");
UiStageTracker.MarkSent("https://example.invalid/a.pkg");
Check(UiStageTracker.Current("https://example.invalid/b.pkg") == UiStageTracker.Stage.LinkReceived, "late async completion cannot advance new link");
UiStageTracker.LinkChanged("");
Check(UiStageTracker.Current("") == UiStageTracker.Stage.AwaitingLink, "clearing input clears visible state");
UiStageTracker.BeginValidation("https://example.invalid/b.pkg");
UiStageTracker.MarkFailed("https://example.invalid/b.pkg");
Check(UiStageTracker.Current("https://example.invalid/b.pkg") == UiStageTracker.Stage.Failed, "explicit failure visible");
UiStageTracker.LinkChanged("https://example.invalid/c.pkg");
Check(UiStageTracker.Current("https://example.invalid/c.pkg") == UiStageTracker.Stage.LinkReceived, "failure reset on next link");

Check(UiTransportDiagnostics.Describe(new TimeoutException(), true).Contains("PS4-9090-TIMEOUT"), "BinLoader timeout category");
Check(UiTransportDiagnostics.Describe(new SocketException((int)SocketError.ConnectionRefused), true).Contains("PS4-9090-RECUSADO"), "refused category");
Check(UiTransportDiagnostics.Describe(new SocketException((int)SocketError.NetworkUnreachable), true).Contains("PS4-REDE"), "LAN category");
Check(UiTransportDiagnostics.Describe(new IOException("https://signed.invalid/token?secret=test"), true).Contains("ENVIO-INCERTO"), "partial delivery never autoretied");
var sanitized = UiTransportDiagnostics.Describe(new IOException("https://signed.invalid/token?secret=test"), true);
Check(!sanitized.Contains("token") && !sanitized.Contains("secret"), "signed URLs never reflected in diagnostics");
Check(UiTransportDiagnostics.Describe(new System.Net.Http.HttpRequestException("secret"), false).Contains("WORKER-REDE"), "HTTP failure classified");
Check(!UiTransportDiagnostics.Describe(new System.Net.Http.HttpRequestException("signed URL secret"), false).Contains("secret"), "HTTP error sanitized");
Check(UiTransportDiagnostics.Describe(new FormatException("unsafe"), false).Contains("PKG-FORMATO"), "PKG format category");
Console.WriteLine("RESULT " + count + " evidence and diagnostic tests passed");
