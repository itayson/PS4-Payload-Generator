using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

if (args.Length < 3) throw new ArgumentException("audit ASSEMBLY REFS | patch ORIGINAL MERGED OUT REFS");
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(args[^1]);
var read = new ReaderParameters { AssemblyResolver = resolver };

if (args[0] == "audit")
{
    using var dll = AssemblyDefinition.ReadAssembly(args[1], read);
    var failures = new SortedSet<string>();
    foreach (var type in dll.MainModule.GetTypes())
    foreach (var method in type.Methods.Where(m => m.HasBody))
    foreach (var i in method.Body.Instructions)
    {
        try
        {
            if (i.Operand is MethodReference mr)
            {
                var target = mr.Resolve();
                if (target == null) failures.Add("Missing: " + mr.FullName);
                else if (target.HasBody && target.Body.Instructions.Any(x => x.OpCode == OpCodes.Ldstr && (string)x.Operand == "Linked away"))
                    failures.Add("Linked away: " + mr.FullName);
            }
            else if (i.Operand is FieldReference fr && fr.Resolve() == null) failures.Add("Missing field: " + fr.FullName);
        }
        catch (Exception ex) { failures.Add("Resolve: " + i.Operand + " :: " + ex.Message); }
    }
    foreach (var f in failures) Console.WriteLine(f);
    Console.WriteLine($"Dependency audit: {failures.Count} missing/trimmed members");
    Environment.ExitCode = failures.Count == 0 ? 0 : 1;
    return;
}

using var original = AssemblyDefinition.ReadAssembly(args[1], read);
using var merged = AssemblyDefinition.ReadAssembly(args[2], read);
var module = merged.MainModule;
module.Mvid = original.MainModule.Mvid;
var main = module.GetType("PS4RemotePkgMobile.MainPage");
MethodDefinition Target(string type, string name) => module.GetType(type).Methods.Single(m => m.Name == name);
void Replace(string source, string type, string name, int[] parameters)
{
    var method = main.Methods.Single(m => m.Name == source);
    method.Body = new MethodBody(method);
    var il = method.Body.GetILProcessor();
    foreach (var p in parameters)
    {
        if (p == 0) il.Emit(OpCodes.Ldarg_0);
        else il.Emit(OpCodes.Ldarg, method.Parameters[p - 1]);
    }
    il.Emit(OpCodes.Call, Target(type, name)); il.Emit(OpCodes.Ret);
}
Replace("TryGetDirectLink", "Tayson.Providers.Integration", "ResolveAsync", new[] { 0, 1 });
Replace("AnalyzeUrlAsync", "Tayson.Providers.Integration", "AnalyzeAsync", new[] { 0, 1, 2 });
Replace("GeneratePatchedElfAsync", "Tayson.Providers.Integration", "GenerateAsync", new[] { 0 });
Replace("PatchElf", "Tayson.Providers.PayloadPatcher", "Patch", new[] { 1, 2, 3, 4, 5, 6 });
Replace("IsUnsupportedUrl", "Tayson.Providers.LinkPolicy", "Unsupported", new[] { 1 });
// These properties serve every locale; report socket delivery honestly.
void SetMessage(string property, string text)
{
    var method = Target("PS4RemotePkgMobile.Idiomas.AppStrings", "get_" + property);
    method.Body = new MethodBody(method);
    var il = method.Body.GetILProcessor();
    il.Emit(OpCodes.Ldstr, text);
    il.Emit(OpCodes.Ret);
}
SetMessage("AlertInjected", "Payload enviado ao PS4. Isso confirma apenas o envio. Confira na TV se o download iniciou; se aparecer Registro: 0x..., fotografe o código completo. Versão Tayson 3.2.0.");
SetMessage("StatusSentToPS4", "Payload enviado; confirme o início do download na TV.");
int titleChanges = 0;
foreach (var type in module.GetTypes())
foreach (var method in type.Methods.Where(m => m.HasBody))
foreach (var instruction in method.Body.Instructions)
    if (instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == "PS4 Payload Generator")
    {
        instruction.Operand = "PS4 Payload Generator · Tayson 3.2.0";
        titleChanges++;
    }
if (titleChanges == 0) throw new InvalidOperationException("Application title was not located");
var ctor = main.Methods.Single(m => m.IsConstructor && !m.IsStatic);
foreach (var ret in ctor.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
{
    var il = ctor.Body.GetILProcessor();
    il.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_0));
    il.InsertBefore(ret, Instruction.Create(OpCodes.Call, Target("Tayson.Providers.Integration", "Initialize")));
}
var scan = main.Methods.Single(m => m.Name == "BtnScan_Clicked");
var scanIL = scan.Body.GetILProcessor();
var first = scan.Body.Instructions[0];
scanIL.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
scanIL.InsertBefore(first, Instruction.Create(OpCodes.Call, Target("Tayson.Providers.Integration", "Invalidate")));
merged.Write(args[3]);
using var check = AssemblyDefinition.ReadAssembly(args[3], read);
var tokens = original.MainModule.GetTypes().ToDictionary(t => t.FullName, t => t.MetadataToken.ToUInt32());
var changes = check.MainModule.GetTypes().Where(t => tokens.ContainsKey(t.FullName) && tokens[t.FullName] != t.MetadataToken.ToUInt32())
    .Select(t => new { name = t.FullName, before = tokens[t.FullName], after = t.MetadataToken.ToUInt32() }).ToArray();
// JNI typemaps in libxamarin-app.so point at these exact TypeDef tokens.
foreach (string type in new[] { "PS4RemotePkgMobile.MainActivity", "PS4RemotePkgMobile.MainApplication", "PS4RemotePkgMobile.Resource" })
    if (check.MainModule.GetType(type).MetadataToken.ToUInt32() != tokens[type])
        throw new InvalidOperationException("JNI TypeDef token changed: " + type);
if (check.MainModule.Mvid != original.MainModule.Mvid) throw new Exception("MVID changed");
Console.WriteLine(JsonSerializer.Serialize(new { patched = true, mvid = check.MainModule.Mvid, originalTypes = tokens.Count,
    changedTypeTokens = changes, jniTypesPreserved = true, originalResources = original.MainModule.Resources.Count,
    finalResources = check.MainModule.Resources.Count }));
