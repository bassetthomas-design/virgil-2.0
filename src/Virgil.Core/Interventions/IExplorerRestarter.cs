using Virgil.Domain;

namespace Virgil.Core.Interventions;

public interface IExplorerRestarter
{
    Task<InterventionExecutionResult> RestartAsync(
        InterventionDiagnostic diagnostic,
        CancellationToken cancellationToken);
}
