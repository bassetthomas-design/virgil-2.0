namespace Virgil.Core.Interventions;

public enum ElevatedPathExistence
{
    MustExist,
    MustNotExist,
    Optional
}

public readonly record struct ElevatedPathEntry(bool Exists, FileAttributes Attributes)
{
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;

    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;

    public static ElevatedPathEntry Missing { get; } = new(false, 0);
}

public sealed class ElevatedPathGuard
{
    private readonly Func<string, ElevatedPathEntry> _inspect;

    public ElevatedPathGuard()
        : this(InspectPath)
    {
    }

    public ElevatedPathGuard(Func<string, ElevatedPathEntry> inspect)
    {
        _inspect = inspect;
    }

    public string GetProtocolRoot(string localAppData)
    {
        return Path.Combine(NormalizeLocalAppData(localAppData), "Virgil", "Temp");
    }

    public string ValidateRoot(string localAppData, bool allowMissingSecureDirectories)
    {
        var normalizedLocalAppData = NormalizeLocalAppData(localAppData);
        var virgilDirectory = Path.Combine(normalizedLocalAppData, "Virgil");
        var rootDirectory = Path.Combine(virgilDirectory, "Temp");

        ValidateDirectory(normalizedLocalAppData, mustExist: true);
        ValidateDirectory(virgilDirectory, mustExist: !allowMissingSecureDirectories);
        ValidateDirectory(rootDirectory, mustExist: !allowMissingSecureDirectories);
        return rootDirectory;
    }

    public string ValidateFilePath(
        string localAppData,
        string path,
        ElevatedPathExistence existence)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chemin protocole absolu invalide.");
        }

        var rootDirectory = ValidateRoot(localAppData, allowMissingSecureDirectories: false);
        var fullPath = NormalizePath(path);
        var parent = Path.GetDirectoryName(fullPath);

        if (!string.Equals(parent, rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Chemin hors racine Virgil autorisee.");
        }

        var entry = Inspect(fullPath);
        if (entry.Exists && entry.IsReparsePoint)
        {
            throw new InvalidOperationException("Point de reanalyse refuse.");
        }

        if (entry.Exists && entry.IsDirectory)
        {
            throw new InvalidOperationException("Un fichier etait attendu.");
        }

        if (existence == ElevatedPathExistence.MustExist && !entry.Exists)
        {
            throw new FileNotFoundException("Fichier protocole introuvable.", fullPath);
        }

        if (existence == ElevatedPathExistence.MustNotExist && entry.Exists)
        {
            throw new IOException("Le fichier protocole existe deja.");
        }

        return fullPath;
    }

    public string NormalizeLocalAppData(string localAppData)
    {
        if (string.IsNullOrWhiteSpace(localAppData) ||
            !Path.IsPathFullyQualified(localAppData) ||
            localAppData.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            localAppData.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Racine LocalAppData invalide.");
        }

        var fullPath = NormalizePath(localAppData);
        var volumeRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(volumeRoot) || !Directory.Exists(volumeRoot))
        {
            throw new InvalidOperationException("Racine Windows LocalAppData invalide.");
        }

        return fullPath;
    }

    private void ValidateDirectory(string path, bool mustExist)
    {
        var entry = Inspect(path);
        if (!entry.Exists)
        {
            if (mustExist)
            {
                throw new DirectoryNotFoundException("Dossier protocole introuvable.");
            }

            return;
        }

        if (!entry.IsDirectory)
        {
            throw new InvalidOperationException("Un dossier etait attendu.");
        }

        if (entry.IsReparsePoint)
        {
            throw new InvalidOperationException("Dossier point de reanalyse refuse.");
        }
    }

    private ElevatedPathEntry Inspect(string path)
    {
        try
        {
            return _inspect(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException("Verification physique du chemin impossible.", ex);
        }
    }

    private static ElevatedPathEntry InspectPath(string path)
    {
        try
        {
            return new ElevatedPathEntry(true, File.GetAttributes(path));
        }
        catch (FileNotFoundException)
        {
            return ElevatedPathEntry.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return ElevatedPathEntry.Missing;
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
