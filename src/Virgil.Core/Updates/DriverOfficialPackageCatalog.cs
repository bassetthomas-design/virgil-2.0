using Virgil.Contracts;

namespace Virgil.Core.Updates;

// Source : microsoft/winget-pkgs, db1e2f3a72d88b016050a00b5071c94a2ef70243. Voir Lot-D1bis-c.md.
public static class DriverOfficialPackageCatalog
{
    public const string AmdCloud = "AMD.AMDSoftwareCloudEdition";
    public const string IntelAssistant = "Intel.IntelDriverAndSupportAssistant";
    // NVIDIA : aucun paquet de pilote graphique silencieux établi dans cette source.
    // AMD : pilotes Cloud Edition seulement, uniquement si proposés pour le paquet déjà présent.
    // Intel : l'assistant officiel n'installe pas les pilotes sans intervention ; jamais lancé ici.
    public static bool IsDriverPackage(UpdateItem item) => item.Id is AmdCloud or IntelAssistant
        || item.Source == UpdateSource.Driver || new[] { "driver", "chipset" }.Any(term => (item.Name + " " + item.Id).Contains(term, StringComparison.OrdinalIgnoreCase));
    public static bool IsFirmware(UpdateItem item) => new UpdateRiskClassifier().Classify(item).Level == UpdateRiskLevel.CriticalInformationOnly;
    public static bool CanInstall(DriverPackageOffer offer) => offer.Item.Id == AmdCloud && offer.InstallsDriversSilently
        && !offer.Item.IsIdTruncated && offer.Item.Source == UpdateSource.Winget && !IsFirmware(offer.Item)
        && offer.Item.RiskLevel != UpdateRiskLevel.CriticalInformationOnly;
    public static string Provider(string text) => text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
        : text.Contains("AMD", StringComparison.OrdinalIgnoreCase) || text.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) ? "AMD"
        : text.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : text.Trim();
}
