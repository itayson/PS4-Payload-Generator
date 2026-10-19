using System;
using Tayson.Providers;
using PS4RemotePkgMobile;
int count=0;
void Check(bool valid, string what)
{
    if(!valid) throw new Exception("FAIL "+what);
    count++; Console.WriteLine("PASS "+what);
}
UiStateStore.ClearHistory();
var meta = new PkgMetadata { ContentId="UP0000-CUSA09254_00-PS4TEST00", TitleId="CUSA09254", DisplayName="Just Cause 4", PkgType="PS4PATCH", Url="https://download.akirabox.invalid/a.pkg?secret=PRIVATE_TOKEN", ContentLength=20L*1024*1024*1024 };
UiStateStore.Record(meta, "VALIDADO", "192.168.0.5");
string key=UiStateStore.ReadHistory()[0].Key;
Check(!UiStateStore.ConfirmDownloadingManually(key), "cannot claim download from validation alone");
UiStateStore.Record(meta,"PAYLOAD ENVIADO","192.168.0.5");
Check(UiStateStore.ConfirmDownloadingManually(key), "user can confirm observed download after TCP send");
Check(UiStateStore.ReadHistory()[0].Status=="BAIXANDO (MANUAL)", "manual provenance persisted");
Check(!UiStateStore.ConfirmDownloadingManually(key), "manual in-progress confirmation idempotent");
Check(UiStateStore.ConfirmCompletedManually(key), "user can attest completed download and launch");
Check(UiStateStore.ReadHistory()[0].Status=="CONCLUÍDO (MANUAL)", "completion remains manual");
Check(!UiStateStore.ConfirmDownloadingManually(key), "cannot downgrade manual completion");
Check(UiStateStore.ReadHistory().Count==1, "history de-duplicated by content ID");
Check(UiStateStore.ReadHistory()[0].Provider=="AkiraBox", "provider preserved");
Check(!UiStateStore.ReadHistory()[0].ToString().Contains("PRIVATE_TOKEN"),"sensitive token excluded in object representation");
Check(UiStateStore.LoadWorkerUrl()=="https://seek.itayson.workers.dev", "worker address unchanged");
Console.WriteLine("RESULT "+count+" manual BGFT history checks passed");
