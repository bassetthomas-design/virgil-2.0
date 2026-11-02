using Xunit;

namespace Virgil.UiTests;

public sealed class SafeUiTests
{
    [Theory]
    [Trait("Category", "Safety")]
    [InlineData("winget.exe list --id Safe.Package", true)]
    [InlineData("winget.exe --version", true)]
    [InlineData("winget.exe upgrade --include-unknown --include-pinned --accept-source-agreements --disable-interactivity", true)]
    [InlineData("winget.exe upgrade --all --silent", false)]
    [InlineData("winget.exe upgrade --id Safe.Package", false)]
    [InlineData("winget.exe upgrade Safe.Package", false)]
    [InlineData("winget.exe install Safe.Package", false)]
    [InlineData("winget.exe upgrade --include-unknown --force", false)]
    public void Winget_listing_is_allowed_but_installation_is_always_refused(string command, bool allowed)
        => Assert.Equal(allowed, WingetSmokeSafety.IsReadOnly(command));

    [Fact]
    [Trait("Category", "Safety")]
    public async Task Update_preview_is_allowed_without_confirmation_or_execution()
    {
        var invoked = new List<string>();
        var ui = new SafeUi(id => { invoked.Add(id); return Task.CompletedTask; });
        await ui.Click("RunSelectedButton");
        await ui.Click("SkipUpdateButton");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ui.Click("ExecuteUpdateButton"));
        Assert.Equal(new[] { "RunSelectedButton", "SkipUpdateButton" }, invoked);
    }

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
