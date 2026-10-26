using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

/// <summary>Renforce les gardes existants, sans autoriser un type de fichier qu'ils refusent.</summary>
public sealed class PersonalFolderPolicy(IKnownFoldersProvider folders)
{
    public bool IsPersonal(string path) => IsPersonal(path, folders.GetFolders());

    private static bool IsPersonal(string path, IReadOnlyList<KnownFolderPath> known) => known.Where(f => f.Kind != KnownFolder.Downloads)
        .Any(f => Same(path, f.Path) || CleanupPathGuard.IsStrictlyUnderRoot(path, f.Path));

    public bool CanQuarantine(CleanupZoneDefinition zone, string path)
        => CanQuarantine(zone, path, folders.GetFolders());

    private static bool CanQuarantine(CleanupZoneDefinition zone, string path, IReadOnlyList<KnownFolderPath> known)
    {
        if (zone.RemnantZones.Count > 0)
        {
            var resolved = CleanupRemnantResolver.Resolve(zone, path);
            return resolved is not null && CanQuarantine(resolved, path, known);
        }
        if (known.Any(f => Same(path, f.Path))) return false;
        if (zone.Id == CleanupZoneId.SoftwareLeftovers && known.Any(f => Same(zone.RootPath, f.Path) ||
            CleanupPathGuard.IsStrictlyUnderRoot(f.Path, zone.RootPath))) return false;
        if (!IsPersonal(path, known)) return true;
        return zone.Id == CleanupZoneId.SoftwareLeftovers && zone.ValidatedRemnantRoot is { } remnant &&
            zone.RemnantAllowedRoot is { } root && CleanupPathGuard.IsStrictlyUnderRoot(remnant, root) &&
            Same(zone.RootPath, remnant) && CleanupPathGuard.IsStrictlyUnderRoot(path, remnant);
    }

    public CleanupZonePreview Apply(CleanupZonePreview preview)
    {
        var known = folders.GetFolders();
        var candidates = preview.Candidates.Select(c => c with
        {
            Group = preview.Definition.Group,
            IsPersonalFolder = IsPersonal(c.FullPath, known),
            IsEligible = c.IsEligible && CanQuarantine(preview.Definition, c.FullPath, known),
            ExclusionReason = CanQuarantine(preview.Definition, c.FullPath, known) ? c.ExclusionReason : "Dossier personnel protégé : aucun reste identifié."
        }).ToArray();
        // Une zone personnelle entière ne peut jamais entrer dans une sélection de groupe.
        return preview with
        {
            Definition = preview.Definition with { IsPersonalFolder = IsPersonal(preview.Definition.RootPath, known) || candidates.Any(c => c.IsPersonalFolder) },
            Candidates = candidates,
            EligibleFileCount = preview.Definition.Id == CleanupZoneId.RecycleBin ? preview.EligibleFileCount : candidates.Count(c => c.IsEligible),
            EligibleBytes = preview.Definition.Id == CleanupZoneId.RecycleBin ? preview.EligibleBytes : candidates.Where(c => c.IsEligible).Sum(c => c.SizeBytes)
        };
    }

    private static bool Same(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
