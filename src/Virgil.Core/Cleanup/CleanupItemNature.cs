using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public enum CleanupNature { System, Personal, Cache, SoftwareLeftover }

public static class CleanupItemNature
{
    // Chaque valeur connue est explicite ; une valeur inconnue échoue au lieu de masquer une nouvelle zone.
    public static CleanupNature For(CleanupZoneId id) => id switch
    {
        CleanupZoneId.WindowsTemporaryFiles or CleanupZoneId.WindowsUpdateCache or
        CleanupZoneId.DeliveryOptimizationCache or CleanupZoneId.WindowsOld or
        CleanupZoneId.WindowsErrorReports or CleanupZoneId.TechnicalLogs or
        CleanupZoneId.InstallerTemporaryFiles or CleanupZoneId.PrefetchInformation => CleanupNature.System,
        CleanupZoneId.OldDownloads or CleanupZoneId.RecycleBin => CleanupNature.Personal,
        CleanupZoneId.UserTemporaryFiles or CleanupZoneId.UserCrashDumps or
        CleanupZoneId.DirectXShaderCache or CleanupZoneId.WindowsThumbnailCache or
        CleanupZoneId.BattleNetCache or CleanupZoneId.VisualStudioCache or
        CleanupZoneId.InternetCache or CleanupZoneId.BrowserEdgeCache or
        CleanupZoneId.BrowserChromeCache or CleanupZoneId.BrowserFirefoxCache or
        CleanupZoneId.BrowserBraveCache or CleanupZoneId.BrowserOperaCache or
        CleanupZoneId.MicrosoftStoreCache => CleanupNature.Cache,
        CleanupZoneId.SoftwareLeftovers => CleanupNature.SoftwareLeftover,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Zone de nettoyage inconnue.")
    };

    public static CleanupNature For(CleanupZonePreview preview) =>
        preview.Definition.IsPersonalFolder || preview.Candidates.Any(c => c.IsPersonalFolder)
            ? CleanupNature.Personal : For(preview.Definition.Id);

    public static string Label(CleanupNature nature) => nature switch
    {
        CleanupNature.System => "Système",
        CleanupNature.Personal => "Personnel",
        CleanupNature.Cache => "Cache",
        CleanupNature.SoftwareLeftover => "Reste de logiciel",
        _ => throw new ArgumentOutOfRangeException(nameof(nature))
    };
}
