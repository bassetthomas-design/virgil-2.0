using System;
using System.Collections.Generic;
using System.IO;
using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public static class CleanupZoneCatalog
{
    public static IReadOnlyList<CleanupZoneDefinition> CreateDefault()
    {
        var localAppData = GetLocalAppDataPath();

        return new[]
        {
            new CleanupZoneDefinition(
                CleanupZoneId.UserTemporaryFiles,
                "Temporaires utilisateur",
                "Fichiers temporaires du profil utilisateur.",
                GetUserTempPath(),
                TimeSpan.FromHours(24),
                CleanupRiskLevel.Low,
                "Les fichiers recents restent exclus.",
                "Suppression definitive des fichiers temporaires eligibles.",
                "Documents, telechargements, profils et donnees personnelles.",
                10),
            new CleanupZoneDefinition(
                CleanupZoneId.UserCrashDumps,
                "Crash dumps utilisateur",
                "Rapports de plantage locaux du profil utilisateur.",
                Path.Combine(localAppData, "CrashDumps"),
                TimeSpan.FromDays(7),
                CleanupRiskLevel.Medium,
                "Ces fichiers peuvent aider a diagnostiquer d'anciens plantages.",
                "Suppression definitive des dumps eligibles.",
                "Applications, documents et journaux systeme hors zone.",
                20),
            new CleanupZoneDefinition(
                CleanupZoneId.DirectXShaderCache,
                "Cache shaders DirectX",
                "Cache DirectX local recree automatiquement par les applications graphiques.",
                Path.Combine(localAppData, "D3DSCache"),
                TimeSpan.FromDays(7),
                CleanupRiskLevel.Low,
                "Les applications peuvent recreer ce cache au prochain lancement.",
                "Suppression definitive des shaders temporaires eligibles.",
                "Parametres graphiques, pilotes et donnees personnelles.",
                30)
        };
    }

    private static string GetUserTempPath()
    {
        try
        {
            return Path.GetTempPath();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetLocalAppDataPath()
    {
        try
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        catch
        {
            return string.Empty;
        }
    }
}
