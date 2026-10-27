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
        var references = snapshot.Installed.SelectMany(s => s.ReferencedPaths).Concat(snapshot.ActivePaths).Concat(snapshot.Evidence.SelectMany(e => e.Paths)).ToArray();
        var accepted = snapshot.Candidates.Where(c => c.Path.Length > 0 && c.Files.Any(f => f.SizeBytes > 0) && !IsProtected(c.Name) &&
            !DevelopmentCacheCatalog.Contains(c.Name) &&
            !snapshot.Installed.Any(i => MatchesInstalledName(c.Name, i.Name)) &&
            !snapshot.Evidence.Any(e => MatchesInstalledName(c.Name, e.Name) ||
                (e.RegistryKeys ?? []).Any(k => c.RegistryKeys.Any(ck => RegistryContains(k, ck)))) &&
            !references.Any(p => Overlaps(c.Path, p))).ToArray();
        var result = new List<CleanupZonePreview>();
        foreach (var group in accepted.GroupBy(c => Normalize(c.Name), StringComparer.OrdinalIgnoreCase))
        {
            var keys = group.SelectMany(c => c.RegistryKeys).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
            var total = group.SelectMany(c => c.Files).Sum(f => f.SizeBytes);
            var count = group.Sum(c => c.Files.Count);
            var last = group.Max(c => c.LastUsedUtc);
            foreach (var c in group)
            {
                var uncertain = c.IsUncertain || !snapshot.IsComplete || c.Name.Length < 3;
                var validRoot = c.Path.Length > 0 && CleanupPathGuard.IsStrictlyUnderRoot(c.Path, c.AllowedRoot);
                var files = c.Files.Where(f => validRoot && CleanupPathGuard.IsStrictlyUnderRoot(f.Path, c.Path))
                    .Select(f => new CleanupCandidate(CleanupZoneId.SoftwareLeftovers, f.Path, f.Path, f.SizeBytes, f.LastWriteUtc,
                        !CleanupSafetyClassifier.IsPersonalExtension(f.Path), CleanupSafetyClassifier.IsPersonalExtension(f.Path) ? "Type personnel ou archive protégé par les gardes existants." : null)
                    { Group = CleanupGroup.RestesLogiciels, NeedsReview = uncertain }).ToArray();
                var zone = new CleanupZoneDefinition(CleanupZoneId.SoftwareLeftovers,
                    $"{c.Name}{(uncertain ? " — Reste possible — à vérifier" : " (désinstallé)")} : {total} octets, {count} fichiers, {keys.Length} clés de registre, dernière utilisation : {last?.ToString("u") ?? "inconnue"}",
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
        return result.GroupBy(p => Normalize(p.Definition.SoftwareName ?? ""), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var zones = g.ToArray();
                if (zones.Length == 1) return zones[0];
                var files = zones.SelectMany(z => z.Candidates).DistinctBy(c => c.FullPath, StringComparer.OrdinalIgnoreCase).ToArray();
                var uncertain = zones.Any(z => z.Definition.NeedsReview);
                var definition = zones[0].Definition with
                {
                    RootPath = "", ValidatedRemnantRoot = null, RemnantAllowedRoot = null,
                    RemnantZones = zones.Select(z => z.Definition).ToArray(),
                    DisplayName = $"{zones[0].Definition.SoftwareName}{(uncertain ? " — Reste possible — à vérifier" : " (désinstallé)")}",
                    NeedsReview = uncertain, RegistryKeys = zones.SelectMany(z => z.Definition.RegistryKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    IsExecutable = zones.Any(z => z.Definition.IsExecutable)
                };
                return new CleanupZonePreview(definition, now, files.Length, files.Count(c => c.IsEligible),
                    files.Where(c => c.IsEligible).Sum(c => c.SizeBytes), files.Count(c => !c.IsEligible), files, []);
            }).ToArray();
    }

    public static bool IsProtected(string name) => Normalize(name) is "net" or "vc" || Protected.Any(p => Normalize(name).Contains(p, StringComparison.OrdinalIgnoreCase));
    public static bool MatchesInstalledName(string a, string b)
    {
        var left = Normalize(a); var right = Normalize(b);
        if (left.Length == 0 || right.Length == 0) return false;
        if (left == right) return true;
        if (Math.Min(left.Length, right.Length) >= 4 &&
            (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal))) return true;
        var tokens = System.Text.RegularExpressions.Regex.Split(b.ToLowerInvariant(), @"[^\p{L}\p{N}]+");
        return tokens.Any(t => t.Length >= 4 && left.Contains(t, StringComparison.Ordinal)) ||
            System.Text.RegularExpressions.Regex.Split(a.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
                .Any(t => t.Length >= 4 && right.Contains(t, StringComparison.Ordinal));
    }
    private static bool RegistryContains(string parent, string child) =>
        string.Equals(parent.TrimEnd('\\'), child.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
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
