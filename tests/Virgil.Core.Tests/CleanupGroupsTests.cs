using System.Text.RegularExpressions;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupGroupsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static string Root => Path.GetFullPath("sandbox");
    private static string Remnant => Path.Combine(Root, "Dofus");
    private static FootprintFile FileItem => new(Path.Combine(Remnant, "cache.tmp"), 123, Now.AddDays(-100));
    private static SoftwareFootprintCandidate Dofus => new("Dofus", Remnant, Root, [FileItem], [@"HKCU\Software\Dofus"], Now.AddDays(-100));
    private static SoftwareFootprintSnapshot Snapshot => new([], [], [Dofus]);
    private static CleanupZonePreview Preview => Assert.Single(new LeftoverAnalyzer().Analyze(Snapshot, Now));

    [Theory]
    [InlineData(CleanupClassification.Cleanable, CleanupZoneId.UserTemporaryFiles, CleanupGroup.Sur)]
    [InlineData(CleanupClassification.AdvancedCleanable, CleanupZoneId.RecycleBin, CleanupGroup.Sur)]
    [InlineData(CleanupClassification.AdvancedCleanable, CleanupZoneId.BrowserChromeCache, CleanupGroup.AVerifier)]
    [InlineData(CleanupClassification.AdvancedCleanable, CleanupZoneId.WindowsOld, CleanupGroup.AVerifier)]
    [InlineData(CleanupClassification.AdvancedCleanable, CleanupZoneId.SoftwareLeftovers, CleanupGroup.RestesLogiciels)]
    public void EveryZoneHasItsGroup(CleanupClassification classification, CleanupZoneId id, CleanupGroup group) =>
        Assert.Equal(group, (Preview.Definition with { Id = id, Classification = classification }).Group);

    [Theory]
    [InlineData(90, 1, false)] [InlineData(91, 1, true)] [InlineData(1, 524288000, false)]
    [InlineData(1, 524288001, true)] [InlineData(91, 524288001, true)]
    public void DownloadsUseStrictAgeOrSizeThresholds(int days, long bytes, bool expected) =>
        Assert.Equal(expected, AdditionalCleanupScanner.IsOldOrLarge(FileItem with { LastWriteUtc = Now.AddDays(-days), SizeBytes = bytes }, Now));

    [Fact]
    public async Task FakePortDetectsDofusWithMetricsKeysAndLastUseWithoutRegistryWriteApi()
    {
        var source = new Source(Snapshot);
        var folders = new Folders([]);
        var scanner = new AdditionalCleanupScanner(folders, new Downloads(), source, new LeftoverAnalyzer(), new PersonalFolderPolicy(folders), new Clock());
        var result = Assert.Single(await scanner.ScanAsync(CancellationToken.None));
        Assert.Contains("Dofus (désinstallé)", result.Definition.DisplayName);
        Assert.Equal(123, result.EligibleBytes); Assert.Equal(1, result.EligibleFileCount);
        Assert.Equal(Dofus.LastUsedUtc, result.Definition.LastUsedUtc);
        Assert.Equal(Dofus.RegistryKeys, result.Definition.RegistryKeys);
        Assert.False(result.Definition.NeedsReview);
        Assert.Equal(CleanupGroup.RestesLogiciels, result.Definition.Group);
        Assert.All(result.Candidates, c => Assert.Equal(CleanupGroup.RestesLogiciels, c.Group));
        Assert.Contains("lot ultérieur", result.Definition.Description);
        Assert.Single(typeof(ISoftwareFootprintSource).GetMethods());
        Assert.Equal("ReadAsync", typeof(ISoftwareFootprintSource).GetMethods()[0].Name);
    }

    [Theory]
    [InlineData("installed")] [InlineData("publisher")] [InlineData("installPath")] [InlineData("childPath")] [InlineData("process")] [InlineData("service")]
    public void InstalledNamesAndReferencedParentOrChildPathsAreExcluded(string kind)
    {
        var snapshot = Snapshot;
        snapshot = kind switch
        {
            "installed" => snapshot with { Installed = [new("Dofus", [])] },
            "publisher" => snapshot with { Installed = [new("Dofus Inc.", [])] },
            "installPath" => snapshot with { Installed = [new("Autre nom", [Root])] },
            "childPath" => snapshot with { Installed = [new("Autre nom", [FileItem.Path])] },
            _ => snapshot with { ActivePaths = [FileItem.Path] }
        };
        Assert.Empty(new LeftoverAnalyzer().Analyze(snapshot, Now));
    }

    [Theory]
    [InlineData("Microsoft")] [InlineData("Windows")] [InlineData("NVIDIA")] [InlineData("AMD")] [InlineData("Intel")]
    [InlineData("Realtek")] [InlineData("Antivirus")] [InlineData("Bitdefender")]
    [InlineData(".NET")] [InlineData("VC++")] [InlineData("dotnet")] [InlineData("VC Redist Runtime")] [InlineData("Virgil")]
    public void ProtectedSoftwareIsNeverProposed(string name) =>
        Assert.Empty(new LeftoverAnalyzer().Analyze(Snapshot with { Candidates = [Dofus with { Name = name }] }, Now));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UncertainOrIncompleteInventoryRequiresIndividualReview(bool incomplete)
    {
        var result = Assert.Single(new LeftoverAnalyzer().Analyze(Snapshot with
        { IsComplete = !incomplete, Candidates = [Dofus with { IsUncertain = !incomplete }] }, Now));
        Assert.True(result.Definition.NeedsReview);
        Assert.Contains("à confirmer", result.Definition.DisplayName);
    }

    [Theory]
    [InlineData(KnownFolder.Pictures)] [InlineData(KnownFolder.Videos)] [InlineData(KnownFolder.Music)]
    [InlineData(KnownFolder.Documents)] [InlineData(KnownFolder.Desktop)] [InlineData(KnownFolder.OneDrive)]
    public void EveryPersonalFolderRequiresCriticalAndAnIdentifiedRemnant(KnownFolder kind)
    {
        var policy = new PersonalFolderPolicy(new Folders([new(kind, Root)]));
        var preview = policy.Apply(Preview);
        Assert.True(preview.Definition.IsPersonalFolder);
        Assert.True(Assert.Single(preview.Candidates).IsPersonalFolder);
        Assert.True(policy.CanQuarantine(preview.Definition, FileItem.Path));
        Assert.False(policy.CanQuarantine(preview.Definition, Root));
        Assert.False(policy.CanQuarantine(preview.Definition with
        { RootPath = Root, ValidatedRemnantRoot = Root, RemnantAllowedRoot = Path.GetDirectoryName(Root) }, FileItem.Path));
        Assert.False(policy.CanQuarantine(preview.Definition, Path.Combine(Root, "personnel.tmp")));
        var regular = preview with { Definition = preview.Definition with { Id = CleanupZoneId.UserTemporaryFiles, ValidatedRemnantRoot = null } };
        Assert.Equal(0, policy.Apply(regular).EligibleFileCount);
        var actionPreview = CleanupSelectionAction.CreatePreview([preview]);
        Assert.Equal(RiskLevel.Critical, actionPreview.RiskLevel);
        Assert.Contains("dossier personnel", actionPreview.Title);
        var authority = new ConfirmationService(new Clock());
        Assert.Throws<ConfirmationRefusedException>(() => authority.Issue(actionPreview, new(ConfirmationLevel.Reinforced, null)));
        Assert.Throws<ConfirmationRefusedException>(() => authority.Issue(actionPreview, new(ConfirmationLevel.Critical, "oui")));
        Assert.NotNull(authority.Issue(actionPreview, new(ConfirmationLevel.Critical, ConfirmationService.ExpectedCriticalText(actionPreview))));
        Assert.All(CleanupActionPreviewFactory.Create(preview).Effects, e => Assert.Contains("quarantaine", e));
    }

    [Fact]
    public async Task OnlyTheResolvedDownloadsFolderIsRequestedAndEachFileGetsOneLine()
    {
        var source = new Downloads { Files = [FileItem, FileItem with { Path = Path.Combine(Remnant, "second.tmp") }, FileItem with { LastWriteUtc = Now, SizeBytes = 1 }] };
        var folders = new Folders([new(KnownFolder.Downloads, Root), new(KnownFolder.Desktop, Path.GetFullPath("desktop"))]);
        var scanner = new AdditionalCleanupScanner(folders, source, new Source(new([], [], [])), new LeftoverAnalyzer(), new PersonalFolderPolicy(folders), new Clock());
        var result = await scanner.ScanAsync(CancellationToken.None);
        Assert.Equal(new[] { Root }, source.Requests); Assert.Equal(2, result.Count);
        Assert.All(result, p => { Assert.Single(p.Candidates); Assert.Equal(CleanupGroup.AVerifier, p.Definition.Group); });
    }

    [Fact]
    public void SummaryAndHashBindEntireSelectionAndMaximumRisk()
    {
        var safe = Preview with { Definition = Preview.Definition with { Id = CleanupZoneId.UserTemporaryFiles, Classification = CleanupClassification.Cleanable, RiskLevel = CleanupRiskLevel.Low, RequiresReinforcedConfirmation = false } };
        var sensitive = Preview;
        var one = CleanupSelectionAction.CreatePreview([safe]);
        var two = CleanupSelectionAction.CreatePreview([safe, sensitive]);
        Assert.NotEqual(ActionPreviewHasher.Hash(one), ActionPreviewHasher.Hash(two));
        Assert.Equal("246", two.Parameters["Bytes"]); Assert.Equal("2", two.Parameters["Files"]);
        Assert.Equal("1", two.Parameters["Sensitive"]); Assert.Equal(RiskLevel.Sensitive, two.RiskLevel);
        Assert.Contains("123", two.Parameters[nameof(CleanupGroup.Sur)]);
        Assert.Contains("123", two.Parameters[nameof(CleanupGroup.RestesLogiciels)]);
        Assert.NotEqual(ActionPreviewHasher.Hash(two), ActionPreviewHasher.Hash(CleanupSelectionAction.CreatePreview([sensitive, safe])));
    }

    [Theory]
    [InlineData("absent")] [InlineData("expired")] [InlineData("used")] [InlineData("hash")]
    public async Task InvalidSelectionPlanIsRefusedBeforeAnyDiskOrQuarantineEffect(string kind)
    {
        var clock = new Clock(); var authority = new ConfirmationService(clock); var disk = new NoDisk();
        var preview = Preview; var action = new CleanupSelectionAction(new CleanupExecutionService(disk, authority, () => Now), [preview]);
        var actionPreview = await action.PreviewAsync(CancellationToken.None);
        var plan = kind == "absent" ? null : authority.Issue(kind == "hash" ? actionPreview with { Effects = ["Autre sélection"] } : actionPreview, new(ConfirmationLevel.Reinforced, null));
        if (kind == "used") Assert.True(authority.TryConsume(plan, actionPreview).IsAccepted);
        if (kind == "expired") clock.Advance(TimeSpan.FromMinutes(11));
        var result = await action.ExecuteAsync(plan, CancellationToken.None);
        Assert.True(result.WasRefused); Assert.False(result.Succeeded); Assert.Empty(result.Effects);
        Assert.Equal(0, disk.Calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RecycleBinKeepsReinforcedIrreversibleApiAndCancellationStopsRemainingZones(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var authority = new ConfirmationService(new Clock());
        var disk = new NoDisk { AllowRecycle = true, OnRecycle = cancel ? cancellation.Cancel : null };
        var recycle = Preview with { Definition = Preview.Definition with { Id = CleanupZoneId.RecycleBin }, Candidates = [] };
        var selection = cancel ? new[] { recycle, Preview } : new[] { recycle };
        var action = new CleanupSelectionAction(new CleanupExecutionService(disk, authority, () => Now), selection);
        var preview = await action.PreviewAsync(CancellationToken.None);
        Assert.Equal(cancel ? Reversibility.Partial : Reversibility.None, preview.Reversibility);
        Assert.Equal(RiskLevel.Sensitive, preview.RiskLevel);
        Assert.Throws<ConfirmationRefusedException>(() => authority.Issue(preview, new(ConfirmationLevel.Simple, null)));
        Assert.Equal(0, disk.Calls);
        var plan = authority.Issue(preview, new(ConfirmationLevel.Reinforced, null));
        var result = Assert.IsType<CleanupSelectionActionResult>(await action.ExecuteAsync(plan, cancellation.Token));
        Assert.False(result.WasRefused); Assert.Equal(!cancel, result.Succeeded);
        Assert.Contains(result.Effects, effect => effect.Contains("Corbeille") && effect.Contains("irréversible"));
        Assert.Equal(1, disk.Calls);
        Assert.Equal(2, result.Selection.Steps[0].DeletedFiles);
        Assert.Equal(42, result.Selection.Steps[0].DeletedBytes);
        if (cancel) Assert.Equal(CleanupStepStatus.Cancelled, result.Selection.Steps[1].Status);
        var replay = await action.ExecuteAsync(plan, CancellationToken.None);
        Assert.True(replay.WasRefused); Assert.Empty(replay.Effects); Assert.Equal(1, disk.Calls);
    }

    [Fact]
    public void NewCoreContainsNoFileDirectoryRegistryOrNativeCalls()
    {
        foreach (var name in new[] { "IKnownFoldersProvider", "ISoftwareFootprintSource", "PersonalFolderPolicy", "LeftoverAnalyzer", "AdditionalCleanupScanner", "CleanupSelectionAction" })
        {
            var text = File.ReadAllText(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src", "Virgil.Core", "Cleanup", name + ".cs"));
            Assert.DoesNotContain("Microsoft.Win32", text); Assert.DoesNotContain("DllImport", text);
            Assert.DoesNotMatch(new Regex(@"\b(?:System\.IO\.)?(?:File|Directory|FileInfo|DirectoryInfo)\s*(?:\.|\()"), text);
        }
    }

    private sealed class Folders(IReadOnlyList<KnownFolderPath> paths) : IKnownFoldersProvider
    { public IReadOnlyList<KnownFolderPath> GetFolders() => paths; }
    private sealed class Source(SoftwareFootprintSnapshot snapshot) : ISoftwareFootprintSource
    { public Task<SoftwareFootprintSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot); }
    private sealed class Downloads : IDownloadsSource
    {
        public IReadOnlyList<FootprintFile> Files { get; init; } = [];
        public List<string> Requests { get; } = [];
        public IReadOnlyList<FootprintFile> ReadFiles(string downloadsRoot, CancellationToken cancellationToken)
        { Requests.Add(downloadsRoot); return Files; }
    }
    private sealed class Clock : TimeProvider
    {
        private TimeSpan _elapsed;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _elapsed.Ticks;
        public override DateTimeOffset GetUtcNow() => Now + _elapsed;
        public void Advance(TimeSpan value) => _elapsed += value;
    }
    private sealed class NoDisk : ICleanupDisk
    {
        public int Calls { get; private set; }
        public bool AllowRecycle { get; init; }
        public Action? OnRecycle { get; init; }
        public IRecycleBinService RecycleBin => new Recycle(this);
        public CleanupFileState ReadFile(string path) { Calls++; throw new InvalidOperationException(); }
        public void DeleteFile(string path) { Calls++; throw new InvalidOperationException(); }
        public IReadOnlyList<string> GetDirectories(string path) { Calls++; throw new InvalidOperationException(); }
        public bool HasEntries(string path) { Calls++; throw new InvalidOperationException(); }
        public void DeleteEmptyDirectory(string path) { Calls++; throw new InvalidOperationException(); }
        private sealed class Recycle(NoDisk owner) : IRecycleBinService
        {
            public RecycleBinState Query() => throw new InvalidOperationException();
            public RecycleBinActionResult Empty()
            { owner.Calls++; if (!owner.AllowRecycle) throw new InvalidOperationException(); owner.OnRecycle?.Invoke(); return new(true, 2, 42); }
        }
    }
}
