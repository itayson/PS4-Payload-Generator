using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

if (args.Length != 4)
    throw new ArgumentException("usage: exact-cover-patcher <original.dll> <merged.dll> <output.dll> <refs>");

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(args[3]);
var read = new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false };

using var original = AssemblyDefinition.ReadAssembly(args[0], read);
using var merged = AssemblyDefinition.ReadAssembly(args[1], read);
merged.MainModule.Mvid = original.MainModule.Mvid;

var main = merged.MainModule.GetType("PS4RemotePkgMobile.MainPage")
    ?? throw new InvalidOperationException("MainPage not found");
var overlay = merged.MainModule.GetType("Tayson.Exact343Cover.ExactCoverOverlay")
    ?? throw new InvalidOperationException("ExactCoverOverlay not found");
var target = overlay.Methods.Single(m => m.Name == "GenerateAsync" && m.IsStatic);
var method = main.Methods.Single(m => m.Name == "GeneratePatchedElfAsync");

method.Body = new MethodBody(method);
var il = method.Body.GetILProcessor();
il.Emit(OpCodes.Ldarg_0);
il.Emit(OpCodes.Call, target);
il.Emit(OpCodes.Ret);

merged.Write(args[2]);

using var check = AssemblyDefinition.ReadAssembly(args[2], read);
if (check.MainModule.Mvid != original.MainModule.Mvid)
    throw new InvalidOperationException("MVID changed");

var before = original.MainModule.GetTypes().ToDictionary(t => t.FullName, t => t.MetadataToken.ToUInt32());
foreach (string type in new[] {
    "PS4RemotePkgMobile.MainActivity",
    "PS4RemotePkgMobile.MainApplication",
    "PS4RemotePkgMobile.Resource"
})
{
    var t = check.MainModule.GetType(type) ?? throw new InvalidOperationException(type + " missing");
    if (!before.TryGetValue(type, out uint token) || t.MetadataToken.ToUInt32() != token)
        throw new InvalidOperationException("JNI TypeDef token changed: " + type);
}

Console.WriteLine(JsonSerializer.Serialize(new {
    exact343Overlay = true,
    mvidPreserved = true,
    jniTokensPreserved = true
}));
