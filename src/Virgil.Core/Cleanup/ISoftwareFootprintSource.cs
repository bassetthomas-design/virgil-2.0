namespace Virgil.Core.Cleanup;

public sealed record InstalledSoftwareFootprint(string Name, IReadOnlyList<string> ReferencedPaths);
public sealed record FootprintFile(string Path, long SizeBytes, DateTimeOffset LastWriteUtc);
public sealed record SoftwareFootprintCandidate(string Name, string Path, string AllowedRoot,
    IReadOnlyList<FootprintFile> Files, IReadOnlyList<string> RegistryKeys, DateTimeOffset? LastUsedUtc, bool IsUncertain = false);
public sealed record SoftwareFootprintSnapshot(IReadOnlyList<InstalledSoftwareFootprint> Installed,
    IReadOnlyList<string> ActivePaths, IReadOnlyList<SoftwareFootprintCandidate> Candidates, bool IsComplete = true);

/// <summary>Lecture seule : aucune primitive d'écriture du registre.</summary>
public interface ISoftwareFootprintSource
{
    Task<SoftwareFootprintSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public interface IDownloadsSource
{
    IReadOnlyList<FootprintFile> ReadFiles(string downloadsRoot, CancellationToken cancellationToken);
}

public interface IWindowsOldSource
{
    Virgil.Contracts.CleanupZonePreview? Read(CancellationToken cancellationToken);
}
