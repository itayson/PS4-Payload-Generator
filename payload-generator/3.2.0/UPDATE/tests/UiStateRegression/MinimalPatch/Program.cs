// Laboratorio controlado Mono.Cecil - dois metodos do Tayson PS4 Payload Generator.
// Escreve SOMENTE uma DLL experimental em caminho separado. Nao cria APK.
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text;

const string ExpectedInputHash = "341c0b442be9be00a4561e08e5808ff02d8a41e6ca9e3aa7460329e2464d8f4a";
static void Check(bool condition, string why)
{
    if (!condition) throw new InvalidOperationException("STOP: " + why);
}
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static string Snapshot(MethodDefinition method)
{
    if (!method.HasBody) return "<no body>";
    var instructions = method.Body.Instructions.ToList();
    var parts = new List<string>();
    foreach (var i in instructions)
    {
        string operand = i.Operand switch
        {
            null => "",
            Instruction target => "->" + instructions.IndexOf(target),
            Instruction[] targets => "->" + string.Join(",", targets.Select(instructions.IndexOf)),
            MethodReference m => m.FullName,
            FieldReference f => f.FullName,
            TypeReference t => t.FullName,
            _ => i.Operand.ToString() ?? ""
        };
        parts.Add(i.OpCode.Code.ToString() + ":" + operand);
    }
    var handlers = method.Body.ExceptionHandlers.Select(h => h.HandlerType.ToString()).ToArray();
    return string.Join("|", parts) + ";EH=" + string.Join(",", handlers);
}
static bool CountGetter(Instruction i)
    => i.Operand is MethodReference m && m.Name == "get_Count"
        && m.ReturnType.FullName == "System.Int32";
static bool IsSbyte10(Instruction i)
    => i.OpCode == OpCodes.Ldc_I4_S && i.Operand is sbyte b && b == 10;
static MethodDefinition Pick(ModuleDefinition module, string fullType, string name, int count)
{
    var type = module.GetType(fullType);
    Check(type != null, "Tipo inexistente " + fullType);
    var methods = type!.Methods.Where(m => m.Name == name && m.Parameters.Count == count).ToArray();
    Check(methods.Length == 1 && methods[0].HasBody, "Metodo alvo ambiguo " + fullType + "." + name);
    return methods[0];
}
static Dictionary<string, uint> Metadata(ModuleDefinition module)
{
    var d = new Dictionary<string, uint>(StringComparer.Ordinal);
    foreach (var type in module.GetTypes())
    {
        d.Add("T:" + type.FullName, type.MetadataToken.ToUInt32());
        foreach (var f in type.Fields) d.Add("F:" + f.FullName, f.MetadataToken.ToUInt32());
        foreach (var m in type.Methods) d.Add("M:" + m.FullName, m.MetadataToken.ToUInt32());
    }
    return d;
}
static Dictionary<string, string> Resources(ModuleDefinition module)
{
    var d = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var resource in module.Resources)
    {
        string hash = resource switch
        {
            EmbeddedResource er => Hash(ReadResource(er)),
            _ => resource.ResourceType + ":" + resource.Name
        };
        d.Add(resource.Name, resource.Attributes + ":" + hash);
    }
    return d;
}
static byte[] ReadResource(EmbeddedResource resource)
{
    using var stream = resource.GetResourceStream();
    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return memory.ToArray();
}
static void Same<TKey, TValue>(Dictionary<TKey, TValue> before,
    Dictionary<TKey, TValue> after, string label) where TKey : notnull
{
    Check(before.Count == after.Count, label + " count mudou");
    foreach (var (key, value) in before)
        Check(after.TryGetValue(key, out var v) &&
            EqualityComparer<TValue>.Default.Equals(value, v),
            label + " modificado: " + key);
}
if (args.Length != 2)
{
    Console.Error.WriteLine("Uso: MinimalPatch BASE_ORIGINAL.dll RESULTADO.LAB-ONLY.dll");
    return 2;
}
var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (!File.Exists(input) || !output.EndsWith(".LAB-ONLY.dll", StringComparison.Ordinal))
{
    Console.Error.WriteLine("STOP: base ausente ou caminho de saida sem sufixo .LAB-ONLY.dll");
    return 2;
}
Check(input != output, "Arquivo de saida deve ser diferente do original.");
Check(!File.Exists(output), "Saida ja existe: nao sobrescrever candidato anterior.");

