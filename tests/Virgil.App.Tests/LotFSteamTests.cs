using System.Windows.Controls;
using Virgil.App.Controls;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotFSteamTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Real_sequence_and_Steam_only_share_game_detail_rule(bool single) => ThemeContrastTests.OnSta(async () =>
    {
        var backend = new Backend(); var confirmation = new RecordingConfirmation();
        var runner = new UpdatesSequenceViewModelTests.Runner();
        var sequence = new UpdateSequenceService(backend, confirmation.Authority, new EmptyPendingRebootProbe(), new(backend, backend.SteamClock));
        var vm = new UpdatesViewModel(new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(runner), runner, confirmation.Authority),
            new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), runner, new UpdatesSequenceViewModelTests.History(), confirmation, sequence);
        Task pending;
        if (single)
        {
            await vm.ScanUpdatesCommand.ExecuteAsync(null);
            var card = Assert.Single(vm.SingleCards, c => c.Step?.Id == "Steam");
            pending = D2SingleItemTests.Command(vm).ExecuteAsync(card.Step);
        }
        else
        {
            pending = vm.RunAllCommand.ExecuteAsync(null);
            foreach (var choice in vm.SequenceChoices) choice.IsSelected = choice.Step.Component is UpdateComponent.Launcher or UpdateComponent.WingetFinal;
        }
        vm.ExecuteDecisionCommand.Execute(null); await pending;
        foreach (var (id, status, detail) in new[] { ("10", "mis à jour", ""), ("20", "reste à installer", "Steam continue en arrière-plan."),
            ("30", "à jour selon Steam", ""), ("40", "en erreur", "Lecture du jeu impossible.") })
        {
            var card = Assert.Single(vm.SingleCards, c => c.Key == "SteamGame:" + id);
            Assert.Equal(status, card.Status); Assert.Equal(detail, card.Detail);
            Assert.DoesNotContain(status, detail, StringComparison.OrdinalIgnoreCase);
            if (!single)
            {
                var step = Assert.Single(vm.Operation.Steps, c => c.Key == "SteamGame:" + id);
                Assert.Equal(status, step.Status); Assert.Equal(detail, step.Detail);
            }
        }
        Assert.Equal(1, backend.Launches); Assert.Equal(1, backend.Closes);
        var panel = new OperationProgressPanel { DataContext = vm.Operation };
        if (!single) LotFRevealTests.Capture(panel, "steam-quatre-statuts", 1000, 1050);
        else
        {
            var cards = new ItemsControl { ItemsSource = vm.SingleCards.Where(c => c.Key.StartsWith("SteamGame:", StringComparison.Ordinal)).Select(c => c.Title + " — " + c.Status + "\n" + c.Detail).ToArray() };
            LotFRevealTests.Capture(cards, "steam-seul-quatre-statuts", 1000, 500);
        }
    });
    private sealed class Scan : IUpdateScanService
    {
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new() { IsAvailable = true } });
    }
    private sealed class Backend : IUpdateSequenceBackend, ILauncherShutdownBackend, ISteamUpdateBackend, ISteamClient
    {
        public ISteamClient SteamClient => this;
        public TimeProvider SteamClock { get; } = new Clock();
        public TimeSpan SteamWaitLimit => TimeSpan.FromSeconds(30);
        public int Closes { get; private set; }
        public int Launches { get; private set; }
        private int reads;
        public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["Steam"]);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) => Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Applications contrôlées."));
        public Task<LauncherSnapshot> ReadAsync(string name, CancellationToken token) => Task.FromResult(new LauncherSnapshot(true,
            Closes == 0 ? [new(1, 0, "C:/Steam/steam.exe")] : [], ["C:/Games"]));
        public Task CloseAsync(string executable, bool force, CancellationToken token) { Closes++; return Task.CompletedTask; }
        public Task<SteamSession> ReadSessionAsync(CancellationToken token) => Task.FromResult(new SteamSession(true, "C:/Steam/steam.exe"));
        public Task<IReadOnlyList<SteamGameState>> ReadGamesAsync(CancellationToken token)
        {
            var poll = ++reads > 1;
            return Task.FromResult<IReadOnlyList<SteamGameState>>([new("10", "Jeu A", poll ? 4u : 6u, poll ? "2" : "1"),
                new("20", "Jeu B", 6, "1"), new("30", "Jeu C", 4, "1"), new("40", "Jeu D", 6, "1") { Error = poll ? "Lecture du jeu impossible." : null }]);
        }
        public Task<SteamExecutableCheck> CheckExecutableAsync(string? path, CancellationToken token) => Task.FromResult(new SteamExecutableCheck(true, ""));
        public Task<bool> LaunchSilentAsync(string path, CancellationToken token) { Launches++; return Task.FromResult(true); }
    }
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Add(ref ticks, dueTime.Ticks);
            return new Timer(callback, state, TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
        }
    }
}
