using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Virgil.App;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Actions;
using Virgil.Core.Diagnostic;
using Virgil.Core.Reports;
using Xunit;
using static Virgil.Tests.TestRepositoryRoot;

namespace Virgil.Tests;

public sealed class L6AppBehaviorTests
{
    [Fact]
    public async Task Last_report_is_available_while_release_check_never_completes()
    {
        var history = new History();
        var network = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var networkStarted = false;
        var enabled = false;
        var local = StartupInitialization.StartAsync(async () =>
        {
            enabled = await history.GetLatestAsync(CancellationToken.None) is not null;
        }, () => { networkStarted = true; return network.Task; });
        Assert.True(networkStarted);
        history.Finish.SetResult(new ReportEntry { Title = "Rapport existant" });
        await local.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(enabled);
        Assert.False(network.Task.IsCompleted);
    }

    [Theory]
    [InlineData(false, "Note de santé : 100/100")]
    [InlineData(true, "Note de santé : 100/100 — partielle (1 lecture en échec)")]
    public void Health_text_counts_only_failed_reads(bool fail, string expected)
    {
        var source = new Source();
        var vm = new DiagnosticViewModel(new DiagnosticModule(new DiagnosticSampler(source, TimeProvider.System)), TimeProvider.System);
        var snapshot = DiagnosticSampler.Calculate(source.Reading, source.Reading, 1, DateTimeOffset.UnixEpoch);
        var signals = new DiagnosticSignals(DiagnosticValue<WindowsErrorSummary>.Available(new(0, [])),
            DiagnosticValue<IReadOnlyList<DeviceProblem>>.Available([]), DiagnosticValue<IReadOnlyList<StartupEntry>>.Available([]),
            fail ? DiagnosticSignals.Unavailable.Processes : DiagnosticValue<IReadOnlyList<ProcessResourceInfo>>.Available([]));
        vm.ApplyFindings(DiagnosticAnalyzer.Analyze(snapshot, signals));
        Assert.Equal(expected, vm.Health);
    }

