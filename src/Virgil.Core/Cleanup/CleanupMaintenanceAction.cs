using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Cleanup;

/// <summary>Une action par zone, produite par le module à partir des constats du scan.</summary>
public sealed class CleanupMaintenanceAction : IMaintenanceAction
{
    private readonly ICleanupExecutionService _execution;
    private readonly CleanupZonePreview _preview;
    private readonly IProgress<CleanupProgress>? _progress;

    public CleanupMaintenanceAction(ICleanupExecutionService execution, CleanupZonePreview preview,
        IProgress<CleanupProgress>? progress = null)
    {
        _execution = execution;
        _preview = CleanupMaintenanceSnapshot.Copy(preview);
        _progress = progress;
    }

    public CleanupZoneId ZoneId => _preview.Definition.Id;
    public string Id => CreatePreview().ActionId;
    public RiskLevel RiskLevel => CreatePreview().RiskLevel;
    public bool RequiresAdmin => _preview.Definition.RequiresElevation;
    public bool RebootRequired => false;
    public Reversibility Reversibility => CreatePreview().Reversibility;

    public Task<ActionPreview> PreviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreatePreview());
    }

    public async Task<ActionResult> ExecuteAsync(ConfirmedPlan? plan, CancellationToken cancellationToken)
    {
        // Le service existant reconstruit le même aperçu et consomme la porte avant tout effet.
        var step = await _execution.ExecuteZoneAsync(_preview, plan, _progress, cancellationToken).ConfigureAwait(false);
        return new CleanupMaintenanceResult(Id, step);
    }

    private ActionPreview CreatePreview() => CleanupActionPreviewFactory.Create(_preview);
}

/// <summary>Conserve les métriques du parcours existant pour les rapports et l'interface.</summary>
public sealed record CleanupMaintenanceResult : ActionResult
{
    public CleanupMaintenanceResult(string actionId, CleanupStepResult step)
        : base(actionId, step.Status == CleanupStepStatus.Completed,
            step.RefusalReason is not null, step.RefusalReason,
            step.Errors,
            step.QuarantinedFiles > 0 ? new[] { step.QuarantineSummary } : step.DeletedFiles > 0 ? new[] { $"{step.DeletedFiles} fichiers supprimés, {step.DeletedBytes} octets libérés." } : Array.Empty<string>(),
            Array.Empty<string>())
    {
        Step = step;
    }

    public CleanupStepResult Step { get; }
}
