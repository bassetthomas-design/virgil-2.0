using Virgil.Contracts.Applications;
using Virgil.Core.Applications;

namespace Virgil.Tests;

internal sealed class EmptyApplicationInventoryService : IApplicationInventoryService
{
    public Task<ApplicationInventoryReport> InventoryAsync(
        IProgress<ApplicationInventoryProgress>? progress,
        CancellationToken cancellationToken) => Task.FromResult(new ApplicationInventoryReport());
}
