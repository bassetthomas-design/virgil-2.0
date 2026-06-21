using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public sealed class CleanupSafetyClassifier
{
    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".raw", ".cr2", ".nef",
        ".mp4", ".mov", ".avi", ".mkv", ".doc", ".docx", ".xls", ".xlsx",
        ".ppt", ".pptx", ".pdf", ".blend", ".psd", ".prproj", ".aep"
    };

    private static readonly HashSet<string> ReviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".iso"
    };

    private static readonly string[] ProtectedNameFragments =
    {
        "photo", "video", "vidéo", "souvenir", "backup", "sauvegarde", "projet",
        "work", "travail", "maison", "famille", "enfant", "bébé", "bebe", "documents",
        "images", "musique", "films", "montage", "steam", "epic", "ubisoft", "games", "jeux"
    };

    private static readonly string[] ProtectedRootFragments =
    {
        "\\program files\\", "\\program files (x86)\\", "\\onedrive\\", "\\icloud\\",
        "\\google drive\\", "\\dropbox\\"
    };

    private static readonly string[] BrowserPersonalFragments =
    {
        "login data", "password", "bookmarks", "history", "cookies", "sessions",
        "extensions", "web data", "autofill", "preferences"
    };

    public CleanupClassification ClassifyPath(string path, bool isDirectory = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CleanupClassification.Protected;
        }

        var normalized = Normalize(path);
        var extension = isDirectory ? string.Empty : Path.GetExtension(path);

        if (ProtectedExtensions.Contains(extension) ||
            ProtectedRootFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            ProtectedNameFragments.Any(fragment => ContainsPathSegment(normalized, fragment)))
        {
            return CleanupClassification.Protected;
        }

        if (ReviewExtensions.Contains(extension))
        {
            return CleanupClassification.ReviewOnly;
        }

        return CleanupClassification.ReviewOnly;
    }

    public bool CanDeleteCandidate(CleanupZoneDefinition zone, string fullPath, out string reason)
    {
        reason = string.Empty;

        if (!zone.IsExecutable || zone.Classification is not (CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable))
        {
            reason = "Zone en lecture seule ou protegee.";
            return false;
        }

        if (!CleanupPathGuard.TryValidateContainedFile(fullPath, zone.RootPath, out var normalized, out reason))
        {
            return false;
        }

        var extension = Path.GetExtension(normalized);
        if (ProtectedExtensions.Contains(extension))
        {
            reason = "Fichier personnel protege.";
            return false;
        }

        if (ReviewExtensions.Contains(extension))
        {
            reason = "Archive ou image disque a revoir manuellement.";
            return false;
        }

        var relative = Normalize(Path.GetRelativePath(zone.RootPath, normalized));
        if (ProtectedNameFragments.Any(fragment => ContainsPathSegment(relative, fragment)))
        {
            reason = "Nom personnel ou sensible detecte.";
            return false;
        }

        if (zone.Id is CleanupZoneId.BrowserEdgeCache or CleanupZoneId.BrowserChromeCache or
            CleanupZoneId.BrowserFirefoxCache or CleanupZoneId.BrowserBraveCache or CleanupZoneId.BrowserOperaCache)
        {
            if (BrowserPersonalFragments.Any(fragment => relative.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                reason = "Donnee de profil navigateur protegee.";
                return false;
            }
        }

        if (zone.AllowedExtensions.Count > 0 && !zone.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            reason = "Type de fichier non autorise pour cette zone.";
            return false;
        }

        if (zone.RequiredPathFragments.Count > 0 &&
            !zone.RequiredPathFragments.Any(fragment => relative.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "Chemin non reconnu dans cette zone.";
            return false;
        }

        if (zone.ExcludedPathFragments.Any(fragment => relative.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "Chemin explicitement exclu.";
            return false;
        }

        return true;
    }

    public static bool IsPersonalExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return ProtectedExtensions.Contains(extension) || ReviewExtensions.Contains(extension);
    }

    public bool CanDeleteEmptyDirectory(CleanupZoneDefinition zone, string directoryPath)
    {
        if (!zone.IsExecutable || !CleanupPathGuard.IsStrictlyUnderRoot(directoryPath, zone.RootPath) ||
            CleanupPathGuard.HasReparsePointAtPath(directoryPath))
        {
            return false;
        }

        var relative = Normalize(Path.GetRelativePath(zone.RootPath, directoryPath));
        return !ProtectedNameFragments.Any(fragment => ContainsPathSegment(relative, fragment)) &&
            !zone.ExcludedPathFragments.Any(fragment => relative.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path) => path.Replace('/', '\\').ToLowerInvariant();

    private static bool ContainsPathSegment(string path, string fragment)
    {
        return path.Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
