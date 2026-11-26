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
        if (value == "2.0b" || value == "2.0" || value == "2.1" ||
            value == "2.2" || value == "2.3" || value == "2.4b18")
            return GoldHenLoaderEvidence.LegacyNeedsVerification;
        return GoldHenLoaderEvidence.Unknown;
    }
}
