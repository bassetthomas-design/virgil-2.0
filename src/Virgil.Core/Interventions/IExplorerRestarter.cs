using Virgil.Contracts;

namespace Virgil.Core.Interventions;

/// <summary>Internal OS adapter. Only ProcessActionService and InterventionExecutionService may call it,
/// after consuming their ConfirmedPlan. It does not issue or consume a second plan.</summary>
public interface IExplorerRestarter
{
    Task<InterventionExecutionResult> RestartAsync(
        InterventionDiagnostic diagnostic,
        CancellationToken cancellationToken);
}