try
{
    var originalBytes = File.ReadAllBytes(input);
    Check(Hash(originalBytes) == ExpectedInputHash, "SHA256 da DLL de origem divergente.");
    using var assembly = AssemblyDefinition.ReadAssembly(input);
    var module = assembly.MainModule;
    Check(assembly.Name.Name == "PS4RemotePkgMobile", "Identidade do assembly incorreta.");
    var mvidBefore = module.Mvid;
    var metaBefore = Metadata(module);
    var resourceBefore = Resources(module);
    var refsBefore = module.AssemblyReferences.Select(r => r.FullName).ToArray();
    var ilBefore = module.GetTypes().SelectMany(t => t.Methods)
        .ToDictionary(m => m.FullName, Snapshot, StringComparer.Ordinal);

    // Assinatura exata confirmada no IL da versao 3.4.3.
    var key = Pick(module, "Tayson.Providers.UiStateStore", "PackageKey", 1);
    Check(key.ReturnType.FullName == "System.String", "PackageKey nao retorna String");
    var ins = key.Body.Instructions.ToList();
    Check(ins.Count == 17, "PackageKey: quantidade de instrucoes inesperada");
    Check(ins[0].OpCode == OpCodes.Ldarg_0 &&
          ins[5].OpCode == OpCodes.Ldarg_0 &&
          ins[10].OpCode == OpCodes.Ldarg_0, "PackageKey: carregamento de parametros divergente");
    Check(ins[2].OpCode == OpCodes.Dup && ins[7].OpCode == OpCodes.Dup &&
          ins[12].OpCode == OpCodes.Dup, "PackageKey: padrao null-coalesce ausente");
    Check(ins[3].OpCode == OpCodes.Brtrue_S && ins[8].OpCode == OpCodes.Brtrue_S &&
          ins[13].OpCode == OpCodes.Brtrue_S, "PackageKey: branches divergentes");
    Check(new[] { 3, 8, 13 }.All(i => ReferenceEquals(ins[i].Operand, ins[16])),
        "PackageKey: branches nao convergem no retorno esperado");
    Check(ins[15].OpCode == OpCodes.Ldstr && (string?)ins[15].Operand == "pkg" &&
          ins[16].OpCode == OpCodes.Ret, "PackageKey: fallback nao reconhecido");
    var names = new[] { "get_ContentId", "get_TitleId", "get_DisplayName" };
    var getters = new MethodReference[3];
    for (var i = 0; i < names.Length; i++)
    {
        var operand = ins[i * 5 + 1];
        Check(operand.OpCode == OpCodes.Callvirt &&
            operand.Operand is MethodReference mr &&
            mr.Name == names[i] &&
            mr.DeclaringType.FullName == "PS4RemotePkgMobile.PkgMetadata",
            "PackageKey: getter inesperado " + names[i]);
        getters[i] = (MethodReference)operand.Operand;
    }
    Check(key.Body.ExceptionHandlers.Count == 0 && key.Body.Variables.Count == 0,
        "PackageKey: handlers/locals inesperados");

    var whitespace = module.ImportReference(typeof(string).GetMethod(
        nameof(string.IsNullOrWhiteSpace), new[] { typeof(string) })!);
    var trim = module.ImportReference(typeof(string).GetMethod(
        nameof(string.Trim), Type.EmptyTypes)!);
    Check(whitespace.ReturnType.FullName == "System.Boolean" &&
          trim.ReturnType.FullName == "System.String", "Import de metodos System.String divergente");
    var newBody = new Mono.Cecil.Cil.MethodBody(key) { MaxStackSize = 2 };
    var il = newBody.GetILProcessor();
    foreach (var getter in getters)
    {
        var next = Instruction.Create(OpCodes.Nop);
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Callvirt, getter));
        il.Append(Instruction.Create(OpCodes.Call, whitespace));
        il.Append(Instruction.Create(OpCodes.Brtrue, next));
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Callvirt, getter));
        il.Append(Instruction.Create(OpCodes.Callvirt, trim));
        il.Append(Instruction.Create(OpCodes.Ret));
        il.Append(next);
    }
    il.Append(Instruction.Create(OpCodes.Ldstr, "pkg"));
    il.Append(Instruction.Create(OpCodes.Ret));
    key.Body = newBody;

    // history.Count < 10 ? history.Count : 10. Mudar ambos limites para 20.
    // ReadHistory limita e ordena previamente ate 20 itens.
    var history = Pick(module, "Tayson.Providers.UiHistoryScreen", "Build", 2);
    var seq = history.Body.Instructions.ToList();
    var candidates = new List<int>();
    for (int i = 2; i < seq.Count - 5; i++)
    {
        if (seq[i].OpCode != OpCodes.Blt_S) continue;
        if (!CountGetter(seq[i - 2]) || !IsSbyte10(seq[i - 1]) ||
            !IsSbyte10(seq[i + 1]) || seq[i + 2].OpCode != OpCodes.Br_S ||
            !CountGetter(seq[i + 4])) continue;
        if (!ReferenceEquals(seq[i].Operand, seq[i + 3]) ||
            !ReferenceEquals(seq[i + 2].Operand, seq[i + 5])) continue;
        candidates.Add(i);
    }
    Check(candidates.Count == 1, "UiHistoryScreen.Build: padrao min(10, Count) nao encontrado de forma univoca");
    var at = candidates.Single();
    seq[at - 1].Operand = (sbyte)20;
    seq[at + 1].Operand = (sbyte)20;
    Console.WriteLine("PATCH: PackageKey (null/whitespace/Trim), maximo de historico 10 -> 20.");
    Console.WriteLine("IL history: duas constantes 10 -> 20, mantendo tamanho de instrucao.");

    // Nunca sobrescrever o original; falha de gates remove apenas a copia.
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    assembly.Write(output);
    try
    {
        using var verify = AssemblyDefinition.ReadAssembly(output);
        var after = verify.MainModule;
        Check(after.Mvid == mvidBefore, "MVID mudou");
        Check(verify.Name.FullName == assembly.Name.FullName, "Assembly name mudou");
        Same(metaBefore, Metadata(after), "metadados tokens");
        Same(resourceBefore, Resources(after), "recursos");
        Check(refsBefore.SequenceEqual(after.AssemblyReferences.Select(r => r.FullName)),
            "Referencias de assembly mudaram");
        var outMethods = after.GetTypes().SelectMany(t => t.Methods)
            .ToDictionary(m => m.FullName, Snapshot, StringComparer.Ordinal);
        Check(outMethods.Count == ilBefore.Count, "Quantidade de metodos mudou");
        foreach (var (name, sig) in ilBefore)
        {
            Check(outMethods.TryGetValue(name, out var afterSig), "Metodo ausente " + name);
            if (name == key.FullName || name == history.FullName)
                Check(afterSig != sig, "Metodo alvo nao mudou " + name);
            else
                Check(afterSig == sig, "IL estranho mudou: " + name);
        }
        var resultKey = Pick(after, "Tayson.Providers.UiStateStore", "PackageKey", 1);
        var resultBuild = Pick(after, "Tayson.Providers.UiHistoryScreen", "Build", 2);
        var code = resultKey.Body.Instructions;
        Check(code.Count(i => i.Operand is MethodReference m &&
            m.Name == "IsNullOrWhiteSpace") == 3, "PackageKey perdeu validação de whitespace");
        Check(code.Count(i => i.Operand is MethodReference m &&
            m.Name == "Trim") == 3, "PackageKey perdeu Trim");
        Check(resultBuild.Body.Instructions.Count(i =>
            i.OpCode == OpCodes.Ldc_I4_S && i.Operand is sbyte b && b == 20) >= 2,
            "Build nao contem limite 20");
        Check(Hash(File.ReadAllBytes(input)) == ExpectedInputHash, "DLL original foi alterada");
        Console.WriteLine("PASS: MVID, AssemblyName, todos TypeDef/MethodDef/FieldDef tokens.");
        Console.WriteLine("PASS: recursos preservados, IL dos outros metodos preservado.");
        Console.WriteLine("PASS: DLL original inalterada: " + ExpectedInputHash);
        Console.WriteLine("LAB_DLL_SHA256: " + Hash(File.ReadAllBytes(output)));
        Console.WriteLine("LAB_ONLY: esta DLL nao foi integrada ao APK; nao instalar.");
        return 0;
    }
    catch
    {
        File.Delete(output);
        throw;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("PATCH NAO APROVADO: " + ex.Message);
    return 10;
}
