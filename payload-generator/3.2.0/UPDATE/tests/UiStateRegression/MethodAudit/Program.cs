// Inspecao exclusivamente de leitura dos metodos do aplicativo Tayson 3.4.3.
// Sem modificacao, execucao de codigo do APK, gravacao de DLL ou assinatura.
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Linq;

if (args.Length != 1)
{
    Console.Error.WriteLine("Uso: MethodAudit /caminho/PS4RemotePkgMobile.dll");
    return 2;
}
try
{
    using var source = AssemblyDefinition.ReadAssembly(args[0]);
    var module = source.MainModule;
    Console.WriteLine("BASE: " + source.Name.FullName);
    Console.WriteLine("MVID: " + module.Mvid);
    Console.WriteLine("TYPES: " + module.GetTypes().Count());
    Console.WriteLine("RESOURCES: " + module.Resources.Count);

    MethodDefinition? Pick(string typeName, string methodName, int parameters)
    {
        var type = module.GetType(typeName);
        if (type is null)
        {
            Console.Error.WriteLine("STOP: tipo ausente " + typeName);
            return null;
        }
        var match = type.Methods.Where(m => m.Name == methodName
            && m.Parameters.Count == parameters).ToArray();
        if (match.Length != 1 || !match[0].HasBody)
        {
            Console.Error.WriteLine("STOP: assinatura ambigua/ausente " + typeName + "::" + methodName);
            foreach (var m in match) Console.Error.WriteLine("  " + m.FullName);
            return null;
        }
        return match[0];
    }

    void Header(MethodDefinition m)
    {
        Console.WriteLine("METHOD: " + m.FullName);
        Console.WriteLine("  TOKEN: " + m.MetadataToken);
        Console.WriteLine("  RVA: 0x" + m.RVA.ToString("X"));
        Console.WriteLine("  MAXSTACK: " + m.Body.MaxStackSize);
        Console.WriteLine("  INSTRUCTIONS: " + m.Body.Instructions.Count);
        Console.WriteLine("  LOCALS: " + m.Body.Variables.Count);
    }

    static string Operand(Instruction i) => i.Operand switch
    {
        MethodReference m => m.FullName,
        FieldReference f => f.FullName,
        TypeReference t => t.FullName,
        Instruction jump => "IL_" + jump.Offset.ToString("X4"),
        Instruction[] labels => string.Join(",", labels.Select(l => "IL_" + l.Offset.ToString("X4"))),
        null => "",
        _ => i.Operand.ToString() ?? ""
    };
    void Show(Instruction i) =>
        Console.WriteLine("  IL_" + i.Offset.ToString("X4") + ": " + i.OpCode + " " + Operand(i));

    var key = Pick("Tayson.Providers.UiStateStore", "PackageKey", 1);
    var history = Pick("Tayson.Providers.UiHistoryScreen", "Build", 2);
    if (key is null || history is null) return 3;
    Header(key);
    Console.WriteLine("PACKAGE_KEY_IL_BEGIN");
    foreach (var ins in key.Body.Instructions) Show(ins);
    Console.WriteLine("PACKAGE_KEY_IL_END");

    Header(history);
    Console.WriteLine("HISTORY_COUNT_CANDIDATES_BEGIN");
    var inst = history.Body.Instructions.ToArray();
    bool IsImportant(Instruction i)
    {
        var operand = Operand(i);
        return (i.OpCode == OpCodes.Ldc_I4_S && i.Operand is sbyte b && b == 10)
            || i.OpCode == OpCodes.Ldc_I4 && i.Operand is int x && x == 10
            || (i.Operand is MethodReference m && (m.Name == "get_Count" || m.Name == "HistoryCard"))
            || operand.Contains("get_Item", StringComparison.Ordinal);
    }
    var indices = Enumerable.Range(0, inst.Length).Where(i => IsImportant(inst[i])).ToArray();
    var printed = new System.Collections.Generic.HashSet<int>();
    foreach (int index in indices)
    {
        Console.WriteLine("  ** candidato IL_" + inst[index].Offset.ToString("X4")
            + " " + inst[index].OpCode + " " + Operand(inst[index]));
        for (int j = Math.Max(0, index - 5); j <= Math.Min(inst.Length - 1, index + 7); j++)
        {
            if (printed.Add(j)) Show(inst[j]);
        }
    }
    Console.WriteLine("HISTORY_COUNT_CANDIDATES_END");
    Console.WriteLine("STATUS: AUDITORIA SOMENTE LEITURA; NENHUM APK/DLL ALTERADO.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("ERRO: " + ex.GetType().Name + ": " + ex.Message);
    return 4;
}
