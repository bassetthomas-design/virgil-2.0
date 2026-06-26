using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationRiskClassifier
{
    private static readonly string[] ProtectedTerms =
    [
        "driver", "pilote", "nvidia", "amd software", "intel", "realtek", "qualcomm",
        "antivirus", "security", "securite", "vpn", "firewall", "pare-feu",
        "windows", "webview2", ".net", "runtime", "redistributable", "visual c++",
        "directx", "sdk", "framework", "chipset", "firmware", "defender"
    ];

    private static readonly string[] CautionTerms =
    [
        "adobe", "office", "visual studio", "android studio", "blender", "obs",
        "premiere", "photoshop", "after effects", "unity", "unreal", "autodesk",
        "itunes", "apple", "steam", "battle.net", "minecraft"
    ];

    private static readonly string[] SafeTerms =
    [
        "vlc", "discord", "7-zip", "7zip", "chrome", "brave", "firefox", "notepad++",
        "spotify", "zoom", "slack"
    ];

    public InstalledApplication Classify(InstalledApplication application)
    {
        var text = $"{application.DisplayName} {application.Publisher} {application.WingetId}".ToLowerInvariant();

        if (application.Source == ApplicationInventorySource.Store && ContainsAny(text, "microsoft", "windows", "framework"))
        {
            return WithRisk(application, ApplicationRiskLevel.Protected, "Package Store ou composant Windows protege en V1.", false);
        }

        if (ContainsAny(text, ProtectedTerms))
        {
            return WithRisk(application, ApplicationRiskLevel.Protected, "Composant systeme, pilote, runtime, securite ou framework protege.", false);
        }

        if (application.Source == ApplicationInventorySource.Store)
        {
            return WithRisk(application, ApplicationRiskLevel.Unknown, "Application Store en lecture seule en V1. Ouvrir les parametres Windows.", false);
        }

        if (application.UninstallKind == ApplicationUninstallKind.None || string.IsNullOrWhiteSpace(application.UninstallCommand) && string.IsNullOrWhiteSpace(application.WingetId))
        {
            return WithRisk(application, ApplicationRiskLevel.Unknown, "Desinstalleur officiel absent ou informations incompletes.", false);
        }

        if (ContainsAny(text, CautionTerms))
        {
            return WithRisk(application, ApplicationRiskLevel.Caution, "Application pouvant contenir profils, projets, presets, bibliotheques ou sauvegardes.", true);
        }

        if (ContainsAny(text, SafeTerms) || application.UninstallKind is ApplicationUninstallKind.Msi or ApplicationUninstallKind.RegistryUninstallString or ApplicationUninstallKind.Winget)
        {
            return WithRisk(application, ApplicationRiskLevel.SafeToUninstall, "Application utilisateur avec desinstalleur officiel identifiable.", true);
        }

        return WithRisk(application, ApplicationRiskLevel.Unknown, "Application insuffisamment identifiee.", false);
    }

    private static InstalledApplication WithRisk(
        InstalledApplication application,
        ApplicationRiskLevel risk,
        string reason,
        bool canUninstall)
    {
        return application with
        {
            RiskLevel = risk,
            RiskReason = reason,
            CanUninstall = canUninstall && risk != ApplicationRiskLevel.Protected,
            Status = risk switch
            {
                ApplicationRiskLevel.Protected => ApplicationStatus.Protected,
                ApplicationRiskLevel.Unknown when !canUninstall => ApplicationStatus.Unknown,
                _ => canUninstall ? ApplicationStatus.UninstallAvailable : ApplicationStatus.ReadOnly
            }
        };
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

