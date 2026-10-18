using Virgil.Contracts;

namespace Virgil.Core.Monitoring;

public interface IMonitoringService
{
    SystemHealthSnapshot CaptureSnapshot();
}
