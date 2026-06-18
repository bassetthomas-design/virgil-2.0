using Virgil.Domain;

namespace Virgil.Core.Interventions;

public interface IInterventionExecutionService
{
    Task<InterventionExecutionResult> ExecuteAsync(
        InterventionDiagnostic diagnostic,
        bool confirmed,
        CancellationToken cancellationToken);

    InterventionExecutionResult Skip(InterventionDiagnostic diagnostic);

    InterventionExecutionResult Cancel(InterventionDiagnostic diagnostic);

    InterventionSessionReport CreateReport(
        DateTimeOffset startedAt,
        IReadOnlyList<InterventionDiagnostic> proposedActions,
        IReadOnlyList<InterventionExecutionResult> results,
        IReadOnlyList<string> errors);
}
