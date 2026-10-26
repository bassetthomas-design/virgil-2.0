using System.Text.RegularExpressions;
using Virgil.Core.Operations;
using Xunit;

namespace Virgil.Tests;

public sealed class LongOperationArchitectureTests
{
    [Fact]
    public void AppAndCoreNeverSynchronouslyWaitForTasks()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        foreach (var project in new[] { "Virgil.Core", "Virgil.App" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            var code = File.ReadAllText(file);
            if (Path.GetFileName(file) == "AppBootstrap.cs")
                code = Regex.Replace(code, @"_host\??\.(?:StartAsync|StopAsync)\(\)\.GetAwaiter\(\)\.GetResult\(\)", "HostLifecycle");
            Assert.DoesNotMatch(@"\.\s*GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\(", code);
            Assert.DoesNotMatch(@"\.\s*Wait\s*\(", code);
            // Result is also a string field on our report DTOs. Only those precise, existing
            // receivers are exempt; a new task.Result remains a violation.
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var dtoReceiver = relative switch
            {
                "src/Virgil.App/ViewModels/ReportsHistoryViewModel.cs" or
                "src/Virgil.Core/Reports/ReportSanitizer.cs" or
                "src/Virgil.Core/Reports/ReportExportService.cs" => "action",
                "src/Virgil.App/ViewModels/ApplicationsViewModel.cs" or
                "src/Virgil.Core/Applications/ApplicationReportMapper.cs" => "result",
                _ => null
            };
            if (dtoReceiver is not null)
                code = Regex.Replace(code, @"\b" + dtoReceiver + @"\.Result\b", "ReportResultText");
            Assert.DoesNotMatch(@"\.\s*Result\b", code);
        }
    }

    [Fact]
    public void ProgressIsThrottledAndCompletionIsNeverLost()
    {
        var clock = new Clock();
        var received = new Sink();
        using var progress = new ThrottledOperationProgress(received, clock);
        progress.Report(new("Première étape"));
        for (var i = 0; i < 10000; i++) progress.Report(new("Lecture", i, 10001));
        Assert.Single(received.Values);
        clock.Ticks += TimeSpan.FromMilliseconds(100).Ticks;
        progress.Report(new("Étape suivante", 1, 2, 50));
        progress.Report(new("Terminé", 2, 2, 100));
        Assert.Equal(3, received.Values.Count);
        Assert.Equal("Terminé", received.Values[^1].Step);
    }

    [Fact]
    public void LastIntermediateStageIsDeliveredWithoutAnotherReport()
    {
        var clock = new Clock();
        var received = new Sink();
        using var progress = new ThrottledOperationProgress(received, clock);
        progress.Report(new("Aperçu terminé", Percent: 100));
        clock.Advance(TimeSpan.FromMilliseconds(10));
        progress.Report(new("Ancienne étape"));
        clock.Advance(TimeSpan.FromMilliseconds(10));
        progress.Report(new("Inventaire des logiciels"));
        clock.Advance(TimeSpan.FromMilliseconds(79));
        Assert.Single(received.Values);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, received.Values.Count);
        Assert.Equal("Inventaire des logiciels", received.Values[^1].Step);
        Assert.Null(received.Values[^1].Percent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletionOrDisposalSuppressesStalePendingStage(bool dispose)
    {
        var clock = new Clock();
        var received = new Sink();
        using var progress = new ThrottledOperationProgress(received, clock);
        progress.Report(new("Lecture"));
        progress.Report(new("Étape en attente"));
        if (dispose) progress.Dispose();
        else progress.Report(new("Terminé", Percent: 100));
        var count = received.Values.Count;
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(count, received.Values.Count);
        Assert.DoesNotContain(received.Values, value => value.Step == "Étape en attente");
    }

    private sealed class Clock : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        public long Ticks { get; set; }
        public override long GetTimestamp() => Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan duration)
        {
            Ticks += duration.Ticks;
            foreach (var timer in _timers.ToArray()) timer.FireIfDue();
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            private long? _due;
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Ticks + dueTime.Ticks;
                return true;
            }
            public void FireIfDue()
            {
                if (_disposed || _due is not { } due || clock.Ticks < due) return;
                _due = null;
                callback(state);
            }
            public void Dispose() { _disposed = true; _due = null; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Sink : IProgress<OperationProgress>
    {
        public List<OperationProgress> Values { get; } = [];
        public void Report(OperationProgress value) => Values.Add(value);
    }
}
