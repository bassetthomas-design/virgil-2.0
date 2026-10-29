namespace Virgil.Core.Updates;

public static class LauncherCatalog
{
    public static IReadOnlyList<string> Detect(IEnumerable<string> installedNames) => installedNames
        .Select(CanonicalName).Where(name => name is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string? CanonicalName(string name) => name.Trim().ToUpperInvariant() switch
    {
        "STEAM" => "Steam",
        "EPIC GAMES" or "EPIC GAMES LAUNCHER" => "Epic Games",
        "UBISOFT CONNECT" or "UPLAY" => "Ubisoft Connect",
        "BATTLE.NET" or "BLIZZARD BATTLE.NET" => "Battle.net",
        _ => null
    };
}
