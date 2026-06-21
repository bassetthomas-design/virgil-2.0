using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public sealed class CleanupPermissionRepairService
{
    private static readonly string[] ForbiddenFragments =
    {
        "\\documents", "\\pictures", "\\images", "\\videos", "\\desktop",
        "\\downloads", "\\onedrive", "\\icloud", "\\dropbox", "\\google drive",
        "\\program files", "\\steam", "\\epic", "\\ubisoft", "\\games", "\\jeux"
    };

    private readonly IReadOnlyList<CleanupZoneDefinition> _allowedZones;

    public CleanupPermissionRepairService(IReadOnlyList<CleanupZoneDefinition>? allowedZones = null)
    {
        _allowedZones = (allowedZones ?? CleanupZoneCatalog.CreateDefault())
            .Where(zone => zone.Classification is CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable)
            .Where(zone => !string.IsNullOrWhiteSpace(zone.RootPath))
            .ToList();
    }

    public CleanupPermissionRepairAssessment Assess(string exactPath)
    {
        if (string.IsNullOrWhiteSpace(exactPath))
        {
            return Refuse(exactPath, "Chemin vide refuse.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(exactPath);
        }
        catch
        {
            return Refuse(exactPath, "Chemin invalide.");
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.Equals(Trim(fullPath), Trim(root ?? string.Empty), StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(fullPath, "Une racine de lecteur ne peut jamais etre reparee.");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var usersRoot = string.IsNullOrWhiteSpace(profile) ? string.Empty : Directory.GetParent(profile)?.FullName ?? string.Empty;
        if (IsExact(fullPath, profile) || IsExact(fullPath, usersRoot) ||
            IsExact(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)) ||
            IsExact(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)) ||
            IsExact(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)))
        {
            return Refuse(fullPath, "Profil ou dossier personnel complet refuse.");
        }

        var normalized = fullPath.Replace('/', '\\');
        if (ForbiddenFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return Refuse(fullPath, "Chemin personnel, applicatif ou sensible refuse.");
        }

        var zone = _allowedZones.FirstOrDefault(candidate => CleanupPathGuard.IsStrictlyUnderRoot(fullPath, candidate.RootPath));
        if (zone is null)
        {
            return Refuse(fullPath, "Chemin hors allowlist technique.");
        }

        if (CleanupPathGuard.HasReparsePointAtPath(fullPath))
        {
            return Refuse(fullPath, "Point de reanalyse refuse.");
        }

        return new CleanupPermissionRepairAssessment(
            fullPath,
            true,
            $"Sous-dossier technique autorise : {zone.DisplayName}. Validation critique et helper eleve requis.");
    }

    private static CleanupPermissionRepairAssessment Refuse(string path, string reason) => new(path, false, reason);

    private static string Trim(string value) => value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsExact(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(right) && string.Equals(Trim(left), Trim(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    }
}
