using System.Collections.ObjectModel;
using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public static class CleanupSelectionPolicy
{
    // Une décision par zone connue ; une nouvelle valeur reste refusée tant qu'elle n'est pas revue.
    public static IReadOnlyDictionary<CleanupZoneId, bool> DefaultDecisions { get; } =
        new ReadOnlyDictionary<CleanupZoneId, bool>(new Dictionary<CleanupZoneId, bool>
        {
            [CleanupZoneId.UserTemporaryFiles] = true,
            [CleanupZoneId.UserCrashDumps] = true,
            [CleanupZoneId.DirectXShaderCache] = true,
            [CleanupZoneId.WindowsTemporaryFiles] = true,
            [CleanupZoneId.WindowsThumbnailCache] = true,
            [CleanupZoneId.WindowsErrorReports] = true,
            [CleanupZoneId.TechnicalLogs] = true,
            [CleanupZoneId.BattleNetCache] = true,
            [CleanupZoneId.VisualStudioCache] = true,
            [CleanupZoneId.InternetCache] = true,
            [CleanupZoneId.BrowserEdgeCache] = false,
            [CleanupZoneId.BrowserChromeCache] = false,
            [CleanupZoneId.BrowserFirefoxCache] = false,
            [CleanupZoneId.BrowserBraveCache] = false,
            [CleanupZoneId.BrowserOperaCache] = false,
            [CleanupZoneId.RecycleBin] = false,
            [CleanupZoneId.WindowsUpdateCache] = false,
            [CleanupZoneId.DeliveryOptimizationCache] = false,
            [CleanupZoneId.MicrosoftStoreCache] = false,
            [CleanupZoneId.InstallerTemporaryFiles] = false,
            [CleanupZoneId.WindowsOld] = false,
            [CleanupZoneId.PrefetchInformation] = false,
            [CleanupZoneId.OldDownloads] = false,
            [CleanupZoneId.SoftwareLeftovers] = false
        });

    public static bool CanSelect(CleanupZonePreview preview) =>
        DefaultDecisions.ContainsKey(preview.Definition.Id) && preview.Definition.Id != CleanupZoneId.WindowsOld &&
        preview.Definition.IsExecutable && preview.HasEligibleCandidates &&
        preview.Definition.Classification is CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable;

    public static bool CanGroupSelect(CleanupZonePreview preview) =>
        CanSelect(preview) && CleanupItemNature.For(preview) != CleanupNature.Personal;

    public static bool IsIncludedInSelectAll(CleanupZonePreview preview) =>
        CanGroupSelect(preview) || (preview.Definition.Id == CleanupZoneId.RecycleBin && CanSelect(preview));

    public static string? IneligibleReason(CleanupZonePreview preview)
    {
        if (CanSelect(preview)) return null;
        if (preview.Definition.Id == CleanupZoneId.RecycleBin && preview.EligibleFileCount == 0)
            return preview.Errors.Count > 0
                ? "Corbeille : estimation indisponible, vidage impossible depuis Virgil."
                : "Corbeille vide : rien à supprimer.";
        if (!DefaultDecisions.ContainsKey(preview.Definition.Id)) return "Zone non revue : sélection refusée.";
        if (preview.Definition.Id == CleanupZoneId.WindowsOld) return "Windows.old : information seulement, suppression non proposée.";
        if (!preview.Definition.IsExecutable) return "Zone en information seulement : aucune suppression possible.";
        if (preview.Definition.Classification is not (CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable))
            return "Zone en lecture seule : à revoir manuellement.";
        return "Aucun fichier éligible.";
    }

    public static bool IsSelectedByDefault(CleanupZonePreview preview) =>
        DefaultDecisions.TryGetValue(preview.Definition.Id, out var allowed) && allowed &&
        CanGroupSelect(preview) && preview.Definition.Group == CleanupGroup.Sur &&
        !preview.Definition.NeedsReview && !preview.Candidates.Any(c => c.NeedsReview);
}
