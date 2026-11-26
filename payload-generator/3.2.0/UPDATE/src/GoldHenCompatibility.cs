using System;

namespace Tayson.Providers;

public enum GoldHenLoaderEvidence
{
    Unknown,
    NoIntegratedLoader,
    LegacyNeedsVerification,
    ElfCandidate
}

// Advisory only: a release note is not proof that this particular ELF or BGFT works.
public static class GoldHenCompatibility
{
    public static bool ValidProfileField(string value)
    {
        if (value == null || value.Length > 32) return false;
        foreach (char c in value)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') ||
                  (c >= 'A' && c <= 'Z') || c == '.' || c == '-' || c == '+'))
                return false;
        return true; // Empty means not declared.
    }

    public static bool TryPort(string value, out int port)
    {
        port = PayloadTransport.DefaultPort;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value.Length > 5) return false;
        foreach (char c in value)
            if (c < '0' || c > '9') return false;
        if (!int.TryParse(value, out int parsed) || parsed < 1 || parsed > 65535) return false;
        port = parsed;
        return true;
    }

    public static string Describe(string release)
    {
        switch (Classify(release))
        {
            case GoldHenLoaderEvidence.NoIntegratedLoader:
                return "Sem loader integrado; um loader externo exige validação.";
            case GoldHenLoaderEvidence.LegacyNeedsVerification:
                return "Loader legado; compatibilidade ELF não comprovada.";
            case GoldHenLoaderEvidence.ElfCandidate:
                return "Candidato a ELF; execução e BGFT ainda não comprovados.";
            default:
                return "Versão não identificada; nenhuma compatibilidade presumida.";
        }
    }

    public static GoldHenLoaderEvidence Classify(string release)
    {
        string value = (release ?? "").Trim().ToLowerInvariant();
        if (value.StartsWith("v", StringComparison.Ordinal)) value = value.Substring(1);
        if (value.StartsWith("1.", StringComparison.Ordinal) &&
            Version.TryParse(value, out _))
            return GoldHenLoaderEvidence.NoIntegratedLoader;
        const string prefix = "2.4b18.";
        if (value.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(value.Substring(prefix.Length), out int revision) &&
            value == prefix + revision.ToString())
        {
            if (revision >= 5 && revision <= 14) return GoldHenLoaderEvidence.ElfCandidate;
            if (revision >= 1 && revision <= 4) return GoldHenLoaderEvidence.LegacyNeedsVerification;
        }
        // Exact documented builds only; unknown suffixes remain unknown.
        switch (value)
        {
            case "2.0b": case "2.0b2": case "2.0":
            case "2.1": case "2.1.1": case "2.1.2":
            case "2.2": case "2.2.1": case "2.2.2": case "2.2.3": case "2.2.4":
            case "2.3": case "2.4b5": case "2.4b10": case "2.4b11":
            case "2.4b12": case "2.4b13": case "2.4b14": case "2.4b15":
            case "2.4b16": case "2.4b16.2": case "2.4b17": case "2.4b17.2":
            case "2.4b17.3": case "2.4b18":
                return GoldHenLoaderEvidence.LegacyNeedsVerification;
        }
        return GoldHenLoaderEvidence.Unknown;
    }
}
