using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public interface ICleanupEstimateService
{
    Task<CleanupFindings> EstimateAsync(IProgress<CleanupProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>Le scan et Nettoyage utilisent le même inventaire protégé, sans exécuter d'action.</summary>
public sealed class CleanupEstimateService(CleanupMaintenanceModule module) : ICleanupEstimateService
{
    public async Task<CleanupFindings> EstimateAsync(IProgress<CleanupProgress>? progress, CancellationToken cancellationToken) =>
        (CleanupFindings)await module.ScanAsync(progress, cancellationToken).ConfigureAwait(false);

    public static CleanupScanInfo Summarize(CleanupAnalysisReport analysis)
    {
        var zones = analysis.Zones.Where(z => z.Definition.IsExecutable && z.Definition.Classification is
            CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable).ToArray();
        var safe = zones.Where(z => z.Definition.Group == CleanupGroup.Sur).Sum(z => z.EligibleBytes);
        var verify = zones.Where(z => z.Definition.Group != CleanupGroup.Sur).Sum(z => z.EligibleBytes);
        return new CleanupScanInfo(true, safe + verify, zones.Sum(z => z.EligibleFileCount),
            analysis.Zones.Select(z => z.Definition.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            analysis.Errors.Concat(analysis.Zones.SelectMany(z => z.Errors)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            SafePotentialBytes = safe,
            AdvancedPotentialBytes = verify,
            ReviewItemCount = analysis.Storage.ReviewItemCount,
            ReviewItemBytes = analysis.Storage.ReviewBytes,
            InformationOnlyZoneCount = analysis.Zones.Count(z => !z.Definition.IsExecutable),
            PersonalDataProtected = true,
            ExecutedAnyAction = false
        };
    }
}
