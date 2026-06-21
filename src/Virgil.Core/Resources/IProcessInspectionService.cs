using Virgil.Domain;

namespace Virgil.Core.Resources;

public interface IProcessInspectionService
{
    Task<ProcessInspectionResult> InspectAsync(
        TimeSpan observationDuration,
        int maximumProcesses,
        CancellationToken cancellationToken);
}
