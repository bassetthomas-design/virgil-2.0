using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Resources;

namespace Virgil.Core.Diagnostic;

/// <summary>Adaptateur du port de fermeture existant ; aucune fermeture parallèle ni nouveau lanceur.</summary>
public sealed class DiagnosticCloseAction(IProcessActionService service, ProcessResourceInfo target, bool force) : IMaintenanceAction
{
    private ProcessActionKind Kind => IsForced ? ProcessActionKind.KillProcess : ProcessActionKind.CloseMainWindow;
    public int ProcessId => target.ProcessId;
    public bool IsForced { get; } = force;
    public string Id => IsForced ? "resources.kill" : "resources.close";
    public RiskLevel RiskLevel => IsForced ? RiskLevel.Sensitive : RiskLevel.Low;
    public bool RequiresAdmin => false;
    public bool RebootRequired => false;
    public Reversibility Reversibility => Reversibility.None;
    public Task<ActionPreview> PreviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProcessActionPreviewFactory.Create(Kind, target));
    }
    public async Task<ActionResult> ExecuteAsync(ConfirmedPlan? plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target.StartedAt is null || string.IsNullOrWhiteSpace(target.Path) || target.Name.Contains("virgil", StringComparison.OrdinalIgnoreCase))
            return new(Id, false, true, "Processus protégé ou identité incomplète.", [], [], []);
        var result = await service.ExecuteAsync(Kind, target, plan, cancellationToken).ConfigureAwait(false);
        return new(Id, result.Status == ProcessActionStatus.Completed, result.Status == ProcessActionStatus.Failed,
            result.Status == ProcessActionStatus.Failed ? result.ReadableError : null,
            result.ReadableError is null ? [] : [result.ReadableError], [result.Summary], []);
    }
}
