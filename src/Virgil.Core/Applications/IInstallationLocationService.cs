namespace Virgil.Core.Applications;

/// <summary>Sonde et ouvre un emplacement via les adaptateurs OS, sans suppression.</summary>
public interface IInstallationLocationService
{
    Task<InstallationLocationOpenResult> OpenAsync(string path, CancellationToken cancellationToken);
}

public enum InstallationLocationOpenResult
{
    Opened,
    Missing,
    Failed
}