    [Fact]
    public void Cleanup_has_no_fake_mark_button_or_command()
    {
        var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(FindRepositoryRoot(), "src", "Virgil.App", "Views", "CleanupView.xaml"));
        var document = System.Xml.Linq.XDocument.Parse(xaml);
        var buttons = document.Descendants().Where(node => node.Name.LocalName == "Button");
        Assert.DoesNotContain(buttons, button => button.Attributes().Any(attribute =>
            attribute.Value.Contains("Marquer", StringComparison.OrdinalIgnoreCase) || attribute.Value.Contains("MarkCommand", StringComparison.Ordinal)));
        // Le parcours fonctionnel « À revoir manuellement » reste disponible ; la carte ne propose plus de faux marquage.
        var reviewCard = Assert.Single(document.Descendants(), node => node.Name.LocalName == "DataTemplate" &&
            node.Attributes().Any(attribute => attribute.Value.Contains("CleanupReviewItemViewModel", StringComparison.Ordinal)));
        Assert.DoesNotContain(reviewCard.Descendants().Where(node => node.Name.LocalName == "Button"), button =>
            button.Attributes().Any(attribute => attribute.Value.Contains("revoir", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(typeof(CleanupReviewItemViewModel).GetProperties(), p => p.Name.StartsWith("Mark", StringComparison.Ordinal));
    }

    [Fact, Trait("Category", "Windows"), Trait("Category", "Safety")]
    public Task Escape_closes_diagnostic_validation_even_when_process_grid_has_focus() => ThemeContrastTests.OnStaWithin(TimeSpan.FromSeconds(5), async deadline =>
    {
        var clock = new DiagnosticClock();
        var authority = new ConfirmationService(clock);
        var vm = new DiagnosticViewModel(new DiagnosticModule(new DiagnosticSampler(new Source(), clock)), clock, authority);
        var view = new DiagnosticView(vm);
        var window = new Window { Content = view, Width = 1280, Height = 900, ShowInTaskbar = false };
        try
        {
            var initialPresentation = vm.PresentationTask;
            window.Show();
            await WpfConditionWait.UntilAsync(() =>
            {
                clock.AdvanceIfArmed();
                return vm.PresentationTask != initialPresentation && vm.PresentationTask.IsCompleted;
            }, "chargement de la vue et présentation du diagnostic terminée",
                () => $"vue chargée : {view.IsLoaded}, présentation démarrée : {vm.PresentationTask != initialPresentation}, " +
                    $"tâche : {vm.PresentationTask.Status}, minuteurs : {clock.ArmedCount}", deadline);
            await vm.PresentationTask;
            var action = new RecordingAction();
            await WpfConditionWait.CompletionAsync(vm.PrepareActionAsync(action), "ouverture de la confirmation", deadline);
            window.UpdateLayout();
            var grid = Descendants<DataGrid>(view).Single();
            grid.ScrollIntoView(grid.Items[0]);
            grid.UpdateLayout();
            var cell = Descendants<DataGridCell>(grid).First();
            cell.BringIntoView();
            Assert.True(cell.Focus());
            Assert.True(cell.IsKeyboardFocusWithin);
            // Un contrôle qui consomme la phase à bulles ne doit pas bloquer le tunnel.
            grid.KeyDown += (_, e) => e.Handled = true;
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            cell.RaiseEvent(key);
            Assert.True(key.Handled);
            Assert.False(vm.IsValidationOpen);
            await WpfConditionWait.CompletionAsync(vm.ExecuteDecisionCommand.ExecuteAsync(null), "traitement de la décision", deadline);
            Assert.Equal(0, action.Calls);
            // Sans overlay visible, Échap n'est pas intercepté.
            var next = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            cell.RaiseEvent(next);
            Assert.False(next.Handled);
        }
        finally { window.Close(); }
    });

    private sealed class DiagnosticClock : TimeProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<SampleTimer> timers = new();
        private readonly DateTimeOffset start = DateTimeOffset.UtcNow;
        private long ticks;

        public int ArmedCount => timers.Count;
        public override DateTimeOffset GetUtcNow() => start.AddTicks(GetTimestamp());
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new SampleTimer(callback, state, dueTime);
            timers.Enqueue(timer);
            return timer;
        }

        public void AdvanceIfArmed()
        {
            if (!timers.TryDequeue(out var timer)) return;
            Interlocked.Add(ref ticks, timer.DueTime.Ticks);
            timer.Fire();
        }

        private sealed class SampleTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            private TimerCallback? pending = callback;
            public TimeSpan DueTime => dueTime;
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Fire() => Interlocked.Exchange(ref pending, null)?.Invoke(state);
            public void Dispose() => Interlocked.Exchange(ref pending, null);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class RecordingAction : IMaintenanceAction
    {
        public int Calls;
        public string Id => "test.close";
        public RiskLevel RiskLevel => RiskLevel.Low;
        public bool RequiresAdmin => false;
        public bool RebootRequired => false;
        public Reversibility Reversibility => Reversibility.None;
        public Task<ActionPreview> PreviewAsync(CancellationToken token) => Task.FromResult(new ActionPreview(Id, "Fermer", RiskLevel, ["Steam"], new Dictionary<string, string>(), [], false, false, Reversibility, []));
        public Task<ActionResult> ExecuteAsync(ConfirmedPlan? plan, CancellationToken token)
        { Calls++; return Task.FromResult(new ActionResult(Id, true, false, null, [], [], [])); }
    }
    private sealed class Source : IDiagnosticSource
    {
        private static DiagnosticValue<T> V<T>(T value) => DiagnosticValue<T>.Available(value);
        private static DiagnosticCounter Gauge(double value) => new(V(value), V(1d), DiagnosticCounterKind.Gauge);
        public DiagnosticReading Reading { get; } = new(new(Gauge(1), [], V(3000d), V(3000d), DiagnosticValue<double>.Unmeasured("Capteur absent")),
            new(V(16d), V(8d), V(1d), V(1d), V(8d), V(8d)), [], [], [],
            [new("steam", 42, "stable", Gauge(0), V(1d), V(1d), Gauge(0), V(0), V("Valve"), V(@"C:\Games\steam.exe"))]);
        public Task<DiagnosticReading> ReadAsync(CancellationToken token) => Task.FromResult(Reading);
    }
    private sealed class History : IReportHistoryService
    {
        public TaskCompletionSource<ReportEntry?> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ReportEntry?> GetLatestAsync(CancellationToken token) => Finish.Task;
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken token) => throw new NotSupportedException();
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
}
