using System.Collections;
using System.Text.RegularExpressions;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Cleanup;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class MaintenanceContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FindingsOnlyExposeReadOnlyDataAndSnapshotTheirInputs()
    {
        var observations = new List<MaintenanceFinding> { new("temp", "Temp", "Constat") };
        var proposals = new List<ProposedMaintenanceAction>
        {
            new("cleanup.zone.temp", "Temp", RiskLevel.Low, false, false, Reversibility.None)
        };
        var findings = new Findings(observations, proposals);
        observations.Clear();
        proposals.Clear();
        Assert.Single(findings.Observations);
        Assert.Single(findings.ProposedActions);
        Assert.Throws<NotSupportedException>(() => ((IList)findings.Observations).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)findings.ProposedActions).Clear());
        Assert.All(typeof(Findings).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(typeof(Findings).GetMethods(), method =>
            method.Name.Contains("Execute", StringComparison.Ordinal) || method.Name.Contains("Confirm", StringComparison.Ordinal));
        Assert.Equal(typeof(ActionPreview).Assembly, typeof(Findings).Assembly);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task ScanReusesExistingAnalysisWithoutAnyDeletionOrRecycleEffect()
    {
        using var sandbox = new Sandbox();
        var disk = new RecordingDisk();
        var previewService = new CleanupPreviewService(new[] { sandbox.Zone }, () => Now, recycleBinService: disk);
        var execution = new CleanupExecutionService(disk, new ConfirmationService(TimeProvider.System), () => Now);
        IMaintenanceModule module = new CleanupMaintenanceModule(
            new CleanupAnalyzer(previewService, new CleanupStorageAnalyzer(Array.Empty<string>())), execution);
        var legacy = Assert.Single(await previewService.PreviewAsync(null, CancellationToken.None));
        var filesBefore = sandbox.Files();
        var findings = Assert.IsType<CleanupFindings>(await module.ScanAsync(CancellationToken.None));
        var actual = Assert.Single(findings.Analysis.Zones);
        Assert.Equal(legacy.Definition, actual.Definition);
        Assert.Equal(legacy.GeneratedAt, actual.GeneratedAt);
        Assert.Equal(legacy.EligibleBytes, actual.EligibleBytes);
        Assert.Equal(legacy.EligibleFileCount, actual.EligibleFileCount);
        Assert.Equal(legacy.ExaminedFileCount, actual.ExaminedFileCount);
        Assert.Equal(legacy.ExcludedFileCount, actual.ExcludedFileCount);
        Assert.Equal(legacy.Candidates, actual.Candidates);
        Assert.Equal(legacy.Errors, actual.Errors);
        Assert.Single(findings.Observations);
        Assert.Equal(CleanupActionPreviewFactory.Create(actual).ActionId, Assert.Single(findings.ProposedActions).Id);
        Assert.Equal(filesBefore, sandbox.Files());
        Assert.Empty(disk.Calls);
    }

    [Fact]
    public async Task PreviewIsDeterministicUsesExistingFactoryAndFreezesSelectionAndPolicy()
    {
        var disk = new RecordingDisk();
        var candidates = new List<CleanupCandidate> { Candidate() };
        var extensions = new List<string> { ".tmp" };
        var preview = Preview() with
        {
            Candidates = candidates,
            Definition = Preview().Definition with { AllowedExtensions = extensions, RequiresReinforcedConfirmation = true }
        };
        IMaintenanceAction action = new CleanupMaintenanceAction(
            new CleanupExecutionService(disk, new ConfirmationService(TimeProvider.System)), preview);
        var expected = CleanupActionPreviewFactory.Create(preview);
        var first = await action.PreviewAsync(CancellationToken.None);
        candidates.Clear();
        extensions.Add(".jpg");
        var second = await action.PreviewAsync(CancellationToken.None);
        Assert.Equal(ActionPreviewHasher.Hash(expected), ActionPreviewHasher.Hash(first));
        Assert.Equal(ActionPreviewHasher.Hash(first), ActionPreviewHasher.Hash(second));
        Assert.Equal(RiskLevel.Sensitive, action.RiskLevel);
        Assert.Equal(first.ActionId, action.Id);
        Assert.Equal(first.RequiresAdmin, action.RequiresAdmin);
        Assert.Equal(first.RebootRequired, action.RebootRequired);
        Assert.Equal(first.Reversibility, action.Reversibility);
        Assert.Empty(disk.Calls);
    }

    [Theory]
    [InlineData("absent", false, "Plan de confirmation absent")]
    [InlineData("expire", false, "expir")]
    [InlineData("utilise", false, "utilis")]
    [InlineData("hash", false, "aperçu")]
    [InlineData("apercuExpire", false, "Previsualisation expiree")]
    [InlineData("absent", true, "Plan de confirmation absent")]
    [InlineData("expire", true, "expir")]
    [InlineData("utilise", true, "utilis")]
    [InlineData("hash", true, "aperçu")]
    [InlineData("apercuExpire", true, "Previsualisation expiree")]
    public async Task RefusedPlansReturnFrenchReasonBeforeAnyDiskOrRecycleCall(string reason, bool recycle, string message)
    {
        var clock = new Clock();
        var authority = new ConfirmationService(clock);
        var disk = new RecordingDisk();
        var preview = Preview(recycle);
        var action = new CleanupMaintenanceAction(new CleanupExecutionService(disk, authority, () => clock.GetUtcNow()), preview);
        var displayed = await action.PreviewAsync(CancellationToken.None);
        ConfirmedPlan? plan = reason == "absent" ? null : Issue(authority, reason == "hash"
            ? displayed with { Effects = new[] { "Autre effet" } } : displayed);
        if (reason == "utilise") Assert.True(authority.TryConsume(plan, displayed).IsAccepted);
        if (reason == "expire") clock.Advance(TimeSpan.FromMinutes(10));
        if (reason == "apercuExpire") clock.Advance(TimeSpan.FromMinutes(11));

        var result = await action.ExecuteAsync(plan, CancellationToken.None);

        Assert.True(result.WasRefused);
        Assert.False(result.Succeeded);
        Assert.Contains(message, result.RefusalReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Effects);
        Assert.Empty(result.RestorationItems);
        Assert.Equal(0, Assert.IsType<CleanupMaintenanceResult>(result).Step.DeletedFiles);
        Assert.Empty(disk.Calls); // Inclut suppression fichier, dossier et vidage de corbeille.
    }

    [Fact]
    public async Task CancellationIsHonoredByScanPreviewAndExecutionWithoutEffects()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var disk = new RecordingDisk();
        var service = new CleanupExecutionService(disk, new ConfirmationService(TimeProvider.System), () => Now);
        var action = new CleanupMaintenanceAction(service, Preview());
        var module = new CleanupMaintenanceModule(new CleanupAnalyzer(
            new CleanupPreviewService(Array.Empty<CleanupZoneDefinition>()),
            new CleanupStorageAnalyzer(Array.Empty<string>())), service);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => module.ScanAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.PreviewAsync(cancellation.Token));
        var result = Assert.IsType<CleanupMaintenanceResult>(await action.ExecuteAsync(null, cancellation.Token));
        Assert.Equal(CleanupStepStatus.Cancelled, result.Step.Status);
        Assert.False(result.WasRefused);
        Assert.Empty(disk.Calls);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task ContractAndLegacyExecutionHaveIdenticalSandboxEffectsAndMetrics()
    {
        using var sandbox = new Sandbox();
        var clock = new Clock();
        var authority = new ConfirmationService(clock);
        var disk = new RecordingDisk(new CleanupDisk());
        var service = new CleanupExecutionService(disk, authority, () => Now);
        var previews = new CleanupPreviewService(new[] { sandbox.Zone }, () => Now, recycleBinService: disk);
        var preview = Assert.Single(await previews.PreviewAsync(null, CancellationToken.None));
        var legacy = await service.ExecuteZoneAsync(preview, Issue(authority, CleanupActionPreviewFactory.Create(preview)), null, CancellationToken.None);
        var remaining = sandbox.Files();
        var calls = disk.Calls.ToArray();
        Assert.Equal(1, legacy.DeletedFiles);
        Assert.Equal(32, legacy.DeletedBytes);
        Assert.DoesNotContain("old.tmp", remaining);
        Assert.Contains("recent.tmp", remaining);
        Assert.Contains("photo.jpg", remaining);
        sandbox.Reset();
        disk.Calls.Clear();
        IMaintenanceAction action = new CleanupMaintenanceAction(service, preview);
        var result = Assert.IsType<CleanupMaintenanceResult>(await action.ExecuteAsync(
            Issue(authority, await action.PreviewAsync(CancellationToken.None)), CancellationToken.None));
        Assert.True(result.Succeeded);
        Assert.False(result.WasRefused);
        Assert.Equal(legacy.Status, result.Step.Status);
        Assert.Equal(legacy.DeletedFiles, result.Step.DeletedFiles);
        Assert.Equal(legacy.DeletedBytes, result.Step.DeletedBytes);
        Assert.Equal(legacy.SkippedFiles, result.Step.SkippedFiles);
        Assert.Equal(legacy.ErrorFiles, result.Step.ErrorFiles);
        Assert.Equal(legacy.Errors, result.Step.Errors);
        Assert.Equal(remaining, sandbox.Files());
        Assert.Equal(calls, disk.Calls);
    }

    [Fact]
    public void CleanupContractAndModifiedExecutionContainNoDirectFileOrDirectoryAccess()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        foreach (var name in new[] { "CleanupMaintenanceModule.cs", "CleanupMaintenanceAction.cs", "CleanupExecutionService.cs", "ICleanupDisk.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "Virgil.Core", "Cleanup", name));
            Assert.DoesNotMatch(new Regex(@"\b(?:System\.IO\.)?(?:File|Directory|FileInfo|DirectoryInfo|DriveInfo)\s*(?:\.|\()"), source);
            Assert.DoesNotContain("DllImport", source);
        }
        Assert.Equal(typeof(ConfirmedPlan), typeof(IMaintenanceAction).GetMethod("ExecuteAsync")!.GetParameters()[0].ParameterType);
        Assert.DoesNotContain(typeof(IMaintenanceAction).GetMethods().SelectMany(method => method.GetParameters()),
            parameter => parameter.ParameterType == typeof(bool));
    }

    private static ConfirmedPlan Issue(ConfirmationService authority, ActionPreview preview) => authority.Issue(preview,
        new ConfirmationConsent(RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel), null));

    private static CleanupCandidate Candidate() => new(CleanupZoneId.UserTemporaryFiles, "zone/old.tmp", "old.tmp", 32, Now.AddDays(-2), true, null);

    private static CleanupZonePreview Preview(bool recycle = false) => new(
        new CleanupZoneDefinition(recycle ? CleanupZoneId.RecycleBin : CleanupZoneId.UserTemporaryFiles,
            "Zone test", "Analyse en lecture seule", "zone", TimeSpan.FromDays(1), CleanupRiskLevel.Low,
            "Attention", "Suppression", "Documents", 0), Now, 1, 1, 32, 0,
        recycle ? Array.Empty<CleanupCandidate>() : new[] { Candidate() }, Array.Empty<string>());

    private sealed class Clock : TimeProvider
    {
        private TimeSpan _elapsed;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _elapsed.Ticks;
        public override DateTimeOffset GetUtcNow() => Now + _elapsed;
        public void Advance(TimeSpan elapsed) => _elapsed += elapsed;
    }

    private sealed class RecordingDisk : ICleanupDisk, IRecycleBinService
    {
        private readonly ICleanupDisk? _real;
        public RecordingDisk(ICleanupDisk? real = null) => _real = real;
        public List<string> Calls { get; } = new();
        public IRecycleBinService RecycleBin => this;
        public CleanupFileState ReadFile(string path)
        {
            Calls.Add("Read:" + path);
            return _real?.ReadFile(path) ?? new CleanupFileState(32, Now.AddDays(-2));
        }
        public void DeleteFile(string path) { Calls.Add("Delete:" + path); _real?.DeleteFile(path); }
        public IReadOnlyList<string> GetDirectories(string path)
        {
            Calls.Add("Directories:" + path);
            return _real?.GetDirectories(path) ?? Array.Empty<string>();
        }
        public bool HasEntries(string path) { Calls.Add("Entries:" + path); return _real?.HasEntries(path) ?? false; }
        public void DeleteEmptyDirectory(string path) { Calls.Add("DeleteDirectory:" + path); _real?.DeleteEmptyDirectory(path); }
        public RecycleBinState Query() { Calls.Add("QueryRecycleBin"); return new(true, 1, 32); }
        public RecycleBinActionResult Empty() { Calls.Add("EmptyRecycleBin"); return new(true, 1, 32); }
    }

    private sealed class Sandbox : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "VirgilMaintenance-" + Guid.NewGuid());
        public Sandbox() => Reset();
        public CleanupZoneDefinition Zone => Preview().Definition with { RootPath = _root };
        public void Reset()
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "empty"));
            Write("old.tmp", Now.AddDays(-2));
            Write("recent.tmp", Now);
            Write("photo.jpg", Now.AddDays(-2));
        }
        private void Write(string name, DateTimeOffset date)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllBytes(path, new byte[32]);
            File.SetLastWriteTimeUtc(path, date.UtcDateTime);
        }
        public string[] Files() => Directory.GetFiles(_root).Select(Path.GetFileName).OrderBy(name => name).Cast<string>().ToArray();
        public void Dispose() => Directory.Delete(_root, true);
    }
}
