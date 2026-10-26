using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public interface IInterventionElevatedHelperClient
{
    Task<ElevatedInterventionResult> ExecuteAsync(
        InterventionDefinition definition,
        CancellationToken cancellationToken);
    Task<ElevatedInterventionResult> ExecuteAsync(InterventionDefinition definition,
        IProgress<InterventionProgress>? progress, CancellationToken cancellationToken)
        => ExecuteAsync(definition, cancellationToken);
}

public interface IElevatedProcessLauncher
{
    Task<int> RunElevatedAsync(
        string helperPath,
        string requestPath,
        CancellationToken cancellationToken);
}
