using Virgil.Domain;

namespace Virgil.Core.Resources;

public interface IProcessInspectionService
{
    Task<IReadOnlyList<ProcessResourceInfo>> InspectAsync(
        TimeSpan observationDuration,
        int maximumProcesses,
        CancellationToken cancellationToken);
}
