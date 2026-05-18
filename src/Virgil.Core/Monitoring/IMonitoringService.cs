using Virgil.Domain;

namespace Virgil.Core.Monitoring;

public interface IMonitoringService
{
    SystemHealthSnapshot CaptureSnapshot();
}
