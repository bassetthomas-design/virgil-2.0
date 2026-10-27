using Xunit;

namespace Virgil.UiTests;

public sealed class SafeUiTests
{
    [Fact]
    [Trait("Category", "Safety")]
    public async Task Cleanup_summary_is_allowed_without_confirmation_or_execution()
    {
        var invoked = new List<string>();
        var ui = new SafeUi(id => { invoked.Add(id); return Task.CompletedTask; });
        await ui.Click("LaunchCleanupButton");
        Assert.Equal(new[] { "LaunchCleanupButton" }, invoked);
        foreach (var id in new[] { "ExecuteZoneButton", "Execute", "Confirmer" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => ui.Click(id));
        Assert.Single(invoked);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public async Task Click_refuses_any_automation_id_outside_the_read_only_allowlist()
    {
        var invoked = false;
        var ui = new SafeUi(_ => { invoked = true; return Task.CompletedTask; });
        string[] forbidden =
        [
            "", "UnknownButton", "homeNavButton", "HomeNavButton ",
            "ExecuteZoneButton", "ExecuteResourceActionButton",
            "ExecuteInterventionButton", "RunGuidedButton", "LaunchUninstallButton",
            "ExecuteUpdateButton", "UpdateAllButton", "PurgeQuarantineButton",
            "RestoreQuarantineButton", "ApplyStartupChangeButton", "Execute", "Confirmer"
        ];
        foreach (var id in forbidden.Concat(Enumerable.Range(0, 256).Select(i => $"Unknown{i}")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => ui.Click(id));
        Assert.False(invoked);
    }
}
