using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class D1bisBPanelTests
{
    [Theory]
    [InlineData("partial")]
    [InlineData("no-session")]
    [InlineData("cancelled")]
    public Task Real_panel_uses_own_results_and_displays_confirmation(string scenario) => ThemeContrastTests.OnSta(async () =>
    {
        var backend = new Backend { Session = scenario != "no-session" };
        var confirmation = new RecordingConfirmation();
        var history = new UpdatesSequenceViewModelTests.History();
        var runner = new UpdatesSequenceViewModelTests.Runner();
        var service = new UpdateSequenceService(backend, confirmation.Authority, new EmptyPendingRebootProbe(), new(backend, backend.SteamClock));
        var vm = new UpdatesViewModel(new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(runner), runner, confirmation.Authority),
            new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), runner, history, confirmation, service);
        if (scenario == "cancelled") backend.Cancel = vm.CancelActiveOperation;
        var pending = vm.RunAllCommand.ExecuteAsync(null);
        var choices = vm.SequenceChoices.Where(c => c.Step.Component == UpdateComponent.Launcher).ToArray();
        Assert.Equal(2, choices.Length);
        var steam = Assert.Single(choices, c => c.Step.Id == "Steam");
        var epic = Assert.Single(choices, c => c.Step.Id == "Epic Games");
        Assert.Contains(scenario == "no-session" ? "non pris en charge automatiquement" : "relancer en silencieux pour les jeux", steam.Title);
        Assert.Contains("Epic Games : fermer ; jeux non pris en charge automatiquement", epic.Title);
        Assert.All(choices, c => Assert.True(c.CanSelect));
        if (scenario == "partial") await Capture(vm, "confirmation");
        foreach (var choice in vm.SequenceChoices) choice.IsSelected = choice.Step.Component is UpdateComponent.Launcher or UpdateComponent.WingetFinal;
        vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(1, confirmation.Calls);
        var source = Assert.Single(vm.Operation.FinalSummary.Split('\n'), line => line.StartsWith("Lanceurs :", StringComparison.Ordinal));
        Assert.Equal("non pris en charge automatiquement", Assert.Single(vm.Operation.Steps, c => c.Key == "Launcher:Epic Games").Status);
        if (scenario == "no-session")
        {
            var card = Assert.Single(vm.Operation.Steps, c => c.Key == "Launcher:Steam");
            Assert.Equal("non pris en charge automatiquement", card.Status);
            Assert.Contains("Steam n’était pas connecté", card.Detail);
            Assert.Equal(0, backend.Launches);
        }
        else
        {
            Assert.Equal("mis à jour", Assert.Single(vm.Operation.Steps, c => c.Key == "SteamGame:10").Status);
            var remaining = Assert.Single(vm.Operation.Steps, c => c.Key == "SteamGame:20");
            Assert.Equal("reste à installer", remaining.Status);
            Assert.Contains("Steam continue en arrière-plan", remaining.Detail);
            Assert.Equal("à jour selon Steam", Assert.Single(vm.Operation.Steps, c => c.Key == "SteamGame:30").Status);
            Assert.Contains("1 mis à jour", source);
            Assert.Contains("1 reste à installer", source);
            Assert.Equal(1, backend.Launches);
            Assert.Equal(1, backend.Closes);
        }
        Assert.Contains("non pris en charge", source);
        Assert.DoesNotContain("Tout est à jour", vm.Operation.FinalSummary);
        Assert.Equal(vm.Operation.FinalSummary, history.Report!.SimpleView);
        Assert.All(vm.Operation.Steps.Where(c => c.Key.StartsWith("SteamGame:", StringComparison.Ordinal) || c.Key.StartsWith("Launcher:", StringComparison.Ordinal)), c =>
        {
            Assert.DoesNotContain("StateFlags", c.Title + c.Detail);
            Assert.DoesNotContain("buildid", c.Title + c.Detail);
            Assert.DoesNotContain("appmanifest", c.Title + c.Detail);
            Assert.DoesNotContain("winget", c.Title + c.Detail, StringComparison.OrdinalIgnoreCase);
        });
        await Capture(vm, scenario);
    });

    private static async Task Capture(UpdatesViewModel vm, string name)
    {
        var view = new UpdatesView(vm);
        var host = new Border { Child = view, Background = (Brush)Application.Current.FindResource("App.BackgroundBrush") };
        var window = new Window { Content = host, Width = 1600, Height = 1200, ShowInTaskbar = false, Background = host.Background };
        try
        {
            window.Show(); window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-d1bis-b");
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(stream);
        }
        finally { window.Close(); }
    }
    private sealed class Scan : IUpdateScanService
    {
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new WingetAvailability { IsAvailable = true } });
    }
    private sealed class Backend : IUpdateSequenceBackend, ILauncherShutdownBackend, ISteamUpdateBackend, ISteamClient
    {
        public ISteamClient SteamClient => this;
        public TimeProvider SteamClock { get; } = new Clock();
        public TimeSpan SteamWaitLimit => TimeSpan.FromSeconds(30);
        public bool Session { get; init; }
        public Action? Cancel { get; set; }
        public int Closes { get; private set; }
        public int Launches { get; private set; }
        private int _reads;
        public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["Steam", "Epic Games"]);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) => Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Applications contrôlées."));
        public Task<LauncherSnapshot> ReadAsync(string name, CancellationToken token) => Task.FromResult(new LauncherSnapshot(true,
            name == "Steam" && Closes == 0 ? [new(1, 0, "C:/Steam/steam.exe")] : [], ["C:/Games"]));
        public Task CloseAsync(string executable, bool force, CancellationToken token) { Closes++; return Task.CompletedTask; }
        public Task<SteamSession> ReadSessionAsync(CancellationToken token) => Task.FromResult(new SteamSession(Session, "C:/Steam/steam.exe"));
        public Task<IReadOnlyList<SteamGameState>> ReadGamesAsync(CancellationToken token)
        {
            var poll = ++_reads > 1;
            if (poll) Cancel?.Invoke();
            return Task.FromResult<IReadOnlyList<SteamGameState>>([new("10", "Game A", poll ? 4u : 6u, poll ? "2" : "1"), new("20", "Game B", 6, "1"), new("30", "Game C", 4, "1")]);
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
