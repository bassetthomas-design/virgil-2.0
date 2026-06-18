using Virgil.Domain;

namespace Virgil.Core.Interventions;

public interface IInterventionDiagnosticService
{
    Task<IReadOnlyList<InterventionDiagnostic>> DiagnoseAllAsync(CancellationToken cancellationToken);

    Task<InterventionDiagnostic> DiagnoseAsync(InterventionId id, CancellationToken cancellationToken);
}
