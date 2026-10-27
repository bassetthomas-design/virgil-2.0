namespace Virgil.Core.Cleanup;

/// <summary>Ces caches se re-téléchargent ; leur présence ne prouve pas une désinstallation.</summary>
public static class DevelopmentCacheCatalog
{
    public static bool Contains(string directoryName) =>
        directoryName.Equals("npm-cache", StringComparison.OrdinalIgnoreCase) ||
        directoryName.Equals("NuGet", StringComparison.OrdinalIgnoreCase) ||
        directoryName.Equals("v3-cache", StringComparison.OrdinalIgnoreCase) ||
        directoryName.Equals("plugins-cache", StringComparison.OrdinalIgnoreCase) ||
        directoryName.Equals("pnpm-cache", StringComparison.OrdinalIgnoreCase) ||
        directoryName.Equals("pnpm", StringComparison.OrdinalIgnoreCase);
}
