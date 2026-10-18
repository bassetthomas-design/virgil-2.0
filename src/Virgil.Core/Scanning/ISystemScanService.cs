using Virgil.Contracts;

namespace Virgil.Core.Scanning;

public interface ISystemScanService
{
    Task<SystemScanReport> RunAsync(
        ScanMode mode,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}
