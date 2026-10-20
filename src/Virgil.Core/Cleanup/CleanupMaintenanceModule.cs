using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Cleanup;

public sealed class CleanupMaintenanceModule : IMaintenanceModule
{
    private readonly CleanupAnalyzer _analyzer;
    private readonly AdditionalCleanupScanner? _additional;
    private readonly PersonalFolderPolicy? _personal;
    private readonly Func<int> _retention;
    private readonly ICleanupExecutionService _sessionResults;

    public CleanupMaintenanceModule(CleanupAnalyzer analyzer, ICleanupExecutionService sessionResults,
        Func<int>? retention = null, AdditionalCleanupScanner? additional = null, PersonalFolderPolicy? personal = null)
    {
        _analyzer = analyzer;
        _additional = additional;
        _personal = personal;
        _retention = retention ?? (() => 7);
        _sessionResults = sessionResults;
    }

    public string Id => "cleanup";
    public string Title => "Nettoyage";
    public string Category => "Maintenance";

    public Task<Findings> ScanAsync(CancellationToken cancellationToken) => ScanAsync(null, cancellationToken);

    public async Task<Findings> ScanAsync(IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var days = _retention();
        if (days is < 1 or > 30) days = 7;
        var analysis = await _analyzer.AnalyzeAsync(progress, cancellationToken).ConfigureAwait(false);
        var zones = analysis.Zones.Select(z => _personal?.Apply(z) ?? z).ToList();
        if (_additional is not null)
        {
            var additional = await _additional.ScanAsync(cancellationToken).ConfigureAwait(false);
            if (additional.Any(p => p.Definition.Id == CleanupZoneId.WindowsOld)) zones.RemoveAll(p => p.Definition.Id == CleanupZoneId.WindowsOld);
            zones.AddRange(additional);
        }
        return new CleanupFindings(analysis with
        { Zones = zones.Select(zone => zone with { QuarantineRetentionDays = days }).ToArray() });
    }

    public IReadOnlyList<IMaintenanceAction> GetActions(Findings findings) => GetActions(findings, null);

    public IReadOnlyList<IMaintenanceAction> GetActions(Findings findings, IProgress<CleanupProgress>? progress)
    {
        if (findings is not CleanupFindings cleanup)
            throw new ArgumentException("Les constats ne proviennent pas du module Nettoyage.", nameof(findings));
        return Array.AsReadOnly(cleanup.Analysis.Zones
            .Where(zone => zone.Definition.IsExecutable && zone.Definition.Classification is
                CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable)
            .Select(zone => (IMaintenanceAction)new CleanupMaintenanceAction(_sessionResults, zone, progress)).ToArray());
    }

    public CleanupSelectionAction Select(IReadOnlyList<CleanupZonePreview> selection, IProgress<CleanupProgress>? progress = null) =>
        new(_sessionResults, selection, progress);

    // Ces opérations ne produisent que des résultats/rapports ; aucune exécution de zone ici.
    public CleanupStepResult SkipZone(CleanupZonePreview preview) => _sessionResults.SkipZone(preview);
    public CleanupStepResult CancelZone(CleanupZonePreview preview) => _sessionResults.CancelZone(preview);
    public CleanupSessionReport CreateReport(DateTimeOffset startedAt, IReadOnlyList<CleanupStepResult> results,
        IReadOnlyList<string> errors) => _sessionResults.CreateReport(startedAt, results, errors);
}

public sealed class CleanupFindings : Findings
{
    public CleanupFindings(CleanupAnalysisReport analysis)
        : base(analysis.Zones.Select(zone => new MaintenanceFinding(zone.Definition.Id.ToString(),
                zone.Definition.DisplayName, zone.Definition.Description)),
            analysis.Zones.Where(zone => zone.Definition.IsExecutable &&
                    zone.Definition.Classification is CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable)
                .Select(zone => CleanupActionPreviewFactory.Create(zone))
                .Select(preview => new ProposedMaintenanceAction(preview.ActionId, preview.Title, preview.RiskLevel,
                    preview.RequiresAdmin, preview.RebootRequired, preview.Reversibility)))
    {
        Analysis = Snapshot(analysis);
    }

    public CleanupAnalysisReport Analysis { get; }

    private static CleanupAnalysisReport Snapshot(CleanupAnalysisReport analysis) => analysis with
    {
        Zones = Array.AsReadOnly(analysis.Zones.Select(CleanupMaintenanceSnapshot.Copy).ToArray()),
        Storage = analysis.Storage with
        {
            Items = Array.AsReadOnly(analysis.Storage.Items.ToArray()),
            SkippedRoots = Array.AsReadOnly(analysis.Storage.SkippedRoots.ToArray()),
            Errors = Array.AsReadOnly(analysis.Storage.Errors.ToArray())
        },
        TechnicalBlocks = Array.AsReadOnly(analysis.TechnicalBlocks.ToArray()),
        Errors = Array.AsReadOnly(analysis.Errors.ToArray())
    };
}

internal static class CleanupMaintenanceSnapshot
{
    internal static CleanupZonePreview Copy(CleanupZonePreview preview)
    {
        return preview with
        {
            Definition = preview.Definition with
            {
                RegistryKeys = preview.Definition.RegistryKeys.Count == 0 ? preview.Definition.RegistryKeys : Array.AsReadOnly(preview.Definition.RegistryKeys.ToArray()),
                AllowedExtensions = Array.AsReadOnly(preview.Definition.AllowedExtensions.ToArray()),
                RequiredPathFragments = Array.AsReadOnly(preview.Definition.RequiredPathFragments.ToArray()),
                ExcludedPathFragments = Array.AsReadOnly(preview.Definition.ExcludedPathFragments.ToArray())
            },
            Candidates = Array.AsReadOnly(preview.Candidates.ToArray()),
            Errors = Array.AsReadOnly(preview.Errors.ToArray())
        };
    }
}
