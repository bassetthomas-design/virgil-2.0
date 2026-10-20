using Virgil.Infrastructure.Windows;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Cleanup;
using Virgil.Core.Interventions;
using Xunit;

namespace Virgil.Tests;

public sealed class InterventionCleanupConfirmedPlanTests
{
    private static InterventionDiagnostic Diagnostic(InterventionId id) => new()
    {
        IsAvailable = true,
        Definition = new InterventionCatalog().GetAll().Single(d => d.Id == id)
    };
    private static ConfirmedPlan Issue(ConfirmationService authority, ActionPreview preview) => authority.Issue(preview,
        new ConfirmationConsent(RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel), null));

    [Theory]
    [InlineData(InterventionId.RestartExplorer, "missing")]
    [InlineData(InterventionId.RestartExplorer, "authority")]
    [InlineData(InterventionId.RestartExplorer, "expired")]
    [InlineData(InterventionId.RestartExplorer, "other")]
    [InlineData(InterventionId.RestartExplorer, "reused")]
    [InlineData(InterventionId.FlushDns, "missing")]
    [InlineData(InterventionId.FlushDns, "authority")]
    [InlineData(InterventionId.FlushDns, "expired")]
    [InlineData(InterventionId.FlushDns, "other")]
    [InlineData(InterventionId.FlushDns, "reused")]
    [InlineData(InterventionId.ResetWinsock, "simple")]
    public async Task InterventionRefusalHasNoEffect(InterventionId id, string scenario)
    {
        var clock = new Clock(); var authority = new ConfirmationService(clock); var effects = new Effects();
        var service = new InterventionExecutionService(effects, effects, authority);
        var diagnostic = Diagnostic(id);
        var preview = InterventionActionPreviewFactory.Create(diagnostic.Definition);
        if (scenario == "other") preview = InterventionActionPreviewFactory.Create(Diagnostic(InterventionId.SfcScan).Definition);
        if (scenario == "simple") preview = preview with { RiskLevel = RiskLevel.Low };
        var plan = scenario == "missing" ? null : Issue(scenario == "authority" ? new ConfirmationService(clock) : authority, preview);
        if (scenario == "expired") clock.Advance();
        if (scenario == "reused")
        {
            Assert.Equal(InterventionStatus.Completed, (await service.ExecuteAsync(diagnostic, plan, CancellationToken.None)).Status);
            effects.Calls = 0;
        }
        var result = await service.ExecuteAsync(diagnostic, plan, CancellationToken.None);
        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.StartsWith("Intervention annulee.\n", result.ReadableError);
        Assert.Equal(0, effects.Calls);
    }

    [Theory]
    [InlineData(InterventionId.RestartExplorer)]
    [InlineData(InterventionId.FlushDns)]
    [InlineData(InterventionId.ResetWinsock)]
    public async Task InterventionConsumesPlanBeforeOneEffect(InterventionId id)
    {
        var authority = new ConfirmationService(TimeProvider.System); var effects = new Effects();
        var diagnostic = Diagnostic(id); var service = new InterventionExecutionService(effects, effects, authority);
        var plan = Issue(authority, InterventionActionPreviewFactory.Create(diagnostic.Definition));
        Assert.Equal(InterventionStatus.Completed, (await service.ExecuteAsync(diagnostic, plan, CancellationToken.None)).Status);
        Assert.Equal(InterventionStatus.Failed, (await service.ExecuteAsync(diagnostic, plan, CancellationToken.None)).Status);
        Assert.Equal(1, effects.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterventionGuardsNeverCallGate(bool unavailable)
    {
        var gate = new RejectGate(); var effects = new Effects(); var diagnostic = Diagnostic(InterventionId.FlushDns);
        diagnostic = unavailable ? diagnostic with { IsAvailable = false } : diagnostic with
        { Definition = diagnostic.Definition with { RequiresAdministrator = false } };
        var result = await new InterventionExecutionService(effects, effects, gate).ExecuteAsync(diagnostic, null, CancellationToken.None);
        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.Equal(0, gate.Calls); Assert.Equal(0, effects.Calls);
    }

    [Fact]
    public void InterventionPreviewExcludesDiagnosticObservations()
    {
        var diagnostic = Diagnostic(InterventionId.FlushDns);
        var changed = diagnostic with { StateBefore = "changed", Warnings = ["changed"], TechnicalData = new Dictionary<string, string> { ["time"] = "changed" } };
        var preview = InterventionActionPreviewFactory.Create(diagnostic.Definition);
        Assert.Equal("interventions.flushdns", preview.ActionId);
        Assert.Equal(new[] { "FlushDns" }, preview.Targets);
        Assert.Equal(new[] { "Category", "RequiresAdministrator", "RiskLevel" }, preview.Parameters.Keys.OrderBy(k => k));
        Assert.Equal(ActionPreviewHasher.Hash(preview), ActionPreviewHasher.Hash(InterventionActionPreviewFactory.Create(changed.Definition)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("authority")]
    [InlineData("expired")]
    [InlineData("zone")]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("size")]
    [InlineData("date")]
    [InlineData("simple")]
    [InlineData("reused")]
    public async Task CleanupRefusalNeverDeletes(string scenario)
    {
        using var files = new Files(); var preview = files.Preview;
        var clock = new Clock(); var authority = new ConfirmationService(clock); var effects = new Effects();
        var service = new CleanupExecutionService(new CleanupDisk(), authority, () => preview.GeneratedAt, effects);
        if (scenario == "simple") preview = preview with { Definition = preview.Definition with { RequiresReinforcedConfirmation = true } };
        var action = CleanupActionPreviewFactory.Create(preview);
        if (scenario == "simple") action = action with { RiskLevel = RiskLevel.Low };
        var plan = scenario == "missing" ? null : Issue(scenario == "authority" ? new ConfirmationService(clock) : authority, action);
        var candidate = preview.Candidates[0];
        preview = scenario switch
        {
            "zone" => preview with { Definition = preview.Definition with { Id = CleanupZoneId.UserCrashDumps } },
            "added" => preview with { Candidates = [candidate, candidate with { FullPath = candidate.FullPath + ".other" }] },
            "removed" => preview with { Candidates = [] },
            "size" => preview with { Candidates = [candidate with { SizeBytes = candidate.SizeBytes + 1 }] },
            "date" => preview with { Candidates = [candidate with { LastWriteTimeUtc = candidate.LastWriteTimeUtc.AddSeconds(1) }] },
            _ => preview
        };
        if (scenario == "expired") clock.Advance();
        if (scenario == "reused") Assert.True(authority.TryConsume(plan, action).IsAccepted);
        var result = await service.ExecuteZoneAsync(preview, plan, null, CancellationToken.None);
        Assert.Equal(CleanupStepStatus.Cancelled, result.Status);
        Assert.StartsWith("Nettoyage annule.\n", Assert.Single(result.Errors));
        Assert.Equal(0, result.DeletedFiles); Assert.True(File.Exists(files.Path)); Assert.Equal(0, effects.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("authority")]
    [InlineData("expired")]
    [InlineData("zone")]
    [InlineData("count")]
    [InlineData("bytes")]
    [InlineData("simple")]
    [InlineData("reused")]
    public async Task RecycleRefusalNeverEmpties(string scenario)
    {
        using var files = new Files(); var preview = files.Preview with
        { Definition = files.Preview.Definition with { Id = CleanupZoneId.RecycleBin, RequiresReinforcedConfirmation = true }, Candidates = [] };
        var clock = new Clock(); var authority = new ConfirmationService(clock); var effects = new Effects();
        var action = CleanupActionPreviewFactory.Create(preview);
        if (scenario == "simple") action = action with { RiskLevel = RiskLevel.Low };
        var plan = scenario == "missing" ? null : Issue(scenario == "authority" ? new ConfirmationService(clock) : authority, action);
        preview = scenario switch
        {
            "zone" => preview with { Definition = preview.Definition with { Id = CleanupZoneId.UserTemporaryFiles } },
            "count" => preview with { EligibleFileCount = 2 },
            "bytes" => preview with { EligibleBytes = 100 },
            _ => preview
        };
        if (scenario == "expired") clock.Advance();
        if (scenario == "reused") Assert.True(authority.TryConsume(plan, action).IsAccepted);
        var result = await new CleanupExecutionService(new CleanupDisk(), authority, () => preview.GeneratedAt, effects)
            .ExecuteZoneAsync(preview, plan, null, CancellationToken.None);
        Assert.Equal(CleanupStepStatus.Cancelled, result.Status); Assert.Equal(0, result.DeletedFiles);
        Assert.Equal(0, effects.Calls); Assert.True(File.Exists(files.Path));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("information")]
    [InlineData("cancelled")]
    [InlineData("recycle-expired")]
    public async Task CleanupGuardsPrecedeGate(string scenario)
    {
        using var files = new Files(); var preview = files.Preview; var gate = new RejectGate(); var effects = new Effects();
        if (scenario == "information") preview = preview with { Definition = preview.Definition with { IsExecutable = false } };
        if (scenario == "recycle-expired") preview = preview with { Definition = preview.Definition with { Id = CleanupZoneId.RecycleBin } };
        var now = scenario.Contains("expired", StringComparison.Ordinal) ? preview.GeneratedAt.AddMinutes(11) : preview.GeneratedAt;
        var result = await new CleanupExecutionService(new CleanupDisk(), gate, () => now, effects).ExecuteZoneAsync(preview, null, null,
            new CancellationToken(scenario == "cancelled"));
        Assert.Equal(scenario == "information" ? CleanupStepStatus.Skipped : scenario == "cancelled" ? CleanupStepStatus.Cancelled : CleanupStepStatus.Expired, result.Status);
        if (scenario.Contains("expired", StringComparison.Ordinal)) Assert.Equal("Previsualisation expiree. Relancer l'analyse.", Assert.Single(result.Errors));
        Assert.Equal(0, gate.Calls); Assert.Equal(0, effects.Calls); Assert.True(File.Exists(files.Path));
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task CleanupDeletesOnceAndReusedPlanHasNoEffect()
    {
        using var files = new Files(); var preview = files.Preview; var authority = new ConfirmationService(TimeProvider.System);
        var service = new CleanupExecutionService(new CleanupDisk(), authority, () => preview.GeneratedAt, new Effects());
        var plan = Issue(authority, CleanupActionPreviewFactory.Create(preview));
        var first = await service.ExecuteZoneAsync(preview, plan, null, CancellationToken.None);
        Assert.Equal(CleanupStepStatus.Completed, first.Status); Assert.Equal(1, first.DeletedFiles); Assert.False(File.Exists(files.Path));
        File.WriteAllText(files.Path, "replacement");
        Assert.Equal(CleanupStepStatus.Cancelled, (await service.ExecuteZoneAsync(preview, plan, null, CancellationToken.None)).Status);
        Assert.Equal("replacement", File.ReadAllText(files.Path));
    }

    [Theory]
    [InlineData(CleanupRiskLevel.Low, false, RiskLevel.Low)]
    [InlineData(CleanupRiskLevel.Medium, true, RiskLevel.Sensitive)]
    [InlineData(CleanupRiskLevel.High, false, RiskLevel.Sensitive)]
    public void CleanupRiskIncludesIndependentReinforcedFlag(CleanupRiskLevel source, bool reinforced, RiskLevel expected)
    {
        using var files = new Files(); var preview = files.Preview with { Definition = files.Preview.Definition with
        { RiskLevel = source, RequiresReinforcedConfirmation = reinforced } };
        var action = CleanupActionPreviewFactory.Create(preview);
        Assert.Equal(expected, action.RiskLevel); Assert.Equal(new[] { files.Path }, action.Targets);
        Assert.Equal(preview.GeneratedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture), action.Parameters["GeneratedAt"]);
    }

    private sealed class Clock : TimeProvider
    {
        private TimeSpan _elapsed;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + _elapsed;
        public override long GetTimestamp() => _elapsed.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance() => _elapsed += TimeSpan.FromMinutes(10);
    }
    private sealed class RejectGate : IConfirmedPlanGate
    {
        public int Calls { get; private set; }
        public PlanValidationResult Validate(ConfirmedPlan? plan, ActionPreview preview) => throw new InvalidOperationException();
        public PlanValidationResult TryConsume(ConfirmedPlan? plan, ActionPreview preview) { Calls++; return new(false, PlanRejectionReason.Missing, "Refus"); }
    }
    private sealed class Effects : IInterventionElevatedHelperClient, IExplorerRestarter, IRecycleBinService
    {
        public int Calls { get; set; }
        public Task<ElevatedInterventionResult> ExecuteAsync(InterventionDefinition definition, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new ElevatedInterventionResult { Status = InterventionStatus.Completed }); }
        public Task<InterventionExecutionResult> RestartAsync(InterventionDiagnostic diagnostic, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new InterventionExecutionResult { Action = diagnostic.Definition, Status = InterventionStatus.Completed }); }
        public RecycleBinState Query() => new(true, 1, 4);
        public RecycleBinActionResult Empty() { Calls++; return new(true, 1, 4); }
    }
    private sealed class Files : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Virgil.Tests", Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public CleanupZonePreview Preview { get; }
        public Files()
        {
            Directory.CreateDirectory(_root); Path = System.IO.Path.Combine(_root, "old.tmp"); File.WriteAllText(Path, "test");
            var date = DateTimeOffset.UtcNow.AddDays(-3); File.SetLastWriteTimeUtc(Path, date.UtcDateTime);
            var definition = new CleanupZoneDefinition(CleanupZoneId.UserTemporaryFiles, "Test", "Test", _root,
                TimeSpan.FromDays(1), CleanupRiskLevel.Low, "", "Suppression", "", 0);
            Preview = new(definition, DateTimeOffset.UtcNow, 1, 1, 4, 0,
                [new CleanupCandidate(definition.Id, Path, "old.tmp", 4, date, true, null)], []);
        }
        public void Dispose() => Directory.Delete(_root, true);
    }
}
