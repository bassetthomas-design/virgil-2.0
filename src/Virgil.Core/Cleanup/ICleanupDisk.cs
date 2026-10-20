namespace Virgil.Core.Cleanup;

/// <summary>Accès disque de l'exécution, injecté ; les politiques restent dans Core.</summary>
public interface ICleanupDisk
{
    IRecycleBinService RecycleBin { get; }
    CleanupFileState ReadFile(string path);
    void DeleteFile(string path);
    IReadOnlyList<string> GetDirectories(string path);
    bool HasEntries(string path);
    void DeleteEmptyDirectory(string path);
}

public sealed record CleanupFileState(long Length, DateTimeOffset LastWriteTimeUtc);
