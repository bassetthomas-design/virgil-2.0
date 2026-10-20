using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

/// <summary>Renforce les gardes existants, sans autoriser un type de fichier qu'ils refusent.</summary>
public sealed class PersonalFolderPolicy(IKnownFoldersProvider folders)
{
    public bool IsPersonal(string path) => folders.GetFolders().Where(f => f.Kind != KnownFolder.Downloads)
        .Any(f => Same(path, f.Path) || CleanupPathGuard.IsStrictlyUnderRoot(path, f.Path));

    public bool CanQuarantine(CleanupZoneDefinition zone, string path)
    {
        var known = folders.GetFolders();
        if (known.Any(f => Same(path, f.Path))) return false;
        if (!IsPersonal(path)) return true;
        return zone.Id == CleanupZoneId.SoftwareLeftovers && zone.ValidatedRemnantRoot is { } remnant &&
            zone.RemnantAllowedRoot is { } root && CleanupPathGuard.IsStrictlyUnderRoot(remnant, root) &&
            Same(zone.RootPath, remnant) && CleanupPathGuard.IsStrictlyUnderRoot(path, remnant);
    }

    public CleanupZonePreview Apply(CleanupZonePreview preview)
    {
        var candidates = preview.Candidates.Select(c => c with
        {
            Group = preview.Definition.Group,
            IsPersonalFolder = IsPersonal(c.FullPath),
            IsEligible = c.IsEligible && CanQuarantine(preview.Definition, c.FullPath),
            ExclusionReason = CanQuarantine(preview.Definition, c.FullPath) ? c.ExclusionReason : "Dossier personnel protégé : aucun reste identifié."
        }).ToArray();
        // Une zone personnelle entière ne peut jamais entrer dans une sélection de groupe.
        return preview with
        {
            Definition = preview.Definition with { IsPersonalFolder = IsPersonal(preview.Definition.RootPath) || candidates.Any(c => c.IsPersonalFolder) },
            Candidates = candidates,
            EligibleFileCount = preview.Definition.Id == CleanupZoneId.RecycleBin ? preview.EligibleFileCount : candidates.Count(c => c.IsEligible),
            EligibleBytes = preview.Definition.Id == CleanupZoneId.RecycleBin ? preview.EligibleBytes : candidates.Where(c => c.IsEligible).Sum(c => c.SizeBytes)
        };
    }

    private static bool Same(string a, string b) => !string.IsNullOrWhiteSpace(b) &&
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
}
