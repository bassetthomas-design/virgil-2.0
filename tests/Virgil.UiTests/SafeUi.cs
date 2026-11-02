namespace Virgil.UiTests;

// No UI dependency: refusal is checked before even looking up an element.
internal sealed class SafeUi(Func<string, Task> invokeByAutomationId)
{
    private static readonly HashSet<string> ReadOnlyAllowlist = new(StringComparer.Ordinal)
    {
        "HomeNavButton", "ScanNavButton", "CleanupNavButton", "DiagnosticNavButton",
        "ResourcesNavButton", "ApplicationsNavButton", "UpdatesNavButton", "StartupNavButton",
        "NetworkNavButton", "InterventionsNavButton", "ReportsNavButton",
        "AnalyzeZonesButton", "CancelProtocolButton",
        "DeepScanButton", "ScanCancelButton",
        // Ouvre seulement le récapitulatif ; ExecuteZoneButton reste interdit.
        "LaunchCleanupButton",
        // Ouvre seulement l’aperçu ; ExecuteUpdateButton reste interdit.
        "RunSelectedButton", "SkipUpdateButton"
    };

    public Task Click(string automationId)
    {
        if (!ReadOnlyAllowlist.Contains(automationId))
            throw new InvalidOperationException($"Interaction UI interdite : {automationId}");

        return invokeByAutomationId(automationId);
    }
}

internal static class WingetSmokeSafety
{
    public static bool IsReadOnly(string commandLine)
    {
        var tokens = System.Text.RegularExpressions.Regex.Matches(commandLine, "\\\"[^\\\"]*\\\"|\\S+")
            .Select(match => match.Value.Trim('"')).ToArray();
        if (tokens.Length < 2) return false;
        var verb = tokens[1];
        if (verb is "list" or "--version") return true;
        if (verb != "upgrade") return false;
        // Sans cible ni --all, « upgrade » liste seulement les versions disponibles.
        string[] listingFlags = ["--include-unknown", "--include-pinned", "--accept-source-agreements", "--disable-interactivity"];
        return tokens.Skip(2).All(token => listingFlags.Contains(token, StringComparer.Ordinal));
    }
}
