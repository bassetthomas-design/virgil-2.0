using Virgil.Domain;

namespace Virgil.Core.Interventions;

public interface IInterventionElevatedHelperClient
{
    Task<ElevatedInterventionResult> ExecuteAsync(
        InterventionDefinition definition,
        CancellationToken cancellationToken);
}

public interface IElevatedProcessLauncher
{
    Task<int> RunElevatedAsync(
        string helperPath,
        string requestPath,
        CancellationToken cancellationToken);
}
