using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationRemnantClassifier
{
    private static readonly string[] ProtectedExtensions =
    [
        ".jpg", ".jpeg", ".png", ".heic", ".raw", ".mp4", ".mov", ".avi", ".mkv",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".zip", ".rar",
        ".7z", ".iso", ".blend", ".psd", ".prproj", ".aep", ".obs", ".db"
    ];

    private static readonly string[] ProtectedNames =
    [
        "project", "projet", "backup", "sauvegarde", "save", "saves", "mods",
        "profiles", "presets", "library", "bibliotheque", "bibliothèque", "photos",
        "videos", "vidéos", "documents", "family", "famille", "enfant", "bébé",
        "maison", "travail", "work", "minecraft", "steam", "adobe", "blender", "obs",
        "license", "licence", "token", "password", "secret", "key"
    ];

    private static readonly string[] TechnicalNames =
    [
        "cache", "temp", "tmp", "logs", "log", "crash", "dump", "dumps", "wer",
        "telemetry", "thumbnail", "thumbs"
    ];

    public ApplicationRemnantCandidate Classify(string path, bool isDirectory, long? sizeBytes = null)
    {
        var fileName = Path.GetFileName(path);
        var lowerPath = path.ToLowerInvariant();
        var lowerName = fileName.ToLowerInvariant();
        var extension = isDirectory ? string.Empty : Path.GetExtension(path).ToLowerInvariant();

        if (IsCloudOrPersonalRoot(lowerPath) || ProtectedExtensions.Contains(extension) || ContainsAny(lowerName, ProtectedNames))
        {
            return Candidate(path, isDirectory, sizeBytes, ApplicationRemnantKind.ProtectedRemnant, "Donnee personnelle, projet, sauvegarde, licence ou dossier ambigu protege.");
        }

        if (!isDirectory && ContainsAny(lowerName, TechnicalNames))
        {
            return Candidate(path, isDirectory, sizeBytes, ApplicationRemnantKind.TechnicalRemnant, "Fichier technique probable.");
        }

        if (isDirectory && ContainsAny(lowerName, TechnicalNames))
        {
            return Candidate(path, isDirectory, sizeBytes, ApplicationRemnantKind.TechnicalRemnant, "Dossier technique probable.");
        }

        if (lowerPath.Contains(@"\appdata\", StringComparison.OrdinalIgnoreCase))
        {
            return Candidate(path, isDirectory, sizeBytes, ApplicationRemnantKind.UnknownRemnant, "Reste AppData non classe : lecture seule et revue manuelle.");
        }

        return Candidate(path, isDirectory, sizeBytes, ApplicationRemnantKind.UnknownRemnant, "Reste non vide ou ambigu : revue manuelle requise.");
    }

    private static ApplicationRemnantCandidate Candidate(
        string path,
        bool isDirectory,
        long? sizeBytes,
        ApplicationRemnantKind kind,
        string reason)
    {
        var actions = kind == ApplicationRemnantKind.TechnicalRemnant
            ? new[] { ApplicationRemnantAction.OpenLocation, ApplicationRemnantAction.ExportList, ApplicationRemnantAction.Ignore, ApplicationRemnantAction.MarkReview, ApplicationRemnantAction.DeleteTechnicalOnly }
            : new[] { ApplicationRemnantAction.OpenLocation, ApplicationRemnantAction.ExportList, ApplicationRemnantAction.Ignore, ApplicationRemnantAction.MarkReview };

        return new ApplicationRemnantCandidate
        {
            Path = path,
            DisplayName = Path.GetFileName(path),
            IsDirectory = isDirectory,
            SizeBytes = sizeBytes,
            Kind = kind,
            Reason = reason,
            AvailableActions = actions
        };
    }

    private static bool IsCloudOrPersonalRoot(string lowerPath)
    {
        return lowerPath.Contains("onedrive") ||
            lowerPath.Contains("dropbox") ||
            lowerPath.Contains("google drive") ||
            lowerPath.Contains(@"\documents\") ||
            lowerPath.Contains(@"\pictures\") ||
            lowerPath.Contains(@"\photos\") ||
            lowerPath.Contains(@"\videos\") ||
            lowerPath.Contains(@"\desktop\");
    }

    private static bool ContainsAny(string value, IEnumerable<string> terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

