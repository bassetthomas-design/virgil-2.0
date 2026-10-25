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
            code = Regex.Replace(code, @"\b(?:action|result)\.Result\b", "ReportResultText");
            Assert.DoesNotMatch(@"\.\s*Result\b", code);
        }
    }

    [Fact]
    public void ProgressIsThrottledAndCompletionIsNeverLost()
    {
        var clock = new Clock();
        var received = new Sink();
        var progress = new ThrottledOperationProgress(received, clock);
        progress.Report(new("Première étape"));
        for (var i = 0; i < 10000; i++) progress.Report(new("Lecture", i, 10001));
        Assert.Single(received.Values);
        clock.Ticks += TimeSpan.FromMilliseconds(100).Ticks;
        progress.Report(new("Étape suivante", 1, 2, 50));
        progress.Report(new("Terminé", 2, 2, 100));
        Assert.Equal(3, received.Values.Count);
        Assert.Equal("Terminé", received.Values[^1].Step);
    }

    private sealed class Clock : TimeProvider
    {
        public long Ticks { get; set; }
        public override long GetTimestamp() => Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
    private sealed class Sink : IProgress<OperationProgress>
    {
        public List<OperationProgress> Values { get; } = [];
        public void Report(OperationProgress value) => Values.Add(value);
    }
}
