using Xunit;
using Xunit.Abstractions;

namespace Virgil.UiTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiCollection : ICollectionFixture<UiSession>
{
    public const string Name = "Application publiee — lecture seule";
}

[Collection(UiCollection.Name)]
[Trait("Category", "UI")]
public sealed class UiSmokeTests(UiSession session, ITestOutputHelper output)
{
    [Theory]
    [InlineData("accueil")]
    [InlineData("scan")]
    [InlineData("nettoyage")]
    [InlineData("diagnostic")]
    [InlineData("ressources")]
    [InlineData("applications")]
    [InlineData("mises-a-jour")]
    [InlineData("demarrage")]
    [InlineData("reseau")]
    [InlineData("reparation")]
    [InlineData("rapports")]
    public async Task Screen_loads_without_error(string screen)
    {
        await session.RunAsync();
        session.AssertScreen(screen);
        session.AssertHealthy();
    }

    [Fact]
    public async Task Ui_thread_stays_responsive_during_navigation_and_complete_scan()
    {
        await session.RunAsync();
        output.WriteLine($"Latence UI max : {session.MaximumLatency:F0} ms");
        output.WriteLine($"Latence UI médiane : {session.MedianLatency:F0} ms");
        foreach (var line in session.LatencyReport()) output.WriteLine(line);
        session.AssertResponsive();
        session.AssertHealthy();
    }

    [Fact]
    public async Task Cleanup_summary_is_captured_without_execution()
    {
        await session.RunAsync();
        session.AssertNoSideEffects();
        Assert.True(File.Exists(Path.Combine(SessionFiles.ArtifactDirectory, "12-recap-nettoyage.png")));
    }

    [Fact]
    public async Task Smoke_run_has_no_destructive_side_effect()
    {
        // The same complete journey is awaited whichever test xUnit schedules first.
        await session.RunAsync();
        session.AssertNoSideEffects();
    }
}
