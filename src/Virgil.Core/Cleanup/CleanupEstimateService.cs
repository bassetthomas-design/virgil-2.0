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

    private static bool IsSafeGroup(CleanupZonePreview zone) => zone.Definition.Group == CleanupGroup.Sur &&
        !zone.Definition.IsPersonalFolder && !zone.Definition.NeedsReview &&
        !zone.Candidates.Any(candidate => candidate.IsPersonalFolder || candidate.NeedsReview);

    public static CleanupScanInfo Summarize(CleanupAnalysisReport analysis)
    {
        var zones = analysis.Zones.Where(z => z.EligibleBytes > 0 && z.HasEligibleCandidates && z.Definition.Id != CleanupZoneId.WindowsOld && z.Definition.IsExecutable && z.Definition.Classification is
            CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable).ToArray();
        var safe = zones.Where(IsSafeGroup).Sum(z => z.EligibleBytes);
        var verify = zones.Where(z => !IsSafeGroup(z)).Sum(z => z.EligibleBytes);
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
