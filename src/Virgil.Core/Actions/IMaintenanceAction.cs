using Virgil.Contracts.Actions;

namespace Virgil.Core.Actions;

public interface IMaintenanceAction
{
    string Id { get; }
    RiskLevel RiskLevel { get; }
    bool RequiresAdmin { get; }
    bool RebootRequired { get; }
    Reversibility Reversibility { get; }
    Task<ActionPreview> PreviewAsync(CancellationToken cancellationToken);
    Task<ActionResult> ExecuteAsync(ConfirmedPlan? plan, CancellationToken cancellationToken);
}
