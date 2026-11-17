using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotReparationProgressionWindowsTests
{
    [Fact]
    public async Task Runner_reports_total_and_monotone_progress_only()
    {
        var values = new List<int?>();
        var script = string.Join("; ", new[]
        {
            "Verification 5% complete.",
            "Verification 5% complete.",
            "Verification 10% complete.",
            "Progress: 10 of 30 done; Stage: 100%; Total: 33%; ETA:   0:00:02",
            "Progress: 0 of 30 done; Stage:   0%; Total: 34%; ETA:   0:00:02",
            "[=====                      10.0%                           ]",
            "[==========                 20.5%                           ]",
            "Progression : 30 sur 30 effectué(s) ; Étape : 100 % ; Total : 100 % ; ETA : 0:00:00",
            "Copie 50 % puis 60 % sans total"
        }.Select(line => "Write-Output '" + line + "'"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await new ElevatedCommandRunner().RunAsync(
            new ElevatedCommandSpec("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", script }),
            new Collector(values), deadline.Token);
        Assert.False(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.Equal(new int?[] { 5, 10, 33, 34, 100 }, values);
    }

    private sealed class Collector(List<int?> values) : IProgress<int?>
    {
        public void Report(int? value) => values.Add(value);
    }
}
