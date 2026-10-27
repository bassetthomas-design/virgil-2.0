using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.App.ViewModels;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
using Virgil.Core.Diagnostic;
using Virgil.Infrastructure.Windows.Diagnostic;
using Xunit;

namespace Virgil.Tests;

public sealed class DiagnosticPresentationTests
{
    [Fact]
    [Trait("Category", "Safety")]
    public async Task CancelRefreshStopsSamplingAndPublishesNoPartialSnapshot()
    {
        var source = new BlockingSource();
        var vm = Create(source);
        vm.SetPresentation(true, false);
        await source.Started.Task;
        vm.Operation.CancelCommand.Execute(null);
        Assert.True(vm.Operation.IsCancelling);
        await vm.PresentationTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(vm.IsRefreshing);
        Assert.Null(vm.Snapshot);
        Assert.Equal(0, source.Active);
        Assert.Contains("annulée", vm.Status);
        vm.SetPresentation(false, false);
    }

    [Fact]
    public async Task HiddenAndMinimizedViewsNeverRead()
    {
        var source = new BlockingSource();
        var vm = Create(source);
        await vm.RefreshAsync();
        vm.SetPresentation(false, false);
        vm.SetPresentation(true, true);
        await vm.RefreshAsync();
        Assert.Equal(0, source.Reads);
        Assert.Null(vm.Snapshot);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task VisibleViewStartsAndHidingOrMinimizingCancelsImmediately(bool visible, bool minimized)
    {
        var source = new BlockingSource();
        var vm = Create(source);
        vm.SetPresentation(true, false);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsRefreshing);
        vm.SetPresentation(visible, minimized);
        await vm.PresentationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, source.Active);
        Assert.Null(vm.Snapshot);
        Assert.False(vm.IsRefreshing);
    }
    [Fact]
    public async Task RepeatedVisibilityAndManualRefreshNeverOverlapSampling()
    {
        var source = new BlockingSource();
        var vm = Create(source);
        vm.SetPresentation(true, false);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.SetPresentation(true, false);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => vm.RefreshAsync()));
        Assert.Equal(1, source.Reads);
        vm.SetPresentation(false, false);
        vm.SetPresentation(true, false);
        vm.SetPresentation(false, false);
        await vm.PresentationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.Maximum);
        Assert.Equal(0, source.Active);
    }
    [Fact]
    public void DiagnosticUsesValidatedDiWithSharedSamplerAndTwoMaintenanceModules()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var vm = provider.GetRequiredService<DiagnosticViewModel>();
        Assert.Same(vm, provider.GetRequiredService<DiagnosticViewModel>());
        Assert.Same(provider.GetRequiredService<DiagnosticSampler>(), provider.GetRequiredService<DiagnosticSampler>());
        var modules = provider.GetServices<IMaintenanceModule>().ToArray();
        Assert.Equal(2, modules.Length);
        Assert.Single(modules.OfType<DiagnosticModule>());
        Assert.Single(modules.OfType<Virgil.Core.Cleanup.CleanupMaintenanceModule>());
        Assert.IsType<WindowsDiagnosticSource>(provider.GetRequiredService<IDiagnosticSource>());
        Assert.IsType<WindowsDiagnosticSignals>(provider.GetRequiredService<IDiagnosticSignalSource>());
    }
    [Fact]
    public void DisplayDoesNotTurnMissingValuesIntoZero()
    {
        var missing = DiagnosticValue<double>.Unavailable("Indisponible : capteur non exposé par Windows");
        Assert.Equal(missing.Reason, DiagnosticDisplay.Percent(missing));
        Assert.Equal(missing.Reason, DiagnosticDisplay.Bytes(missing));
        Assert.Equal(missing.Reason, DiagnosticDisplay.Rate(missing));
        Assert.Equal("1 Go", DiagnosticDisplay.Bytes(DiagnosticValue<double>.Available(1024d * 1024 * 1024)));
        Assert.Equal("1 Mo/s", DiagnosticDisplay.Rate(DiagnosticValue<double>.Available(1024d * 1024)));
    }
    [Fact]
    [Trait("Category", "Windows")]
    public async Task RealWindowsReadingHasLogicalCoresMemoryVolumesAndCurrentProcess()
    {
        var source = new WindowsDiagnosticSource();
        var reading = await source.ReadAsync(CancellationToken.None);
        Assert.Equal(Environment.ProcessorCount, reading.Cpu.Cores.Count);
        Assert.True(reading.Memory.TotalBytes.IsAvailable);
        Assert.True(reading.Memory.TotalBytes.Value > 0);
        Assert.Contains(reading.Disks, d => d.Kind == "Volume" && d.Total.IsAvailable && d.Total.Value > 0);
        Assert.Contains(reading.Processes, p => p.Pid == Environment.ProcessId);
    }
    [Fact]
    public async Task VisibleViewMeasuresOnceUntilExplicitRefreshUsingInjectedClock()
    {
        var clock = new PresentationClock();
        var source = new ImmediateSource();
        var signals = new CountingSignals();
        var vm = new DiagnosticViewModel(new DiagnosticModule(new DiagnosticSampler(source, clock), signals), clock);
        vm.SetPresentation(true, false);
        Assert.Equal(1, source.Reads);
        clock.Advance(TimeSpan.FromSeconds(1));
        await vm.PresentationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, source.Reads);
        Assert.Equal(1, signals.Reads);
        var measuredStatus = vm.Status;
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(2, source.Reads);
        Assert.Equal(measuredStatus, vm.Status);
        vm.SetPresentation(false, false);
        vm.SetPresentation(true, false);
        clock.Advance(TimeSpan.FromSeconds(1));
        await vm.PresentationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, source.Reads);
        Assert.Equal(1, signals.Reads);
        measuredStatus = vm.Status;
        var refresh = vm.RefreshCommand.ExecuteAsync(null);
        Assert.True(vm.IsRefreshing);
        Assert.Equal(measuredStatus, vm.Status);
        clock.Advance(TimeSpan.FromSeconds(1));
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(6, source.Reads);
        Assert.Equal(2, signals.Reads);
        Assert.False(vm.IsRefreshing);
        Assert.NotEqual(measuredStatus, vm.Status);
        vm.SetPresentation(false, false);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(6, source.Reads);
    }
    [Fact]
    public async Task UnknownCoreIdentityShowsReasonWithoutGuessingALogicalCoreLabel()
    {
        var clock = new PresentationClock();
        var source = new ImmediateSource { UnknownCoreIdentity = true };
        var vm = new DiagnosticViewModel(new DiagnosticModule(new DiagnosticSampler(source, clock)), clock);
        vm.SetPresentation(true, false);
        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => vm.Snapshot is not null);
        Assert.Contains("Charge par cœur : Indisponible : identité des cœurs PDH non interprétable ou ambiguë", vm.CpuDetails);
        Assert.DoesNotContain(vm.CpuDetails, text => text.StartsWith("Cœur logique", StringComparison.Ordinal));
        vm.SetPresentation(false, false);
        await vm.PresentationTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact]
    public async Task NoteAndTopThreeArePublishedWithPartialSignalReasons()
    {
        var clock = new PresentationClock();
        var vm = new DiagnosticViewModel(new DiagnosticModule(new DiagnosticSampler(new ImmediateSource(), clock)), clock);
        vm.SetPresentation(true, false);
        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => vm.Snapshot is not null && !vm.IsRefreshing);
        Assert.Contains("partielle", vm.Health);
        Assert.Contains("80/100", vm.Health);
        Assert.Single(vm.Top3);
        Assert.NotEmpty(vm.Findings);
        Assert.NotEmpty(vm.Unavailable);
        vm.SetPresentation(false, false);
        await vm.PresentationTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact]
    public async Task StartupInventoryIsInformationalInsteadOfAnIssue()
    {
        var clock = new PresentationClock();
        var signals = new CountingSignals
        {
            Value = DiagnosticSignals.Unavailable with
            {
                Startup = DiagnosticValue<IReadOnlyList<StartupEntry>>.Available(
                    [new("Programme", "HKCU/Run", DiagnosticValue<string>.Unavailable("Impact non mesuré"))])
            }
        };
        var vm = new DiagnosticViewModel(new DiagnosticModule(new DiagnosticSampler(new ImmediateSource(), clock), signals), clock);
        vm.SetPresentation(true, false);
        clock.Advance(TimeSpan.FromSeconds(1));
        await vm.PresentationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("Programme — HKCU/Run · Impact : Impact non mesuré", vm.Startup);
        Assert.DoesNotContain(vm.Findings, finding => finding.Title.Contains("Programme", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.Unavailable, reason => reason.Contains("Impact", StringComparison.Ordinal));
        vm.SetPresentation(false, false);
    }
    [Fact]
    public async Task ConfirmationIsExplicitAndCancelHasNoExecutionEffect()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var vm = new DiagnosticViewModel(new DiagnosticModule(new DiagnosticSampler(new ImmediateSource(), TimeProvider.System)), TimeProvider.System, authority);
        var action = new RecordingAction(authority);
        await vm.PrepareActionAsync(action);
        Assert.True(vm.IsValidationOpen);
        Assert.Contains("renforcée", vm.ValidationSafety);
        Assert.Equal(0, action.Effects);
        vm.CancelConfirmation();
        await vm.ExecuteDecisionCommand.ExecuteAsync(null);
        Assert.Equal(0, action.Effects);
        await vm.PrepareActionAsync(action);
        await vm.ExecuteDecisionCommand.ExecuteAsync(null);
        Assert.False(vm.IsValidationOpen);
        Assert.Equal(1, action.Effects);
        Assert.Equal(ConfirmationLevel.Reinforced, action.Level);
    }
    [Fact]
    public async Task NavigationRequiresDecisionAndNeverRunsAnUpdateOrCleanup()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var vm = new DiagnosticViewModel(new DiagnosticModule(new DiagnosticSampler(new ImmediateSource(), TimeProvider.System)), TimeProvider.System, authority);
        var navigations = new List<DiagnosticSuggestion>();
        vm.NavigationRequested += navigations.Add;
        await vm.PrepareActionAsync(new DiagnosticNavigationAction(authority, DiagnosticSuggestion.Cleanup));
        Assert.Empty(navigations);
        await vm.ExecuteDecisionCommand.ExecuteAsync(null);
        Assert.Equal(new[] { DiagnosticSuggestion.Cleanup }, navigations);
    }
    [Fact]
    public async Task HidingPresentationCancelsPendingConfirmation()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var vm = new DiagnosticViewModel(new DiagnosticModule(new DiagnosticSampler(new ImmediateSource(), TimeProvider.System)), TimeProvider.System, authority);
        var action = new RecordingAction(authority);
        await vm.PrepareActionAsync(action);
        vm.SetPresentation(false, false);
        await vm.ExecuteDecisionCommand.ExecuteAsync(null);
        Assert.False(vm.IsValidationOpen);
        Assert.Equal(0, action.Effects);
    }
    [Fact]
    public async Task SlowSignalsAreCachedUntilExplicitInvalidation()
    {
        var clock = new PresentationClock();
        var signals = new CountingSignals();
        var module = new DiagnosticModule(new DiagnosticSampler(new ImmediateSource(), clock), signals);
        async Task Scan()
        {
            var task = module.ScanAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(1));
            await task;
        }
        await Scan(); await Scan();
        Assert.Equal(1, signals.Reads);
        module.InvalidateSignals();
        await Scan();
        Assert.Equal(2, signals.Reads);
    }
    private sealed class CountingSignals : IDiagnosticSignalSource
    {
        public int Reads;
        public DiagnosticSignals Value { get; init; } = DiagnosticSignals.Unavailable;
        public Task<DiagnosticSignals> ReadAsync(CancellationToken token) { Reads++; return Task.FromResult(Value); }
    }
    private sealed class RecordingAction(IConfirmedPlanGate gate) : IMaintenanceAction
    {
        public int Effects;
        public ConfirmationLevel? Level;
        public string Id => "test.close";
        public RiskLevel RiskLevel => RiskLevel.Sensitive;
        public bool RequiresAdmin => false;
        public bool RebootRequired => false;
        public Reversibility Reversibility => Reversibility.None;
        public Task<ActionPreview> PreviewAsync(CancellationToken token) => Task.FromResult(new ActionPreview(Id, "Fermeture forcée", RiskLevel, ["steam"], new Dictionary<string, string>(), ["Perte possible de données"], false, false, Reversibility, []));
        public async Task<ActionResult> ExecuteAsync(ConfirmedPlan? plan, CancellationToken token)
        {
            var validation = gate.TryConsume(plan, await PreviewAsync(token));
            if (!validation.IsAccepted) return new(Id, false, true, validation.Message, [], [], []);
            Level = plan!.ConfirmationLevel;
            Effects++;
            return new(Id, true, false, null, [], ["Application fermée."], []);
        }
    }
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(1, deadline.Token);
    }
    private sealed class ImmediateSource : IDiagnosticSource
    {
        public int Reads;
        public bool UnknownCoreIdentity { get; init; }
        public TaskCompletionSource SecondRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DiagnosticReading> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            var v = DiagnosticValue<double>.Available(100);
            var gauge = new DiagnosticCounter(v, v, DiagnosticCounterKind.Gauge);
            var core = UnknownCoreIdentity
                ? new DiagnosticCounter(DiagnosticValue<double>.Unavailable("Indisponible : identité des cœurs PDH non interprétable ou ambiguë"),
                    v, DiagnosticCounterKind.IdleTime)
                : gauge;
            var result = new DiagnosticReading(new(gauge, new[] { core }, v, v, v), new(v, v, v, v, v, v),
                Array.Empty<RawDisk>(), Array.Empty<RawNetwork>(), Array.Empty<RawGpu>(), Array.Empty<RawProcess>());
            if (Reads == 2) SecondRead.TrySetResult();
            return Task.FromResult(result);
        }
    }
    private sealed class PresentationClock : TimeProvider
    {
        private long _ticks;
        private readonly List<PresentationTimer> _timers = new();
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new PresentationTimer(this, callback, state);
            lock (_timers) _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan by)
        {
            _ticks += by.Ticks;
            PresentationTimer[] timers;
            lock (_timers) timers = _timers.ToArray();
            foreach (var timer in timers) timer.Fire(_ticks);
        }
        private sealed class PresentationTimer(PresentationClock owner, TimerCallback callback, object? state) : ITimer
        {
            private long _due = long.MaxValue;
            private long _period;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner._ticks + dueTime.Ticks;
                _period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                return true;
            }
            public void Fire(long now)
            {
                if (now < _due) return;
                _due = _period > 0 ? now + _period : long.MaxValue;
                callback(state);
            }
            public void Dispose() => _due = long.MaxValue;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private static DiagnosticViewModel Create(IDiagnosticSource source) => new(new DiagnosticModule(new DiagnosticSampler(source, TimeProvider.System)), TimeProvider.System);
    private sealed class BlockingSource : IDiagnosticSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads, Active, Maximum;
        public async Task<DiagnosticReading> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            Active++;
            Maximum = Math.Max(Maximum, Active);
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("La lecture doit être annulée.");
            }
            finally { Active--; }
        }
    }
}
