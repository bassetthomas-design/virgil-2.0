using Xunit;

namespace Virgil.UiTests;

public sealed class SafeUiTests
{
    [Fact]
    [Trait("Category", "Safety")]
    public async Task Click_refuses_any_automation_id_outside_the_read_only_allowlist()
    {
        var invoked = false;
        var ui = new SafeUi(_ => { invoked = true; return Task.CompletedTask; });
        string[] forbidden =
        [
            "", "UnknownButton", "homeNavButton", "HomeNavButton ",
            "LaunchCleanupButton", "ExecuteZoneButton", "ExecuteResourceActionButton",
            "ExecuteInterventionButton", "RunGuidedButton", "LaunchUninstallButton",
            "ExecuteUpdateButton", "UpdateAllButton", "PurgeQuarantineButton",
            "RestoreQuarantineButton", "ApplyStartupChangeButton", "Execute", "Confirmer"
        ];
        foreach (var id in forbidden.Concat(Enumerable.Range(0, 256).Select(i => $"Unknown{i}")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => ui.Click(id));
        Assert.False(invoked);
    }
}
