using System.Globalization;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Cleanup;

public sealed record CleanupSelectionResult(IReadOnlyList<CleanupStepResult> Steps, string? RefusalReason)
{
    public bool WasRefused => RefusalReason is not null;
    public static CleanupSelectionResult Refused(string reason) => new([], reason);
}

public sealed class CleanupSelectionAction : IMaintenanceAction
{
    private readonly ICleanupExecutionService _execution;
    private readonly CleanupZonePreview[] _selection;
    private readonly IProgress<CleanupProgress>? _progress;
    public CleanupSelectionAction(ICleanupExecutionService execution, IReadOnlyList<CleanupZonePreview> selection,
        IProgress<CleanupProgress>? progress = null)
    {
        _execution = execution;
        _selection = selection.Select(CleanupMaintenanceSnapshot.Copy).ToArray();
        _progress = progress;
    }
    public string Id => "cleanup.selection";
    public RiskLevel RiskLevel => CreatePreview(_selection).RiskLevel;
    public bool RequiresAdmin => _selection.Any(p => p.Definition.RequiresElevation);
    public bool RebootRequired => false;
    public Reversibility Reversibility => CreatePreview(_selection).Reversibility;
    public Task<ActionPreview> PreviewAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(CreatePreview(_selection)); }
    public async Task<ActionResult> ExecuteAsync(ConfirmedPlan? plan, CancellationToken cancellationToken)
    {
        var result = await _execution.ExecuteSelectionAsync(_selection, plan, _progress, cancellationToken).ConfigureAwait(false);
        return new CleanupSelectionActionResult(result);
    }
    public static ActionPreview CreatePreview(IReadOnlyList<CleanupZonePreview> selection)
    {
        var previews = selection.Select(CleanupActionPreviewFactory.Create).ToArray();
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Bytes"] = selection.Sum(p => p.EligibleBytes).ToString(CultureInfo.InvariantCulture),
            ["Files"] = selection.Sum(p => p.EligibleFileCount).ToString(CultureInfo.InvariantCulture),
            ["Sensitive"] = selection.Where(p => CleanupActionPreviewFactory.Create(p).RiskLevel >= RiskLevel.Sensitive).Sum(p => p.EligibleFileCount).ToString(CultureInfo.InvariantCulture)
        };
        for (var i = 0; i < previews.Length; i++) parameters["Selection:" + i] = ActionPreviewHasher.Hash(previews[i]);
        foreach (var group in Enum.GetValues<CleanupGroup>())
        {
            var rows = selection.Where(p => p.Definition.Group == group).ToArray();
            parameters[group.ToString()] = $"{rows.Sum(p => p.EligibleBytes)} octets, {rows.Sum(p => p.EligibleFileCount)} fichiers";
        }
        var personal = selection.Any(p => p.Definition.IsPersonalFolder || p.Candidates.Any(c => c.IsPersonalFolder));
        return new("cleanup.selection", personal ? "Nettoyage — dossier personnel" : "Nettoyage — confirmation récapitulative",
            previews.Length == 0 ? RiskLevel.Low : previews.Max(p => p.RiskLevel), previews.SelectMany(p => p.Targets).ToArray(),
            parameters, previews.SelectMany(p => p.Effects).Distinct().ToArray(), selection.Any(p => p.Definition.RequiresElevation), false,
            selection.Any(p => p.Definition.Id == CleanupZoneId.RecycleBin) ? Reversibility.Partial : Reversibility.Full, []);
    }
}

public sealed record CleanupSelectionActionResult : ActionResult
{
    public CleanupSelectionActionResult(CleanupSelectionResult result) : base("cleanup.selection",
        !result.WasRefused && result.Steps.All(s => s.Status == CleanupStepStatus.Completed), result.WasRefused, result.RefusalReason,
        result.Steps.SelectMany(s => s.Errors).ToArray(), result.Steps.Where(s => s.QuarantinedFiles > 0).Select(s => s.QuarantineSummary).ToArray(), [])
    { Selection = result; }
    public CleanupSelectionResult Selection { get; }
}
