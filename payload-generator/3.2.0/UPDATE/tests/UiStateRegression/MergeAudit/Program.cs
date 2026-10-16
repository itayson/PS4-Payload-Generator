using Mono.Cecil;

// Auditoria somente leitura de metadados CIL. Nao executa codigo do APK nem grava DLL.
if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Uso: MergeAudit ORIGINAL.dll CANDIDATE.dll [MERGED.dll]");
    return 2;
}

try
{
    using var original = AssemblyDefinition.ReadAssembly(args[0]);
    using var candidate = AssemblyDefinition.ReadAssembly(args[1]);
    var originalNames = new HashSet<string>(original.MainModule.GetTypes()
        .Where(t => t.Name != "<Module>").Select(t => t.FullName), StringComparer.Ordinal);
    var candidateNames = candidate.MainModule.GetTypes()
        .Where(t => t.Name != "<Module>").Select(t => t.FullName)
        .Distinct(StringComparer.Ordinal).ToArray();
    var collisions = candidateNames.Where(originalNames.Contains)
        .OrderBy(t => t, StringComparer.Ordinal).ToArray();
    Console.WriteLine($"BASE_ORIGINAL: {original.Name.Name}; types={originalNames.Count}; MVID={original.MainModule.Mvid}");
    Console.WriteLine($"CANDIDATA: {candidate.Name.Name}; types={candidateNames.Length}; MVID={candidate.MainModule.Mvid}");
    Console.WriteLine($"COLISOES_TIPOS: {collisions.Length}");
    foreach (string name in collisions.Take(50)) Console.WriteLine("  CONFLITO: " + name);
    if (collisions.Length > 50) Console.WriteLine($"  ... e {collisions.Length - 50} adicionais");

    var targets = new [] { "Tayson.Providers.UiHistoryScreen", "Tayson.Providers.UiStateStore" };
    foreach (string target in targets)
        Console.WriteLine($"ALVO {target}: original={(originalNames.Contains(target) ? "presente" : "ausente")}, compilado={(candidateNames.Contains(target, StringComparer.Ordinal) ? "presente" : "ausente")}");

    // Se os mesmos tipos ja existem na DLL original, fundir assemblies inteiros
    // nao e uma operacao segura. Exigira patch pontual de metodos com Cecil.
    if (collisions.Length != 0)
    {
        Console.Error.WriteLine("STOP_DUPLICATE_TYPES: merge integral NAO AUTORIZADO pelo auditor.");
        Console.Error.WriteLine("Proxima fase: analisar metodos concretos no original para patch minimo.");
        return 10;
    }

    if (args.Length == 2)
    {
        Console.WriteLine("PRE_MERGE_OK: nenhum nome de tipo duplicado no escopo observado.");
        return 0;
    }

    using var merged = AssemblyDefinition.ReadAssembly(args[2]);
    var newNames = merged.MainModule.GetTypes().Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);
    var missing = originalNames.Where(t => !newNames.Contains(t)).ToArray();
    Console.WriteLine($"MERGED: {merged.Name.Name}; types={newNames.Count}; MVID={merged.MainModule.Mvid}");
    Console.WriteLine($"TIPOS_ORIGINAIS_AUSENTES: {missing.Length}");
    if (merged.Name.Name != original.Name.Name || missing.Length != 0)
    {
        Console.Error.WriteLine("STOP_METADATA: identidade ou tipos originais divergiram.");
        return 11;
    }
    // Esta verificacao e preliminar. MVID, JNI e recursos exigem gates separados.
    Console.WriteLine("METADATA_PRELIMINAR_OK: nao e aprovacao para instalacao.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("ERRO_MONO_CECIL: " + ex.GetType().Name + ": " + ex.Message);
    return 12;
}
