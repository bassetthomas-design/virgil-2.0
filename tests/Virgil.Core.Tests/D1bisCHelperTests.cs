using System.Runtime.InteropServices;
using System.Text.Json;
using Virgil.Contracts;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Safety")]
public sealed class D1bisCHelperTests
{
    private const string Session = "f813d575-a123-4788-b34a-d547c15c5663";
    private const string MicrosoftUpdate = "7971f918-a847-4430-9279-4a52d1efe18d";
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Drivers_keep_search_download_catalogue_and_three_passes_but_install_one_at_a_time()
    {
        var agent = new Agent { AlwaysOffered = true };
        var result = await Run(agent);
        Assert.Equal(3, agent.Searches.Count);
        Assert.Equal(3, agent.Downloads.Count);
        Assert.Equal(6, agent.Installations.Count);
        Assert.All(agent.Installations, batch => Assert.Single(batch));
        Assert.All(agent.Downloads, batch => Assert.Equal(2, batch.Count));
        Assert.All(agent.Searches, query =>
        {
            Assert.Equal("IsInstalled=0 and IsHidden=0 and Type='Driver'", query.Criteria);
            Assert.Equal(3, query.Selection);
            Assert.Equal(MicrosoftUpdate, query.Service);
        });
        var cards = ReadCards(result.Message);
        Assert.Contains(cards, c => c.Identity == "first" && c.Title == agent.First.Title);
        Assert.Contains(cards, c => c.Identity == "second" && c.Title == agent.Second.Title);
    }

    [Theory]
    [InlineData("software")]
    [InlineData("windows-feature-upgrade")]
    public async Task Software_and_feature_upgrade_keep_their_original_grouped_installation(string tokens)
    {
        var agent = new Agent { Feature = tokens == "windows-feature-upgrade" };
        await Run(agent, tokens: tokens);
        Assert.Equal(2, Assert.Single(agent.Installations).Count);
        Assert.Equal(2, Assert.Single(agent.Downloads).Count);
    }

