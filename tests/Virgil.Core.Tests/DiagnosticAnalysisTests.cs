using Virgil.Contracts;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Diagnostic;
using Xunit;

namespace Virgil.Tests;

public sealed class DiagnosticAnalysisTests
{
    internal static DiagnosticValue<T> V<T>(T value) => DiagnosticValue<T>.Available(value);
    internal static DiagnosticSnapshot Healthy() => new(DateTimeOffset.UnixEpoch,
        new(V(10d), [], V(3000d), V(3000d), V(40d)), new(V(16d), V(8d), V(1d), V(1d), V(8d), V(8d)),
        [new("0", "Disque physique", V(0d), V(0d), V(1d), V(1e12), V(1e12), V("Aucune défaillance prédite par Windows (contrôle limité)")),
         new("C:", "Volume", V(0d), V(0d), V(1d), V(100e9), V(50e9), V("Contrôle limité"))], [],
        [new("GPU", V(1d), V(1d), V(1d), V(40d))], []);
    internal static DiagnosticSignals Signals() => new(V(new WindowsErrorSummary(0, [])),
        V<IReadOnlyList<DeviceProblem>>([]), V<IReadOnlyList<StartupEntry>>([]), V<IReadOnlyList<ProcessResourceInfo>>([]));
    [Fact] public void HealthySignalsProduceFullScoreAndNoProblems()
    {
        var result = DiagnosticAnalyzer.Analyze(Healthy(), Signals());
        Assert.Equal(new HealthScore(100, false), result.Score);
        Assert.Empty(result.Issues);
        Assert.Empty(result.Top3);
    }
    [Theory]
    [InlineData("cpu", 80)] [InlineData("gpu", 80)] [InlineData("disk", 70)]
    [InlineData("space", 90)] [InlineData("errors", 90)] [InlineData("devices", 90)] [InlineData("process", 95)]
    public void EveryProblemHasDocumentedPenaltyAndFrenchExplanation(string problem, int score)
    {
        var (snapshot, signals) = Problem(problem);
        var result = DiagnosticAnalyzer.Analyze(snapshot, signals);
        Assert.Equal(score, result.Score.Value);
        Assert.False(result.Score.IsPartial);
        var issue = Assert.Single(result.Issues);
        Assert.False(string.IsNullOrWhiteSpace(issue.Title));
        Assert.False(string.IsNullOrWhiteSpace(issue.Description));
    }
    internal static (DiagnosticSnapshot, DiagnosticSignals) Problem(string problem)
    {
        var s = Healthy(); var signals = Signals();
        switch (problem)
        {
            case "cpu": s = s with { Cpu = s.Cpu with { TemperatureCelsius = V(90d) } }; break;
            case "gpu": s = s with { Gpu = [s.Gpu[0] with { TemperatureCelsius = V(90d) }] }; break;
            case "disk": s = s with { Disks = [s.Disks[0] with { Health = V("Défaillance prédite par Windows") }, s.Disks[1]] }; break;
            case "space": s = s with { Disks = [s.Disks[0], s.Disks[1] with { FreeBytes = V(1d) }] }; break;
            case "errors": signals = signals with { Errors = V(new WindowsErrorSummary(12, ["Kernel-Power (12)"])) }; break;
            case "devices": signals = signals with { Devices = V<IReadOnlyList<DeviceProblem>>([new("Carte", 1024, 28)]) }; break;
            case "process":
                s = s with { Processes = [new("steam", 42, V(20d), V(1d), V(1d), V(0d), V(0), V("Valve"), V(@"C:\Games\steam.exe"), "Lanceur")] };
                signals = signals with { Processes = V<IReadOnlyList<ProcessResourceInfo>>([Target()]) }; break;
        }
        return (s, signals);
    }
    internal static ProcessResourceInfo Target() => new() { ProcessId = 42, Name = "steam", Path = @"C:\Games\steam.exe",
        StartedAt = DateTimeOffset.UnixEpoch, MainWindowTitle = "Steam", CanCloseGracefully = true, CanForceClose = true };
    [Fact] public void SeveralProblemsAreCappedAndTopThreeUsesSeverityThenImpact()
    {
        var s = Healthy();
        s = s with { Cpu = s.Cpu with { TemperatureCelsius = V(95d) }, Gpu = [s.Gpu[0] with { TemperatureCelsius = V(95d) }],
            Disks = [s.Disks[0] with { Health = V("Défaillance prédite par Windows") }, s.Disks[1] with { FreeBytes = V(1d) }] };
        var signals = Signals() with { Devices = V<IReadOnlyList<DeviceProblem>>([new("A", 1024, 28), new("B", 1024, 28)]) };
        var result = DiagnosticAnalyzer.Analyze(s, signals);
        Assert.Equal(10, result.Score.Value);
        Assert.Equal(new[] { "disk", "cpu", "gpu" }, result.Top3.Select(i => i.Category));
        Assert.Equal(6, result.Issues.Count);
    }
    [Fact] public void MissingSignalsNeverPenalizeOrBecomeHealthy()
    {
        var s = Healthy();
        s = s with { Cpu = s.Cpu with { TemperatureCelsius = DiagnosticValue<double>.Unavailable("Capteur absent") }, Gpu = [], Disks = [] };
        var result = DiagnosticAnalyzer.Analyze(s, DiagnosticSignals.Unavailable);
        Assert.Equal(new HealthScore(100, true), result.Score);
        Assert.NotEmpty(result.Unavailable);
        Assert.Empty(result.Issues);
    }
    [Theory] [InlineData(9, 200)] [InlineData(11, 200)] [InlineData(10, 100)]
    public void SpaceUsesEitherStrictThreshold(double freeGiB, double totalGiB)
    {
        const double gib = 1024d * 1024 * 1024;
        var s = Healthy();
        s = s with { Disks = [s.Disks[0], s.Disks[1] with { FreeBytes = V(freeGiB * gib), TotalBytes = V(totalGiB * gib) }] };
        Assert.Equal(freeGiB / totalGiB < .1 || freeGiB < 10, DiagnosticAnalyzer.Analyze(s, Signals()).Issues.Any());
    }
    [Fact] public void StartupImpactIsUnavailableAndDoesNotPenalizeOrOfferModification()
    {
        var signals = Signals() with { Startup = V<IReadOnlyList<StartupEntry>>([new("Launcher", "HKCU/Run", DiagnosticValue<string>.Unavailable("Impact non mesuré"))]) };
        var result = DiagnosticAnalyzer.Analyze(Healthy(), signals);
        Assert.Equal(new HealthScore(100, true), result.Score);
        Assert.Contains("sauvegarde et point de restauration requis", Assert.Single(result.Issues).Description);
        Assert.Empty(result.ProposedActions);
        Assert.Empty(result.Top3);
    }
    [Theory] [InlineData("chrome")] [InlineData("Virgil")] [InlineData("svchost")]
    public void NonLauncherIsNeverProposed(string name)
    {
        var (s, signals) = Problem("process");
        s = s with { Processes = [s.Processes[0] with { Name = name }] };
        Assert.Empty(DiagnosticAnalyzer.Analyze(s, signals).Issues);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void ProtectedOrIdentityIncompleteLauncherIsNeverProposed(bool protectedProcess)
    {
        var (s, signals) = Problem("process");
        signals = signals with { Processes = V<IReadOnlyList<ProcessResourceInfo>>([Target() with {
            IsCriticalSystemProcess = protectedProcess, StartedAt = protectedProcess ? DateTimeOffset.UnixEpoch : null }]) };
        Assert.Empty(DiagnosticAnalyzer.Analyze(s, signals).Issues);
    }
    [Fact] public void LargeRamLauncherIsDetectedWithoutCpuPenaltyForUnknownTemperature()
    {
        var (s, signals) = Problem("process");
        s = s with { Processes = [s.Processes[0] with { CpuPercent = V(0d), WorkingSetBytes = V(512d * 1024 * 1024) }] };
        Assert.Equal(95, DiagnosticAnalyzer.Analyze(s, signals).Score.Value);
    }
}
