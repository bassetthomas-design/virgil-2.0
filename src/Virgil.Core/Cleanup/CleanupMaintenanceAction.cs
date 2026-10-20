using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Cleanup;

/// <summary>Une action par zone ; WithPreview crée une instance liée à un instantané du scan.</summary>
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
    public Reversibility Reversibility => Reversibility.None;

    public CleanupMaintenanceAction WithPreview(CleanupZonePreview preview, IProgress<CleanupProgress>? progress = null)
    {
        if (preview.Definition.Id != ZoneId)
            throw new ArgumentException("L'aperçu ne correspond pas à la zone.", nameof(preview));
        return new CleanupMaintenanceAction(_execution, preview, progress);
    }

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
            step.Status == CleanupStepStatus.Expired || (step.Status == CleanupStepStatus.Cancelled && step.Errors.Count > 0),
            step.Status == CleanupStepStatus.Expired || (step.Status == CleanupStepStatus.Cancelled && step.Errors.Count > 0)
                ? string.Join("\n", step.Errors) : null,
            step.Errors,
            step.DeletedFiles > 0 ? new[] { $"{step.DeletedFiles} fichiers supprimés, {step.DeletedBytes} octets libérés." } : Array.Empty<string>(),
            Array.Empty<string>())
    {
        Step = step;
    }

    public CleanupStepResult Step { get; }
}
