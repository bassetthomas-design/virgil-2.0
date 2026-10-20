using Virgil.Contracts.Actions;

namespace Virgil.Core.Actions;

public interface IMaintenanceModule
{
    string Id { get; }
    string Title { get; }
    string Category { get; }
    Task<Findings> ScanAsync(CancellationToken cancellationToken);
}
