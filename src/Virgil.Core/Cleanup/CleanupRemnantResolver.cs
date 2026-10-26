using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

/// <summary>Une racine composite ne donne jamais accès à un ancêtre commun plus large.</summary>
internal static class CleanupRemnantResolver
{
    internal static CleanupZoneDefinition? Resolve(CleanupZoneDefinition parent, string path)
    {
        if (parent.Id != CleanupZoneId.SoftwareLeftovers) return null;
        var matches = parent.RemnantZones.Where(z => z.Id == CleanupZoneId.SoftwareLeftovers &&
            z.RemnantZones.Count == 0 && z.SoftwareName == parent.SoftwareName &&
            z.ValidatedRemnantRoot is not null && z.RemnantAllowedRoot is not null &&
            string.Equals(z.RootPath, z.ValidatedRemnantRoot, StringComparison.OrdinalIgnoreCase) &&
            CleanupPathGuard.IsStrictlyUnderRoot(z.RootPath, z.RemnantAllowedRoot) &&
            CleanupPathGuard.IsStrictlyUnderRoot(path, z.RootPath)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
