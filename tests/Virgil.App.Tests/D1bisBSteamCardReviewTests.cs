using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class D1bisBSteamCardReviewTests
{
    [Theory]
    [InlineData("partial", "reste à installer")]
    [InlineData("updated", "mis à jour")]
    [InlineData("current", "à jour selon Steam")]
    [InlineData("error", "en erreur")]
    public Task Steam_card_reflects_its_games_after_successful_restart(string scenario, string expected) => ThemeContrastTests.OnSta(async () =>
    {
        var backend = new Backend(scenario);
        var confirmation = new RecordingConfirmation();
        var runner = new UpdatesSequenceViewModelTests.Runner();
        var history = new UpdatesSequenceViewModelTests.History();
        var service = new UpdateSequenceService(backend, confirmation.Authority, new EmptyPendingRebootProbe(), new(backend, backend.SteamClock));
        var vm = new UpdatesViewModel(new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(runner), runner, confirmation.Authority),
            new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), runner, history, confirmation, service);
        var pending = vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in vm.SequenceChoices) choice.IsSelected = choice.Step.Component is UpdateComponent.Launcher or UpdateComponent.WingetFinal;
        vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(1, confirmation.Calls);
        Assert.Equal(1, backend.Launches);
        var steam = Assert.Single(vm.Operation.Steps, card => card.Key == "Launcher:Steam");
        var games = vm.Operation.Steps.Where(card => card.Key.StartsWith("SteamGame:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, games.Length);
        Assert.Equal(expected, steam.Status);
        Assert.DoesNotContain("à vérifier", steam.Status);
        Assert.Equal("à jour selon Steam", Assert.Single(games, game => game.Key == "SteamGame:30").Status);
        if (scenario == "partial")
        {
            Assert.Equal("mis à jour", Assert.Single(games, game => game.Key == "SteamGame:10").Status);
            Assert.Equal("reste à installer", Assert.Single(games, game => game.Key == "SteamGame:20").Status);
        }
        else if (scenario == "updated")
        {
            Assert.Contains(games, game => game.Status == "mis à jour");
            Assert.All(games, game => Assert.Contains(game.Status, new[] { "mis à jour", "à jour selon Steam" }));
        }
        else if (scenario == "current") Assert.All(games, game => Assert.Equal("à jour selon Steam", game.Status));
        else
        {
            Assert.Contains(games, game => game.Status == "en erreur");
            Assert.Contains(games, game => game.Status == "reste à installer");
        }
        Assert.Equal(vm.Operation.FinalSummary, history.Report!.SimpleView);
    });

    private sealed class Scan : IUpdateScanService
    {
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new WingetAvailability { IsAvailable = true } });
    }
    private sealed class Backend(string scenario) : IUpdateSequenceBackend, ILauncherShutdownBackend, ISteamUpdateBackend, ISteamClient
    {
        public ISteamClient SteamClient => this;
        public TimeProvider SteamClock { get; } = new Clock();
        public TimeSpan SteamWaitLimit => TimeSpan.FromSeconds(30);
        public int Launches { get; private set; }
        private bool _closed;
        private int _reads;
        public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["Steam"]);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) => Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Applications contrôlées."));
        public Task<LauncherSnapshot> ReadAsync(string name, CancellationToken token) => Task.FromResult(new LauncherSnapshot(true,
            _closed ? [] : [new(1, 0, "C:/Steam/steam.exe")], ["C:/Games"]));
        public Task CloseAsync(string executable, bool force, CancellationToken token) { _closed = true; return Task.CompletedTask; }
        public Task<SteamSession> ReadSessionAsync(CancellationToken token) => Task.FromResult(new SteamSession(true, "C:/Steam/steam.exe"));
        public Task<IReadOnlyList<SteamGameState>> ReadGamesAsync(CancellationToken token)
        {
            var poll = ++_reads > 1;
            var a = scenario == "current" ? new SteamGameState("10", "Game A", 4, "1")
                : new("10", "Game A", poll ? scenario == "error" ? 128u : 4u : 6u, poll ? "2" : "1");
            var b = new SteamGameState("20", "Game B", scenario is "partial" or "error" ? 6u : 4u, "1");
            return Task.FromResult<IReadOnlyList<SteamGameState>>([a, b, new("30", "Game C", 4, "1")]);
        }
        public Task<SteamExecutableCheck> CheckExecutableAsync(string? path, CancellationToken token) => Task.FromResult(new SteamExecutableCheck(true, ""));
        public Task<bool> LaunchSilentAsync(string path, CancellationToken token) { Launches++; return Task.FromResult(true); }
    }
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Add(ref _ticks, dueTime.Ticks);
            return new Timer(callback, state, TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
        }
    }
}
