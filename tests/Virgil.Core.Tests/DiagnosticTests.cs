using Virgil.Contracts.Diagnostic;
using Virgil.Core.Diagnostic;
using Xunit;

namespace Virgil.Tests;

public sealed class DiagnosticTests
{
    private static DiagnosticValue<double> V(double n) => DiagnosticValue<double>.Available(n);
    private static DiagnosticCounter C(double n, DiagnosticCounterKind kind = DiagnosticCounterKind.Rate, double denominator = 0) => new(V(n), V(denominator), kind);
    private static DiagnosticValue<string> Text(string text) => DiagnosticValue<string>.Available(text);
    private static DiagnosticReading Reading(double n) => new(
        new(C(n, DiagnosticCounterKind.IdleTime, n * 2), new[] { C(n, DiagnosticCounterKind.IdleTime, n * 2) }, V(3000), V(3200), DiagnosticValue<double>.Unavailable("Indisponible : capteur non exposé par Windows")),
        new(V(16000), V(8000), V(2000), V(1000), V(8000), V(9000)),
        new[] { new RawDisk("C:", "Volume", C(n), C(n * 2), C(n, DiagnosticCounterKind.AverageMilliseconds, n * 2), V(10000), V(5000), Text("Contrôle limité")) },
        new[] { new RawNetwork("Ethernet", "Ethernet", C(n), C(n * 2), V(1_000_000)) },
        new[] { new RawGpu("GPU", new[] { C(n, DiagnosticCounterKind.BusyTime, n * 4) }, V(1024), V(4096), DiagnosticValue<double>.Unavailable("Indisponible : capteur non exposé par Windows")) },
        new[] { new RawProcess("explorer", 12, "session-12", C(n, DiagnosticCounterKind.BusyTime, n * 4), V(300), V(400), C(n), DiagnosticValue<int>.Available(2), Text("Microsoft"), Text(@"C:\Windows\explorer.exe")) });

