namespace Virgil.Core.Updates;

public static class LauncherCatalog
{
    public static IReadOnlyDictionary<string, string> Executables { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Steam"] = "steam.exe", ["Battle.net"] = "Battle.net.exe", ["Epic Games"] = "EpicGamesLauncher.exe",
        ["EA app"] = "EADesktop.exe", ["Ubisoft Connect"] = "UbisoftConnect.exe", ["WeGame"] = "WeGame.exe"
    };
    private static readonly IReadOnlyDictionary<string, string[]> Helpers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Steam"] = ["steamwebhelper.exe", "steamservice.exe", "steamerrorreporter.exe", "crashhandler.exe", "crashhandler64.exe"],
        ["Epic Games"] = ["EpicWebHelper.exe", "UnrealCEFSubProcess.exe"],
        ["EA app"] = ["EADesktop.exe", "EABackgroundService.exe", "EALauncher.exe", "QtWebEngineProcess.exe", "CefSharp.BrowserSubprocess.exe"],
        ["Ubisoft Connect"] = ["UplayWebCore.exe", "QtWebEngineProcess.exe", "CefSharp.BrowserSubprocess.exe"],
        ["Battle.net"] = ["Agent.exe", "BlizzardError.exe", "QtWebEngineProcess.exe", "CefSharp.BrowserSubprocess.exe"],
        ["WeGame"] = ["QtWebEngineProcess.exe", "CefSharp.BrowserSubprocess.exe"]
    };
    public static bool IsKnownHelper(string launcherName, string executableName) =>
        Helpers.TryGetValue(launcherName, out var helpers) && helpers.Contains(executableName, StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<string> Detect(IEnumerable<string> installedNames) => installedNames
        .Select(CanonicalName).Where(name => name is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string? CanonicalName(string name) => name.Trim().ToUpperInvariant() switch
    {
        "STEAM" => "Steam",
        "EPIC GAMES" or "EPIC GAMES LAUNCHER" => "Epic Games",
        "UBISOFT CONNECT" or "UPLAY" => "Ubisoft Connect",
        "BATTLE.NET" or "BLIZZARD BATTLE.NET" => "Battle.net",
        "EA APP" or "EA DESKTOP" => "EA app",
        "WEGAME" => "WeGame",
        _ => null
    };
}
