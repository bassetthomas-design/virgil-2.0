using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Diagnostic;
using Virgil.Core.Interventions;
using Virgil.Core.Resources;
using Virgil.Infrastructure.Windows.Diagnostic;
using Xunit;

namespace Virgil.Tests;

public sealed class DiagnosticActionTests
{
    private static ProcessResourceInfo Target() => DiagnosticAnalysisTests.Target();
    private static DiagnosticCloseAction Action(Runtime runtime, ConfirmationService authority, bool force = false, ProcessResourceInfo? target = null) =>
        new(new ProcessActionService(runtime, new ProcessProtectionPolicy(Path.Combine(Path.GetTempPath(), "Windows", "System32"), 999),
            new Restarter(), new InterventionCatalog(), () => DateTimeOffset.UnixEpoch, authority), target ?? Target(), force);
    [Theory] [InlineData("missing")] [InlineData("expired")] [InlineData("different")] [InlineData("reused")] [InlineData("gone")]
    [InlineData("protected")] [InlineData("virgil")] [InlineData("used")] [InlineData("path")] [InlineData("critical")]
    public async Task RefusalsNeverReachProcessEffects(string reason)
    {
        var clock = new Clock(); var gate = new ConfirmationService(clock); var runtime = new Runtime();
        var target = reason == "virgil" ? Target() with { Name = "Virgil" } : Target();
        if (reason == "protected") target = target with { IsCriticalSystemProcess = true };
        if (reason == "critical") target = target with { Name = "svchost" };
        runtime.Identity = new(target.ProcessId, target.Name, target.Path, target.StartedAt, true, false);
        var action = Action(runtime, gate, target: target);
        var preview = await action.PreviewAsync(CancellationToken.None);
        var plan = reason == "missing" ? null : gate.Issue(reason == "different" ? preview with { Title = "Autre" } : preview, new(ConfirmationLevel.Simple, null));
        if (reason == "expired") clock.Advance(TimeSpan.FromMinutes(10));
        if (reason == "used") gate.TryConsume(plan, preview);
        if (reason == "reused") runtime.Identity = runtime.Identity! with { StartedAt = DateTimeOffset.UnixEpoch.AddMinutes(1) };
        if (reason == "path") runtime.Identity = runtime.Identity! with { Path = @"C:\Other\steam.exe" };
        if (reason == "gone") runtime.Identity = null;
        var result = await action.ExecuteAsync(plan, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.True(result.WasRefused);
        Assert.Equal(0, runtime.Close + runtime.KillCount);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CloseUsesExistingServiceMatchingPreviewAndOneUseConsent(bool force)
    {
        var authority = new ConfirmationService(TimeProvider.System); var runtime = new Runtime();
        var action = Action(runtime, authority, force);
        var preview = await action.PreviewAsync(CancellationToken.None);
        Assert.Equal(force ? RiskLevel.Sensitive : RiskLevel.Low, preview.RiskLevel);
        Assert.Equal("42", preview.Parameters["ProcessId"]);
        Assert.Equal(Target().Path, preview.Parameters["Path"]);
        Assert.NotEmpty(preview.Parameters["StartedAt"]);
        if (force) Assert.Throws<ConfirmationRefusedException>(() => authority.Issue(preview, new(ConfirmationLevel.Simple, null)));
        var plan = authority.Issue(preview, new(force ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple, null));
        Assert.True((await action.ExecuteAsync(plan, CancellationToken.None)).Succeeded);
        Assert.Equal(force ? 0 : 1, runtime.Close);
        Assert.Equal(force ? 1 : 0, runtime.KillCount);
        Assert.True((await action.ExecuteAsync(plan, CancellationToken.None)).WasRefused);
        Assert.Equal(1, runtime.Close + runtime.KillCount);
    }
    [Fact]
    public async Task OperationalFailureAfterKillAttemptIsNotReportedAsRefusalBeforeEffect()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var runtime = new Runtime { FailKill = true };
        var action = Action(runtime, authority, force: true);
        var preview = await action.PreviewAsync(CancellationToken.None);
        var plan = authority.Issue(preview, new(ConfirmationLevel.Reinforced, null));
        var result = await action.ExecuteAsync(plan, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.False(result.WasRefused);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(1, runtime.KillCount);
    }
    [Theory] [InlineData(DiagnosticSuggestion.Cleanup)] [InlineData(DiagnosticSuggestion.Updates)]
    public async Task NavigationOnlyReportsAnOpeningAndRequiresConsent(DiagnosticSuggestion destination)
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var action = new DiagnosticNavigationAction(authority, destination);
        Assert.True((await action.ExecuteAsync(null, CancellationToken.None)).WasRefused);
        var preview = await action.PreviewAsync(CancellationToken.None);
        Assert.False(preview.RequiresAdmin);
        Assert.Contains("Aucune sélection imposée", Assert.Single(preview.Effects));
        var plan = authority.Issue(preview, new(ConfirmationLevel.Simple, null));
        Assert.True((await action.ExecuteAsync(plan, CancellationToken.None)).Succeeded);
        Assert.True((await action.ExecuteAsync(plan, CancellationToken.None)).WasRefused);
        // Ce type n'a aucune dépendance vers un service d'exécution Nettoyage/Mises à jour.
        Assert.DoesNotContain(typeof(DiagnosticNavigationAction).GetConstructors().SelectMany(c => c.GetParameters()),
            p => p.ParameterType.Name.Contains("Exécution", StringComparison.Ordinal));
    }
    [Fact]
    public async Task ModuleReturnsRealActionsBoundToItsFindings()
    {
        var authority = new ConfirmationService(TimeProvider.System); var runtime = new Runtime();
        var service = new ProcessActionService(runtime, new ProcessProtectionPolicy(Path.Combine(Path.GetTempPath(), "Windows", "System32"), 999),
            new Restarter(), new InterventionCatalog(), () => DateTimeOffset.UnixEpoch, authority);
        var module = new DiagnosticModule(new DiagnosticSampler(new UnusedSource(), TimeProvider.System), processes: service, gate: authority);
        var (snapshot, signals) = DiagnosticAnalysisTests.Problem("process");
        var actions = module.GetActions(DiagnosticAnalyzer.Analyze(snapshot, signals));
        Assert.Equal(2, actions.Count);
        var action = Assert.Single(actions.OfType<DiagnosticCloseAction>(), a => !a.IsForced);
        var preview = await action.PreviewAsync(CancellationToken.None);
        Assert.True((await action.ExecuteAsync(authority.Issue(preview, new(ConfirmationLevel.Simple, null)), CancellationToken.None)).Succeeded);
        Assert.Equal(1, runtime.Close);
        var (space, healthy) = DiagnosticAnalysisTests.Problem("space");
        Assert.IsType<DiagnosticNavigationAction>(Assert.Single(module.GetActions(DiagnosticAnalyzer.Analyze(space, healthy))));
    }
    private sealed class UnusedSource : IDiagnosticSource
    {
        public Task<DiagnosticReading> ReadAsync(CancellationToken token) => throw new InvalidOperationException();
    }
    [Fact] public void DiagnosticCoreContainsNoOsAccessAndStartupAdapterContainsNoWrites()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var source = string.Join("\n", Directory.GetFiles(Path.Combine(root, "src", "Virgil.Core", "Diagnostic"), "*.cs").Select(File.ReadAllText));
        foreach (var forbidden in new[] { "System.Diagnostics", "System.IO", "Microsoft.Win32", "DllImport", "Process.Get", "File.", "Directory.", "Environment.", "RegistryKey" }) Assert.DoesNotContain(forbidden, source);
        var adapter = File.ReadAllText(Path.Combine(root, "src", "Virgil.Infrastructure", "Windows", "Diagnostic", "WindowsDiagnosticSignals.cs"));
        foreach (var forbidden in new[] { "SetValue(", "DeleteValue(", "CreateSubKey(", "DeleteSubKey(", "File.Write", "File.Delete", "Directory.Create", "writable: true" }) Assert.DoesNotContain(forbidden, adapter);
        Assert.Contains("writable: false", adapter);
        Assert.Contains("EventLogReader", adapter);
        Assert.DoesNotContain("FormatDescription", adapter);
    }
    [Fact] [Trait("Category", "Windows")]
    public void RealSystemJournalReadingReturnsDataOrExplicitUnavailableWithoutException()
    {
        var result = WindowsDiagnosticSignals.ReadErrors(CancellationToken.None);
        if (result.IsAvailable) { Assert.True(result.Value.Count >= 0); Assert.True(result.Value.MainSources.Count <= 3); }
        else Assert.Contains("Indisponible", result.Reason);
    }
    [Fact] [Trait("Category", "Windows")]
    public void RealDevicesReadingReturnsProblemCodesOrExplicitUnavailableWithoutException()
    {
        var result = WindowsDiagnosticSignals.ReadDevices(CancellationToken.None);
        if (result.IsAvailable) Assert.All(result.Value, p => Assert.True(p.ProblemCode != 0 || (p.Status & 0x400) != 0));
        else Assert.Contains("Indisponible", result.Reason);
    }
    private sealed class Clock : TimeProvider
    {
        private TimeSpan _elapsed;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + _elapsed;
        public override long GetTimestamp() => _elapsed.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan elapsed) => _elapsed += elapsed;
    }
    private sealed class Runtime : IProcessRuntime
    {
        public ProcessRuntimeIdentity? Identity { get; set; } = new(42, "steam", Target().Path, Target().StartedAt, true, false);
        public int Close, KillCount;
        public bool FailKill { get; init; }
        public ProcessRuntimeIdentity? ReadIdentity(int id) => Identity;
        public bool CloseMainWindow(int id) { Close++; return true; }
        public void Kill(int id) { KillCount++; if (FailKill) throw new InvalidOperationException(); }
        public Task<bool> WaitForExitAsync(int id, TimeSpan timeout, CancellationToken token) => Task.FromResult(true);
        public bool FileExists(string path) => throw new InvalidOperationException();
        public Task<bool> OpenLocationAsync(string path, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
    private sealed class Restarter : IExplorerRestarter
    {
        public Task<InterventionExecutionResult> RestartAsync(InterventionDiagnostic diagnostic, CancellationToken token) => throw new InvalidOperationException();
    }
}