    [Fact]
    public void TwoReadingsProduceCompleteUtcSnapshotAndMeasuredRates()
    {
        var result = DiagnosticSampler.Calculate(Reading(100), Reading(300), 2, DateTimeOffset.Parse("2026-10-20T14:00:00+02:00"));
        Assert.Equal(TimeSpan.Zero, result.CapturedAtUtc.Offset);
        Assert.Equal(50, result.Cpu.TotalPercent.Value);
        Assert.Equal(50, Assert.Single(result.Cpu.CorePercent).Value);
        Assert.Equal(16000, result.Memory.TotalBytes.Value);
        Assert.Equal(100, Assert.Single(result.Disks).ReadBytesPerSecond.Value);
        Assert.Equal(200, Assert.Single(result.Network).ReceiveBytesPerSecond.Value);
        Assert.Equal(25, Assert.Single(result.Gpu).LoadPercent.Value);
        Assert.Equal(25, Assert.Single(result.Processes).CpuPercent.Value);
        Assert.Equal(2, result.Processes[0].NetworkConnections.Value);
    }
    [Fact]
    public void UnavailableIsPropagatedWithReasonAndCannotBeReadAsZero()
    {
        var snapshot = DiagnosticSampler.Calculate(Reading(1), Reading(2), 1, DateTimeOffset.UnixEpoch);
        Assert.False(snapshot.Cpu.TemperatureCelsius.IsAvailable);
        Assert.Equal("Indisponible : capteur non exposé par Windows", snapshot.Cpu.TemperatureCelsius.Reason);
        Assert.Throws<InvalidOperationException>(() => snapshot.Cpu.TemperatureCelsius.Value);
        var unavailable = new DiagnosticCounter(snapshot.Cpu.TemperatureCelsius, V(0), DiagnosticCounterKind.Rate);
        Assert.Equal(snapshot.Cpu.TemperatureCelsius, DiagnosticSampler.CalculateCounter(unavailable, C(10), 1));
        Assert.Equal(snapshot.Cpu.TemperatureCelsius, DiagnosticSampler.CalculateCounter(C(1), unavailable, 1));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidIntervalIsUnavailable(double seconds) => Assert.False(DiagnosticSampler.CalculateCounter(C(1), C(2), seconds).IsAvailable);
    [Fact]
    public void ResetOrNewCounterAndChangedIdentityAreUnavailable()
    {
        Assert.False(DiagnosticSampler.CalculateCounter(C(10), C(1), 1).IsAvailable);
        Assert.False(DiagnosticSampler.CalculateCounter(null, C(1), 1).IsAvailable);
        Assert.False(DiagnosticSampler.CalculateCounter(C(1), C(2) with { Identity = "new" }, 1).IsAvailable);
        var after = Reading(200) with { Processes = new[] { Reading(200).Processes[0] with { Identity = "reused-pid" } } };
        Assert.False(DiagnosticSampler.Calculate(Reading(100), after, 1, DateTimeOffset.UnixEpoch).Processes[0].CpuPercent.IsAvailable);
    }
    [Theory]
    [InlineData("System")]
    [InlineData("svchost.exe")]
    [InlineData("explorer")]
    [InlineData("dwm")]
    [InlineData("csrss")]
    [InlineData("MsMpEng")]
    [InlineData("chrome")]
    [InlineData("Steam")]
    [InlineData("EpicGamesLauncher")]
    [InlineData("Battle.net")]
    [InlineData("Discord")]
    [InlineData("nvcontainer")]
    [InlineData("RadeonSoftware")]
    [InlineData("igfxEM")]
    [InlineData("OneDrive")]
    public void KnownProcessHasFrenchExplanation(string name) => Assert.DoesNotContain("Programme inconnu", ProcessExplanationCatalog.Explain(name, Text("éditeur"), Text(@"C:\Apps\x.exe")));
    [Fact]
    public void UnknownProcessUsesOnlyObservedPublisherAndDirectoryOrHonestFallback()
    {
        Assert.Equal(@"Programme de Acme, situé dans C:\Apps", ProcessExplanationCatalog.Explain("whatever", Text("Acme"), Text(@"C:\Apps\x.exe")));
        Assert.Equal("Programme inconnu : à vérifier", ProcessExplanationCatalog.Explain("whatever", DiagnosticValue<string>.Unavailable("Indisponible"), Text(@"C:\Apps\x.exe")));
    }
    [Theory]
    [InlineData(DiagnosticProcessSort.Cpu)]
    [InlineData(DiagnosticProcessSort.Memory)]
    [InlineData(DiagnosticProcessSort.Disk)]
    [InlineData(DiagnosticProcessSort.Network)]
    public void NumericSortUsesValuesAndPutsUnavailableLastInBothDirections(DiagnosticProcessSort sort)
    {
        var p = DiagnosticSampler.Calculate(Reading(1), Reading(2), 1, DateTimeOffset.UnixEpoch).Processes[0];
        var low = p with { Name = "A", CpuPercent = V(2), PrivateBytes = V(2), DiskBytesPerSecond = V(2), NetworkConnections = DiagnosticValue<int>.Available(2) };
        var high = p with { Name = "Z", CpuPercent = V(10), PrivateBytes = V(10), DiskBytesPerSecond = V(10), NetworkConnections = DiagnosticValue<int>.Available(10) };
        var missing = p with { Name = "Missing", CpuPercent = DiagnosticValue<double>.Unavailable("Indisponible"), PrivateBytes = DiagnosticValue<double>.Unavailable("Indisponible"), DiskBytesPerSecond = DiagnosticValue<double>.Unavailable("Indisponible"), NetworkConnections = DiagnosticValue<int>.Unavailable("Indisponible") };
        Assert.Equal(new[] { high, low, missing }, DiagnosticProcessOrdering.Sort(new[] { low, missing, high }, sort));
        Assert.Equal(new[] { low, high, missing }, DiagnosticProcessOrdering.Sort(new[] { low, missing, high }, sort, false));
        Assert.Equal("A", DiagnosticProcessOrdering.Sort(new[] { low, high }, DiagnosticProcessSort.Name, false)[0].Name);
    }
    [Fact]
    public async Task SamplerUsesFakePortsAndClockAndModuleHasNoActions()
    {
        var clock = new ManualClock();
        var source = new FakeSource();
        var module = new DiagnosticModule(new DiagnosticSampler(source, clock));
        var scan = module.ScanAsync(CancellationToken.None);
        Assert.False(scan.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = Assert.IsType<DiagnosticFindings>(await scan);
        Assert.Equal(100, result.Snapshot.Disks[0].ReadBytesPerSecond.Value);
        Assert.Empty(module.GetActions(result));
        Assert.Equal("Nettoyer", Assert.Single(result.ProposedActions).Title);
        Assert.NotEmpty(result.Observations);
        Assert.Equal(2, source.Reads);
    }
    [Fact]
    public async Task CancellationNeverReturnsPartialSnapshotAndReleasesSamplingGate()
    {
        var clock = new ManualClock();
        var source = new FakeSource();
        var sampler = new DiagnosticSampler(source, clock);
        using var cancellation = new CancellationTokenSource();
        var scan = sampler.SampleAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        var next = sampler.SampleAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(await next);
    }
    [Fact]
    public async Task ConcurrentSamplersShareTheSameGateAndWaitingCancellationDoesNotRead()
    {
        var clock = new ManualClock();
        var source = new FakeSource();
        var sampler = new DiagnosticSampler(source, clock);
        using var active = new CancellationTokenSource();
        using var waiting = new CancellationTokenSource();
        var first = sampler.SampleAsync(active.Token);
        var second = sampler.SampleAsync(waiting.Token);
        Assert.Equal(1, source.Reads);
        waiting.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(1, source.Reads);
        active.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }
    [Fact]
    public async Task CancellationDuringSecondReadCannotPublishCompletedFindings()
    {
        var clock = new ManualClock();
        using var cancellation = new CancellationTokenSource();
        var source = new CancelOnSecondRead(cancellation);
        var module = new DiagnosticModule(new DiagnosticSampler(source, clock));
        var result = module.ScanAsync(cancellation.Token);
        clock.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
    }
    private sealed class CancelOnSecondRead(CancellationTokenSource cancellation) : IDiagnosticSource
    {
        private int _reads;
        public Task<DiagnosticReading> ReadAsync(CancellationToken cancellationToken)
        {
            if (++_reads == 2) cancellation.Cancel();
            return Task.FromResult(Reading(_reads * 100));
        }
    }
    [Fact]
    public void DiagnosticCoreContainsNoOsAccess()
    {
        var folder = System.IO.Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src", "Virgil.Core", "Diagnostic");
        foreach (var file in System.IO.Directory.EnumerateFiles(folder, "*.cs"))
        {
            var text = System.IO.File.ReadAllText(file);
            foreach (var forbidden in new[] { "Microsoft.Win32", "System.IO", "DllImport", "LibraryImport", "System.Diagnostics.Process" })
                Assert.DoesNotContain(forbidden, text);
        }
    }
    private sealed class FakeSource : IDiagnosticSource
    {
        public int Reads { get; private set; }
        public Task<DiagnosticReading> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Reading(++Reads * 100));
        }
    }

    // Horloge et timers déterministes, sans paquet de test supplémentaire.
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        private readonly List<Timer> _timers = new();
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan by)
        {
            _ticks += by.Ticks;
            foreach (var timer in _timers.ToArray()) timer.Fire(_ticks);
        }
        private sealed class Timer(ManualClock owner, TimerCallback callback, object? state) : ITimer
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
}
