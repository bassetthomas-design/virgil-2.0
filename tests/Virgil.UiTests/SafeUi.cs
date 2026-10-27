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
        // Decision of 27/10: run the complete read-only scan without cancellation.
        "DeepScanButton"
    };

    public Task Click(string automationId)
    {
        if (!ReadOnlyAllowlist.Contains(automationId))
            throw new InvalidOperationException($"Interaction UI interdite : {automationId}");

        return invokeByAutomationId(automationId);
    }
}
