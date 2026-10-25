using Virgil.Contracts;

namespace Virgil.Core.Monitoring;

public interface IMonitoringService
{
    Task<SystemHealthSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken);
}
