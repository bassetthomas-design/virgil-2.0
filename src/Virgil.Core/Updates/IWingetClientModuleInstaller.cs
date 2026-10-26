namespace Virgil.Core.Updates;

/// <summary>OS installation adapter, called only after the service consumes its plan.</summary>
public interface IWingetClientModuleInstaller
{
    Task<bool> InstallAsync(CancellationToken cancellationToken);
}
