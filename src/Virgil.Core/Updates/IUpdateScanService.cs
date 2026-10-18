using Virgil.Contracts;

namespace Virgil.Core.Updates;

public interface IUpdateScanService
{
    Task<UpdateScanReport> ScanAsync(
        UpdateScanRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
