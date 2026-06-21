using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public static class CleanupZoneCatalog
{
    public static IReadOnlyList<CleanupZoneDefinition> CreateDefault()
    {
        var local = Folder(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Folder(Environment.SpecialFolder.ApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return new[]
        {
            Zone(CleanupZoneId.UserTemporaryFiles, "TEMP utilisateur", "Fichiers temporaires anciens du profil utilisateur.", SafeTemp(), 24, CleanupRiskLevel.Low, 10),
            Zone(CleanupZoneId.WindowsTemporaryFiles, "TEMP Windows", "Contenu temporaire Windows ancien. Le dossier racine n'est jamais supprime.", Path.Combine(windows, "Temp"), 48, CleanupRiskLevel.Medium, 20) with { RequiresElevation = true },
            Zone(CleanupZoneId.WindowsThumbnailCache, "Miniatures Windows", "Bases de miniatures recreees par Windows.", Path.Combine(local, "Microsoft", "Windows", "Explorer"), 168, CleanupRiskLevel.Low, 30) with { AllowedExtensions = new[] { ".db" } },
            Zone(CleanupZoneId.DirectXShaderCache, "Cache DirectX", "Shaders temporaires recrees par les applications graphiques.", Path.Combine(local, "D3DSCache"), 168, CleanupRiskLevel.Low, 40),
            Zone(CleanupZoneId.UserCrashDumps, "Crash dumps anciens", "Dumps utilisateur anciens utiles seulement au diagnostic.", Path.Combine(local, "CrashDumps"), 168, CleanupRiskLevel.Medium, 50) with { AllowedExtensions = new[] { ".dmp", ".mdmp", ".tmp" } },
            Zone(CleanupZoneId.WindowsErrorReports, "Rapports d'erreur Windows", "Archives WER anciennes du profil utilisateur.", Path.Combine(local, "Microsoft", "Windows", "WER", "ReportArchive"), 336, CleanupRiskLevel.Medium, 60),
            Zone(CleanupZoneId.TechnicalLogs, "Logs techniques anciens", "Journaux non critiques d'applications connues.", Path.Combine(local, "Battle.net", "Logs"), 720, CleanupRiskLevel.Medium, 70) with { AllowedExtensions = new[] { ".log", ".txt" } },
            Zone(CleanupZoneId.BattleNetCache, "Cache Battle.net", "Cache utilisateur Battle.net clairement identifie.", Path.Combine(local, "Battle.net", "Cache"), 168, CleanupRiskLevel.Low, 80),
            Zone(CleanupZoneId.VisualStudioCache, "Cache Visual Studio", "Cache de composants et telemetrie recreable.", Path.Combine(local, "Microsoft", "VisualStudio", "ComponentModelCache"), 168, CleanupRiskLevel.Low, 90),
            Zone(CleanupZoneId.InternetCache, "Cache Internet Windows", "Cache INetCache du profil utilisateur.", Path.Combine(local, "Microsoft", "Windows", "INetCache"), 168, CleanupRiskLevel.Low, 100),

            Advanced(CleanupZoneId.RecycleBin, "Corbeille", "Fichiers places volontairement dans la corbeille. Ils ne seront plus recuperables depuis Windows.", "::{RecycleBin}", 0, 200, executable: true),
            Advanced(CleanupZoneId.WindowsUpdateCache, "Cache Windows Update", "Detection de la zone SoftwareDistribution. Action non exposee sans orchestration fiable des services.", Path.Combine(windows, "SoftwareDistribution", "Download"), 168, 210, executable: false, elevation: true),
            Advanced(CleanupZoneId.DeliveryOptimizationCache, "Cache Delivery Optimization", "Detection du cache Microsoft Delivery Optimization.", Path.Combine(programData, "Microsoft", "Windows", "DeliveryOptimization", "Cache"), 168, 220, executable: false, elevation: true),
            Advanced(CleanupZoneId.MicrosoftStoreCache, "Cache Microsoft Store", "Detection uniquement : aucune suppression destructive sans API fiable.", Path.Combine(local, "Packages"), 168, 230, executable: false),
            Browser(CleanupZoneId.BrowserEdgeCache, "Cache Edge", Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"), 240),
            Browser(CleanupZoneId.BrowserChromeCache, "Cache Chrome", Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), 250),
            Browser(CleanupZoneId.BrowserFirefoxCache, "Cache Firefox", Path.Combine(local, "Mozilla", "Firefox", "Profiles"), 260) with { RequiredPathFragments = new[] { "cache2\\", "startupCache\\", "thumbnails\\" } },
            Browser(CleanupZoneId.BrowserBraveCache, "Cache Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"), 270),
            Browser(CleanupZoneId.BrowserOperaCache, "Cache Opera", Path.Combine(roaming, "Opera Software", "Opera Stable", "Cache"), 280),
            Advanced(CleanupZoneId.InstallerTemporaryFiles, "Restes temporaires d'installation", "Fichiers d'installation temporaires clairement identifies.", Path.Combine(SafeTemp(), "VirgilInstallerCache"), 168, 290, executable: true),
            Advanced(CleanupZoneId.WindowsOld, "Windows.old", "Ancienne installation Windows detectee. Suppression deleguee aux outils Windows.", Path.Combine(Path.GetPathRoot(windows) ?? "C:\\", "Windows.old"), 720, 300, executable: false, elevation: true),

            Information(CleanupZoneId.PrefetchInformation, "Prefetch", "Gere par Windows, nettoyage non recommande en routine.", Path.Combine(windows, "Prefetch"), 400)
        };
    }

    private static CleanupZoneDefinition Zone(CleanupZoneId id, string name, string description, string root, int minimumAgeHours, CleanupRiskLevel risk, int order)
    {
        return new CleanupZoneDefinition(
            id, name, description, root, TimeSpan.FromHours(minimumAgeHours), risk,
            "Les fichiers recents, personnels, ambigus, verrouilles et points de reanalyse restent exclus.",
            "Suppression definitive des seuls fichiers techniques eligibles.",
            "La racine, les donnees personnelles, profils, applications, cookies, sessions et favoris.", order)
        {
            Classification = CleanupClassification.Cleanable
        };
    }

    private static CleanupZoneDefinition Advanced(CleanupZoneId id, string name, string description, string root, int minimumAgeHours, int order, bool executable, bool elevation = false)
    {
        return Zone(id, name, description, root, minimumAgeHours, CleanupRiskLevel.Medium, order) with
        {
            Classification = CleanupClassification.AdvancedCleanable,
            RequiresReinforcedConfirmation = true,
            IsExecutable = executable,
            RequiresElevation = elevation,
            Warning = executable
                ? "Confirmation renforcee obligatoire. Cette zone peut deconnecter des caches ou rendre des fichiers irrecuperables."
                : "Information seulement : aucune action destructive n'est exposee tant qu'une implementation fiable n'est pas disponible."
        };
    }

    private static CleanupZoneDefinition Browser(CleanupZoneId id, string name, string root, int order)
    {
        return Advanced(id, name,
            "Cache navigateur uniquement. Mots de passe, favoris, historique, cookies, sessions, profils et extensions sont proteges.",
            root, 24, order, executable: true) with
        {
            ExcludedPathFragments = new[] { "Login Data", "Bookmarks", "History", "Cookies", "Sessions", "Extensions", "Web Data" }
        };
    }

    private static CleanupZoneDefinition Information(CleanupZoneId id, string name, string description, string root, int order)
    {
        return Zone(id, name, description, root, 0, CleanupRiskLevel.Low, order) with
        {
            Classification = CleanupClassification.InformationOnly,
            IsExecutable = false,
            Warning = "Information uniquement. Aucun nettoyage n'est propose."
        };
    }

    private static string Folder(Environment.SpecialFolder folder)
    {
        try { return Environment.GetFolderPath(folder); }
        catch { return string.Empty; }
    }

    private static string SafeTemp()
    {
        try { return Path.GetTempPath(); }
        catch { return string.Empty; }
    }
}
