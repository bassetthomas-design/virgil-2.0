using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Actions;
using Virgil.Core.Diagnostic;
using Virgil.Core.Status;
using Xunit;

namespace Virgil.Tests;

public sealed class LotDiagnosticChargeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test");
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private const string CpuDescription = "Charge totale d'au moins 85 % sur deux relevés consécutifs (97 % puis 92 %). Un jeu, un calcul ou une mise à jour peut l'expliquer.";
    private const string Recommendation = "Action proposée : regardez les programmes les plus gourmands dans la liste des processus, puis quittez ceux que vous n'utilisez pas.";
    private const string NoFinding = "Aucun problème détecté dans les signaux disponibles.";
    private const string PriorityCaption = "À vérifier en priorité";

    [Fact]
    public async Task Two_high_processor_readings_make_a_load_finding()
    {
        var clock = new StepClock();
        var source = new LoadSource { Cpu = 97 };
        var module = Module(source, clock);
        var first = await Scan(module);
        Assert.Empty(first.Issues);
        clock.Advance(TimeSpan.FromSeconds(30));
        source.Cpu = 92;
        var second = await Scan(module);
        Assert.Equal(new[] { "cpu.load" }, second.Issues.Select(i => i.Id));
        var issue = Assert.Single(second.Issues);
        Assert.Equal("Processeur : très sollicité", issue.Title);
        Assert.Equal(CpuDescription, issue.Description);
        Assert.Equal(2, issue.Severity);
        Assert.Equal(10, issue.Impact);
        Assert.Equal("load", issue.Category);
        Assert.Equal(DiagnosticSuggestion.None, issue.Suggestion);
        Assert.Null(issue.Process);
        Assert.Equal(new HealthScore(90, false), second.Score);
        Assert.Equal(new[] { "cpu.load" }, second.Top3.Select(i => i.Id));
        Assert.Equal(Hint("processeur 97 %"), Read<string>(first, "PendingLoad"));
        Assert.Equal(string.Empty, Read<string>(second, "PendingLoad"));
        Assert.Equal(4, source.Reads);
    }

    [Fact]
    public async Task Two_high_memory_readings_make_a_load_finding_and_share_the_load_weight()
    {
        var clock = new StepClock();
        var source = new LoadSource { MemoryAvailable = 1.6e9 };
        var module = Module(source, clock);
        var first = await Scan(module);
        clock.Advance(TimeSpan.FromSeconds(30));
        source.MemoryAvailable = 2.08e9;
        var second = await Scan(module);
        Assert.Equal(new[] { "memory.load" }, second.Issues.Select(i => i.Id));
        var issue = Assert.Single(second.Issues);
        Assert.Equal("Mémoire : presque pleine", issue.Title);
        Assert.Equal("Au moins 85 % de la mémoire utilisée sur deux relevés consécutifs (90 % puis 87 %). Des programmes ouverts en arrière-plan peuvent l'expliquer.", issue.Description);
        Assert.Equal(2, issue.Severity);
        Assert.Equal(10, issue.Impact);
        Assert.Equal("load", issue.Category);
        Assert.Equal(new HealthScore(90, false), second.Score);
        clock.Advance(TimeSpan.FromSeconds(30));
        source.Cpu = 95;
        var third = await Scan(module);
        Assert.Equal(new[] { "memory.load" }, third.Issues.Select(i => i.Id));
        clock.Advance(TimeSpan.FromSeconds(30));
        source.Cpu = 96;
        var fourth = await Scan(module);
        Assert.Equal(new[] { "cpu.load", "memory.load" }, fourth.Issues.Select(i => i.Id));
        Assert.Equal(new HealthScore(90, false), fourth.Score);
        Assert.Equal(Hint("mémoire 90 %"), Read<string>(first, "PendingLoad"));
        Assert.Equal(string.Empty, Read<string>(second, "PendingLoad"));
        Assert.Equal(Hint("processeur 95 %"), Read<string>(third, "PendingLoad"));
        Assert.Equal(string.Empty, Read<string>(fourth, "PendingLoad"));
    }

    [Fact]
    public async Task Load_card_recommends_and_opens_the_process_list()
    {
        var clock = new StepClock();
        var source = new LoadSource { Cpu = 97 };
        var vm = new DiagnosticViewModel(Module(source, clock), clock);
        var pending = typeof(DiagnosticViewModel).GetProperty("PendingLoad");
        vm.SetPresentation(true, false);
        try
        {
            await vm.PresentationTask.WaitAsync(Limit);
            var first = pending?.GetValue(vm);
            Assert.Equal(NoFinding, vm.Top3Caption);
            clock.Advance(TimeSpan.FromSeconds(30));
            source.Cpu = 92;
            await vm.RefreshCommand.ExecuteAsync(null).WaitAsync(Limit);
            Assert.Equal(PriorityCaption, vm.Top3Caption);
            Assert.Equal("Note de santé : 90/100", vm.Health);
            var rows = vm.TopFindings;
            Assert.Equal(new[] { "Processeur : très sollicité" }, rows.Select(row => row.Title));
            Assert.Equal(Recommendation, rows[0].Recommendation);
            Assert.Empty(rows[0].Actions);
            Assert.NotNull(pending);
            Assert.Equal(Hint("processeur 97 %"), Assert.IsType<string>(first));
            Assert.Equal(string.Empty, Read<string>(vm, "PendingLoad"));
            Assert.Equal(new[] { "bravo", "charlie", "alpha" }, vm.Processes.Select(p => p.Name));
            vm.Sort(DiagnosticProcessSort.Name);
            Assert.Equal(new[] { "alpha", "bravo", "charlie" }, vm.Processes.Select(p => p.Name));
            var requested = typeof(DiagnosticViewModel).GetEvent("ProcessesRequested");
            Assert.NotNull(requested);
            var requests = 0;
            requested.AddEventHandler(vm, (Action)(() => requests++));
            Assert.True(Read<bool>(rows[0], "ShowsProcesses"));
            Read<ICommand>(rows[0], "ShowProcessesCommand").Execute(null);
            Assert.Equal(new[] { "bravo", "charlie", "alpha" }, vm.Processes.Select(p => p.Name));
            Assert.Equal(1, requests);
            Assert.False(vm.IsValidationOpen);
            clock.Advance(TimeSpan.FromSeconds(30));
            source.MemoryAvailable = 1.6e9;
            await vm.RefreshCommand.ExecuteAsync(null).WaitAsync(Limit);
            clock.Advance(TimeSpan.FromSeconds(30));
            source.MemoryAvailable = 2.08e9;
            await vm.RefreshCommand.ExecuteAsync(null).WaitAsync(Limit);
            Assert.Equal(new[] { "Processeur : très sollicité", "Mémoire : presque pleine" }, vm.TopFindings.Select(row => row.Title));
            Read<ICommand>(vm.TopFindings[1], "ShowProcessesCommand").Execute(null);
            Assert.Equal(new[] { "charlie", "bravo", "alpha" }, vm.Processes.Select(p => p.Name));
            Assert.Equal(2, requests);
            Assert.Equal(8, source.Reads);
        }
        finally { vm.SetPresentation(false, false); }
    }

    [Fact, Trait("Category", "Windows")]
    public Task Rendered_screen_shows_pending_hint_then_load_card_with_its_button() =>
        ThemeContrastTests.OnSta(async () =>
        {
            var clock = new StepClock();
            var source = new LoadSource { Cpu = 97 };
            var module = Module(source, clock);
            var first = await Scan(module);
            clock.Advance(TimeSpan.FromSeconds(30));
            source.Cpu = 92;
            var second = await Scan(module);
            var viewClock = new LotFSpeechTests.Clock();
            var neverSource = new NeverSource();
            var vm = new DiagnosticViewModel(new DiagnosticModule(new DiagnosticSampler(neverSource, viewClock),
                gate: new ConfirmationService(viewClock)), viewClock);
            var view = new DiagnosticView(vm);
            var window = Show(view);
            try
            {
                vm.ApplyFindings(first);
                await Settle(window);
                var texts = VisibleTexts(view);
                var buttons = VisibleButtons(view);
                WriteEvidence(1, texts, buttons);
                Assert.Contains(Hint("processeur 97 %"), texts);
                Assert.Contains(NoFinding, texts);
                Assert.DoesNotContain("Voir les processus", buttons);

                vm.ApplyFindings(second);
                await Settle(window);
                texts = VisibleTexts(view);
                buttons = VisibleButtons(view);
                WriteEvidence(2, texts, buttons);
                Assert.Contains(PriorityCaption, texts);
                Assert.Contains("Processeur : très sollicité", texts);
                Assert.Contains(CpuDescription, texts);
                Assert.Contains(Recommendation, texts);
                Assert.DoesNotContain(Hint("processeur 97 %"), texts);
                Assert.Single(buttons, button => button == "Voir les processus");
                Assert.Equal("ProcessGrid", Assert.Single(Descendants<DataGrid>(view)).Name);
                Assert.InRange(neverSource.Reads, 0, 1);
            }
            finally { window.Close(); }
        }).WaitAsync(Limit);

    [Fact]
    public async Task Load_priority_opens_diagnostic_and_returns_to_normal()
    {
        var (confirmed, later) = await RunPriorityReadings(new LoadSource { Cpu = 97 }, source => source.Cpu = 92,
            source => source.Cpu = 30, 60);
        var store = new SystemStatusStore();
        store.PublishDiagnostic(confirmed);
        Assert.Equal(new[] { "cpu.load" }, store.LastPriorityPoints!.Value.Select(p => p.Id));
        var item = HomePriorityCatalog.For(Assert.Single(store.LastPriorityPoints.Value), Zone);
        Assert.Equal("Le processeur est très chargé.", item.Text);
        Assert.Equal("Ouvrir Diagnostic", item.Button);
        Assert.Equal("Diagnostic", item.Destination);
        Assert.Equal("Diagnostic · mesuré à 11 h 00", item.Detail);
        store.PublishDiagnostic(later);
        Assert.Empty(store.LastPriorityPoints!.Value);
        Assert.Equal(new[] { "cpu.load" }, store.LastResolvedPoints.Select(p => p.Id));
        Assert.Equal("Charge du processeur — retour à la normale lors du diagnostic de 11 h 01.",
            HomePriorityCatalog.ResolvedText(Assert.Single(store.LastResolvedPoints), Zone));

        (confirmed, later) = await RunPriorityReadings(new LoadSource { Cpu = 97 }, source => source.Cpu = 92,
            source => source.Cpu = null, 30);
        store = new SystemStatusStore();
        store.PublishDiagnostic(confirmed);
        store.PublishDiagnostic(later);
        var point = Assert.Single(store.LastPriorityPoints!.Value, p => p.Id == "cpu.load");
        Assert.Equal(PriorityState.ToRecheck, point.State);
        Assert.Equal("Diagnostic · mesuré à 11 h 00 · à revérifier", HomePriorityCatalog.For(point, Zone).Detail);

        (confirmed, later) = await RunPriorityReadings(new LoadSource { MemoryAvailable = 1.6e9 },
            source => source.MemoryAvailable = 2.08e9, source => source.MemoryAvailable = 8e9, 60);
        store = new SystemStatusStore();
        store.PublishDiagnostic(confirmed);
        item = HomePriorityCatalog.For(Assert.Single(store.LastPriorityPoints!.Value), Zone);
        Assert.Equal("La mémoire est presque pleine.", item.Text);
        Assert.Equal("Ouvrir Diagnostic", item.Button);
        store.PublishDiagnostic(later);
        Assert.Equal("Mémoire — retour à la normale lors du diagnostic de 11 h 01.",
            HomePriorityCatalog.ResolvedText(Assert.Single(store.LastResolvedPoints), Zone));
    }

    [Fact]
    public async Task Spike_short_gap_stale_or_unreadable_readings_never_conclude()
    {
        await Check(new LoadSource { Cpu = 97 }, 30, 40);
        await Check(new LoadSource { Cpu = 97 }, 30, null);
        await Check(new LoadSource { Cpu = 97 }, 360, 97);
        await Check(new LoadSource { Cpu = 97 }, 0, 97);
        await Check(new LoadSource { MemoryAvailable = null }, 30, 10);

        static async Task Check(LoadSource source, int seconds, double? cpu)
        {
            var clock = new StepClock();
            var module = Module(source, clock);
            await Scan(module);
            clock.Advance(TimeSpan.FromSeconds(seconds));
            source.Cpu = cpu;
            var findings = await Scan(module);
            Assert.Empty(findings.Issues);
            Assert.Equal(100, findings.Score.Value);
        }
    }

    [Fact]
    public void One_reading_alone_keeps_the_existing_analysis()
    {
        var h = Healthy(T0);
        var findings = DiagnosticAnalyzer.Analyze(h with
        {
            Cpu = h.Cpu with { TotalPercent = V(99d) },
            Memory = h.Memory with { UsedBytes = V(15.5d), AvailableBytes = V(0.5d) }
        }, Signals());
        Assert.Empty(findings.Issues);
        Assert.Empty(findings.Top3);
        Assert.Equal(new HealthScore(100, false), findings.Score);
    }

    private static async Task<(DiagnosticFindings Confirmed, DiagnosticFindings Later)> RunPriorityReadings(
        LoadSource source, Action<LoadSource> second, Action<LoadSource> third, int lastGap)
    {
        var clock = new StepClock();
        var module = Module(source, clock);
        await Scan(module);
        clock.Advance(TimeSpan.FromSeconds(30));
        second(source);
        var confirmed = await Scan(module);
        clock.Advance(TimeSpan.FromSeconds(lastGap));
        third(source);
        return (confirmed, await Scan(module));
    }

    private static string Hint(string value) =>
        "Charge élevée sur ce relevé (" + value + "). Actualisez le diagnostic dans quelques secondes pour vérifier qu'elle dure.";

    private static DiagnosticModule Module(LoadSource source, StepClock clock) =>
        new(new DiagnosticSampler(source, clock), new FixedSignals());

    private static async Task<DiagnosticFindings> Scan(DiagnosticModule module) =>
        (DiagnosticFindings)await module.ScanAsync(CancellationToken.None).WaitAsync(Limit);

    private sealed class FixedSignals : IDiagnosticSignalSource
    {
        public Task<DiagnosticSignals> ReadAsync(CancellationToken token) => Task.FromResult(Signals());
    }

    private sealed class StepClock : TimeProvider
    {
        private readonly object _sync = new();
        private DateTimeOffset _now = T0;
        public override DateTimeOffset GetUtcNow() { lock (_sync) return _now; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => GetUtcNow().UtcTicks;
        public override TimeZoneInfo LocalTimeZone => Zone;
        public void Advance(TimeSpan elapsed) { lock (_sync) _now += elapsed; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (period == Timeout.InfiniteTimeSpan)
            {
                Advance(dueTime);
                ThreadPool.QueueUserWorkItem(_ => callback(state));
            }
            return new InertTimer();
        }

        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class LoadSource : IDiagnosticSource
    {
        public double? Cpu { get; set; } = 10;
        public double? MemoryAvailable { get; set; } = 8e9;
        public int Reads { get; private set; }

        public Task<DiagnosticReading> ReadAsync(CancellationToken token)
        {
            Reads++;
            var missingCpu = DiagnosticValue<double>.Unavailable("Lecture du compteur Windows en échec");
            var missingMemory = DiagnosticValue<double>.Unavailable("Lecture mémoire en échec");
            var cpu = new RawCpu(Cpu is { } c ? Gauge(c) : new DiagnosticCounter(missingCpu, missingCpu, DiagnosticCounterKind.Gauge),
                [], V(3000d), V(3000d), V(40d));
            var memory = MemoryAvailable is { } a
                ? new MemoryDiagnostic(V(16e9), V(16e9 - a), V(1e9), V(0d), V(a), V(8e9))
                : new MemoryDiagnostic(missingMemory, missingMemory, missingMemory, missingMemory, missingMemory, missingMemory);
            return Task.FromResult(new DiagnosticReading(cpu, memory, [], [], [],
                [Process("alpha", 101, "a", 5, 1e9), Process("bravo", 102, "b", 70, 2e9), Process("charlie", 103, "c", 20, 6e9)]));
        }

        private static DiagnosticCounter Gauge(double value) => new(V(value), V(value), DiagnosticCounterKind.Gauge);

        private static RawProcess Process(string name, int pid, string identity, double cpu, double memory) =>
            new(name, pid, identity, Gauge(cpu), V(memory), V(memory), Gauge(0), V(0), V("Test"), V($@"C:\Test\{name}.exe"));
    }

    private static DiagnosticValue<T> V<T>(T value) => DiagnosticValue<T>.Available(value);

    private static DiagnosticSnapshot Healthy(DateTimeOffset at) => new(at,
        new(V(10d), [], V(3000d), V(3000d), V(40d)), new(V(16d), V(8d), V(1d), V(1d), V(8d), V(8d)),
        [new("0", "Disque physique", V(0d), V(0d), V(1d), V(1e12), V(1e12), V("Aucune défaillance prédite par Windows (contrôle limité)")),
         new("C:", "Volume", V(0d), V(0d), V(1d), V(100e9), V(50e9), V("Contrôle limité"))], [],
        [new("GPU", V(1d), V(1d), V(1d), V(40d))], []);

    private static DiagnosticSignals Signals() => new(V(new WindowsErrorSummary(0, [])),
        V<IReadOnlyList<DeviceProblem>>([]), V<IReadOnlyList<StartupEntry>>([]), V<IReadOnlyList<ProcessResourceInfo>>([]));
    private static T Read<T>(object target, string name)
    {
        var property = target.GetType().GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property.GetValue(target));
    }

    private static string[] VisibleTexts(DependencyObject view) =>
        Descendants<TextBlock>(view).Where(block => block.IsVisible && block.ActualWidth > 0 && !string.IsNullOrWhiteSpace(block.Text))
            .Select(block => block.Text).ToArray();

    private static string[] VisibleButtons(DependencyObject view) =>
        Descendants<Button>(view).Where(button => button.IsVisible).Select(button => button.Content).OfType<string>().ToArray();

    private static void WriteEvidence(int stage, string[] texts, string[] buttons)
    {
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-t5b");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "diagnostic-charge.txt");
        using var writer = new StreamWriter(path, append: stage > 1, encoding: new UTF8Encoding(false));
        writer.WriteLine($"# étape {stage}");
        foreach (var text in texts.Concat(buttons))
            writer.WriteLine(text);
    }

    private static Window Show(FrameworkElement view)
    {
        var window = new Window { Content = view, Width = 1600, Height = 900, ShowInTaskbar = false,
            Background = Brushes.White, Foreground = Brushes.Black };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static async Task Settle(Window window)
    {
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
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

    private sealed class NeverSource : IDiagnosticSource
    {
        public int Reads { get; private set; }

        public async Task<DiagnosticReading> ReadAsync(CancellationToken token)
        {
            Reads++;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }
    }
}
