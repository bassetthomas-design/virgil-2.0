using Virgil.Core.Actions;
using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public interface IInterventionExecutionService
{
    Task<InterventionExecutionResult> ExecuteAsync(
        InterventionDiagnostic diagnostic,
        ConfirmedPlan? plan,
        CancellationToken cancellationToken);

    Task<InterventionExecutionResult> ExecuteAsync(InterventionDiagnostic diagnostic, ConfirmedPlan? plan,
        IProgress<InterventionProgress>? progress, CancellationToken cancellationToken)
        => ExecuteAsync(diagnostic, plan, cancellationToken);

    InterventionExecutionResult Skip(InterventionDiagnostic diagnostic);

    InterventionExecutionResult Cancel(InterventionDiagnostic diagnostic);

    InterventionSessionReport CreateReport(
        DateTimeOffset startedAt,
        IReadOnlyList<InterventionDiagnostic> proposedActions,
        IReadOnlyList<InterventionExecutionResult> results,
        IReadOnlyList<string> errors);
}