    [Fact]
    public async Task Each_driver_retains_its_actual_result_even_when_a_later_driver_fails()
    {
        var agent = new Agent { FailSecond = true };
        var result = await Run(agent);
        Assert.False(result.Success);
        var cards = ReadCards(result.Message);
        var first = Assert.Single(cards, c => c.Identity == "first");
        var second = Assert.Single(cards, c => c.Identity == "second");
        Assert.Equal("mis à jour", first.Status);
        Assert.Equal("en erreur", second.Status);
        Assert.Equal(agent.First.Title, first.Title);
        Assert.Equal(agent.Second.Title, second.Title);
        Assert.DoesNotContain("terminé", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mis à jour", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(second.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_first_install_finishes_that_call_and_never_starts_second(bool fails)
    {
        using var cancel = new CancellationTokenSource();
        var agent = new Agent { BlockFirst = true };
        var pending = Run(agent, cancel.Token);
        await agent.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        Assert.False(pending.IsCompleted);
        agent.Finish.SetResult(new(fails ? 0 : 1, true, !fails));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Reboot);
        Assert.Equal(new[] { "first" }, Assert.Single(agent.Installations).Select(i => i.Identity));
        Assert.Single(agent.Searches);
        var cards = ReadCards(result.Message);
        Assert.Equal(fails ? "en erreur" : "mis à jour", Assert.Single(cards, c => c.Identity == "first").Status);
        Assert.Equal("non lancée", Assert.Single(cards, c => c.Identity == "second").Status);
    }

    [Fact]
    public async Task Cancellation_after_download_before_first_install_marks_every_driver_unstarted()
    {
        using var cancel = new CancellationTokenSource();
        var agent = new Agent { AfterDownload = cancel.Cancel };
        var result = await Run(agent, cancel.Token);
        Assert.Empty(agent.Installations);
        Assert.Single(agent.Downloads);
        var cards = ReadCards(result.Message);
        Assert.Equal(2, cards.Count);
        Assert.All(cards, c => Assert.Equal("non lancée", c.Status));
        Assert.Equal(new[] { "first", "second" }, cards.Select(c => c.Identity));
    }

    [Fact]
    public async Task Restart_related_failure_of_next_driver_is_local_and_does_not_relabel_first()
    {
        var agent = new Agent { ThrowSecond = true };
        var result = await Run(agent);
        Assert.False(result.Success);
        var cards = ReadCards(result.Message);
        Assert.Equal("mis à jour", Assert.Single(cards, c => c.Identity == "first").Status);
        var second = Assert.Single(cards, c => c.Identity == "second");
        Assert.Contains(second.Status, new[] { "en erreur", "reste à installer" });
        Assert.NotEmpty(second.Message);
        Assert.DoesNotContain("COM", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Task<(bool Success, bool Reboot, string Message)> Run(Agent agent, CancellationToken token = default, string tokens = "drivers") =>
        UpdateMaintenanceRunner.ExecuteAsync(new() { ActionId = InterventionId.InstallMicrosoftUpdates, UpdateIdentity = tokens,
            RestoreSessionId = Session, RestorePointSequenceNumber = 12 }, new ReadOnlyRestore(), agent, token, timeProvider: new Clock());

    // Le protocole doit porter des résultats par identité, et non le seul total de la source.
    private sealed record Card(string Identity, string Title, string Status, string Message);
    private static IReadOnlyList<Card> ReadCards(string output) => output.Split('\n')
        .Where(line => line.StartsWith("VIRGIL|driver-item|RESULT|", StringComparison.Ordinal))
        .Select(line => JsonSerializer.Deserialize<Card>(line.Split('|', 4)[3])!).ToArray();

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class ReadOnlyRestore : IElevatedCommandRunner
    {
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command)
        {
            Assert.Contains("Get-ComputerRestorePoint", command.Arguments.Last());
            Assert.DoesNotContain("Checkpoint-Computer", command.Arguments.Last());
            return Task.FromResult(new ElevatedCommandResult(0, JsonSerializer.Serialize(new[] {
                new { SequenceNumber = 12, Description = "Virgil mises à jour " + Session, CreationTime = Now.AddHours(-1) } }), ""));
        }
    }
    private sealed class Item(string identity, string title) : IWindowsUpdateItem
    {
        public string Identity => identity;
        public string Title => title;
        public bool IsFeatureUpgrade { get; set; }
        public bool EulaAccepted { get; private set; }
        public bool IsDownloaded { get; set; }
        public void AcceptEula() => EulaAccepted = true;
    }
    private sealed class Agent : IWindowsUpdateAgent
    {
        public Item First { get; } = new("first", "NVIDIA - Display - 32.0.15.7216");
        public Item Second { get; } = new("second", "Intel - System - 10.1.1.38");
        public bool AlwaysOffered { get; init; }
        public bool Feature { get; init; }
        public bool BlockFirst { get; init; }
        public bool FailSecond { get; init; }
        public bool ThrowSecond { get; init; }
        public Action? AfterDownload { get; init; }
        public List<(string Criteria, int Selection, string? Service)> Searches { get; } = [];
        public List<IReadOnlyList<Item>> Installations { get; } = [];
        public List<IReadOnlyList<Item>> Downloads { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WindowsUpdateInstallResult> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsServiceRegistered(string serviceId) { Assert.Equal(MicrosoftUpdate, serviceId); return true; }
        public void AddService(string serviceId, int flags) => throw new InvalidOperationException("Catalogue déjà enregistré.");
        public Task<IReadOnlyList<IWindowsUpdateItem>> SearchAsync(string criteria, int serverSelection, string? serviceId)
        {
            Searches.Add((criteria, serverSelection, serviceId));
            First.IsFeatureUpgrade = Second.IsFeatureUpgrade = Feature;
            return Task.FromResult<IReadOnlyList<IWindowsUpdateItem>>(AlwaysOffered || Searches.Count == 1 ? [First, Second] : []);
        }
        public Task DownloadAsync(IReadOnlyList<IWindowsUpdateItem> updates)
        {
            var items = updates.Cast<Item>().ToArray();
            Downloads.Add(items);
            foreach (var item in items) { Assert.True(item.EulaAccepted); item.IsDownloaded = true; }
            AfterDownload?.Invoke();
            return Task.CompletedTask;
        }
        public Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<IWindowsUpdateItem> updates)
        {
            var items = updates.Cast<Item>().ToArray();
            Assert.All(items, i => Assert.True(i.IsDownloaded));
            Installations.Add(items);
            Started.TrySetResult();
            if (BlockFirst && Installations.Count == 1) return Finish.Task;
            if (ThrowSecond && items.Contains(Second)) throw new COMException();
            var failed = FailSecond && items.Contains(Second);
            return Task.FromResult(new WindowsUpdateInstallResult(failed ? 0 : items.Length, false, !failed));
        }
    }
}
