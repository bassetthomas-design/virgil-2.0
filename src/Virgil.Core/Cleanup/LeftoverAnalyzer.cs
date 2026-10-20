using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

/// <summary>Analyse pure d'un instantané fourni par le port ; aucun accès OS.</summary>
public sealed class LeftoverAnalyzer
{
    private static readonly string[] Protected = ["microsoft", "windows", "nvidia", "amd", "intel", "realtek", "virgil",
        "antivirus", "security", "defender", "avast", "avg", "kaspersky", "bitdefender", "eset", "mcafee", "norton",
        "sophos", "malwarebytes", "crowdstrike", "sentinel", "trendmicro", "avira", "fsecure", "netframework", "dotnet", "visualc", "vcredist", "runtime", "driver", "commonfiles", "packages", "packagecache"];

    public IReadOnlyList<CleanupZonePreview> Analyze(SoftwareFootprintSnapshot snapshot, DateTimeOffset now)
    {
        var references = snapshot.Installed.SelectMany(s => s.ReferencedPaths).Concat(snapshot.ActivePaths).ToArray();
        var accepted = snapshot.Candidates.Where(c => !IsProtected(c.Name) &&
            !snapshot.Installed.Any(i => MatchesInstalledName(c.Name, i.Name)) &&
            (c.Path.Length == 0 || !references.Any(p => Overlaps(c.Path, p)))).ToArray();
        var result = new List<CleanupZonePreview>();
        foreach (var group in accepted.GroupBy(c => Normalize(c.Name), StringComparer.OrdinalIgnoreCase))
        {
            var keys = group.SelectMany(c => c.RegistryKeys).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
            var total = group.SelectMany(c => c.Files).Sum(f => f.SizeBytes);
            var count = group.Sum(c => c.Files.Count);
            var last = group.Max(c => c.LastUsedUtc);
            foreach (var c in group)
            {
                var uncertain = c.IsUncertain || !snapshot.IsComplete || c.Name.Length < 3 ||
                    snapshot.Installed.Any(i => Normalize(i.Name).Contains(Normalize(c.Name), StringComparison.OrdinalIgnoreCase) || Normalize(c.Name).Contains(Normalize(i.Name), StringComparison.OrdinalIgnoreCase));
                var validRoot = c.Path.Length > 0 && CleanupPathGuard.IsStrictlyUnderRoot(c.Path, c.AllowedRoot);
                var files = c.Files.Where(f => validRoot && CleanupPathGuard.IsStrictlyUnderRoot(f.Path, c.Path))
                    .Select(f => new CleanupCandidate(CleanupZoneId.SoftwareLeftovers, f.Path, f.Path, f.SizeBytes, f.LastWriteUtc,
                        !CleanupSafetyClassifier.IsPersonalExtension(f.Path), CleanupSafetyClassifier.IsPersonalExtension(f.Path) ? "Type personnel ou archive protégé par les gardes existants." : null)
                    { Group = CleanupGroup.RestesLogiciels, NeedsReview = uncertain }).ToArray();
                var zone = new CleanupZoneDefinition(CleanupZoneId.SoftwareLeftovers,
                    $"{c.Name} (désinstallé){(uncertain ? " — à confirmer" : "")} : {total} octets, {count} fichiers, {keys.Length} clés de registre, dernière utilisation : {last?.ToString("u") ?? "inconnue"}",
                    "Suppression des clés de registre : lot ultérieur (sauvegarde et point de restauration)", c.Path, TimeSpan.Zero,
                    CleanupRiskLevel.Medium, "Vérifiez l'identification du logiciel avant toute sélection.", "Mise en quarantaine des fichiers uniquement.", "Clés de registre : lecture seule.", 500)
                {
                    Classification = CleanupClassification.AdvancedCleanable, RequiresReinforcedConfirmation = true,
                    IsExecutable = validRoot && files.Length > 0, NeedsReview = uncertain, SoftwareName = group.First().Name,
                    ValidatedRemnantRoot = validRoot ? c.Path : null, RemnantAllowedRoot = validRoot ? c.AllowedRoot : null,
                    RegistryKeys = keys, LastUsedUtc = last
                };
                result.Add(new(zone, now, files.Length, files.Count(f => f.IsEligible), files.Where(f => f.IsEligible).Sum(f => f.SizeBytes), files.Count(f => !f.IsEligible), files, []));
            }
        }
        return result;
    }

    public static bool IsProtected(string name) => Normalize(name) is "net" or "vc" || Protected.Any(p => Normalize(name).Contains(p, StringComparison.OrdinalIgnoreCase));
    public static bool MatchesInstalledName(string a, string b) => Normalize(a) == Normalize(b);
    private static string Normalize(string name)
    {
        var value = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        foreach (var suffix in new[] { "incorporated", "corporation", "company", "limited", "gmbh", "llc", "corp", "sarl", "ltd", "inc", "sas" })
            if (value.Length > suffix.Length + 2 && value.EndsWith(suffix, StringComparison.Ordinal))
                return value[..^suffix.Length];
        return value;
    }
    private static bool Overlaps(string a, string b) => !string.IsNullOrWhiteSpace(b) &&
        (string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
         CleanupPathGuard.IsStrictlyUnderRoot(a, b) || CleanupPathGuard.IsStrictlyUnderRoot(b, a));
}
