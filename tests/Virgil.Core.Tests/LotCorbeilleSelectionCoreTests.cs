using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class LotCorbeilleSelectionCoreTests
{
    [Fact]
    public async Task Recycle_selection_requires_typed_critical_consent_and_never_empties_otherwise()
    {
        var now = DateTimeOffset.UtcNow;
        var temp = new CleanupZonePreview(new(CleanupZoneId.UserTemporaryFiles, "Temp", "", Path.GetFullPath("temp"),
            TimeSpan.Zero, CleanupRiskLevel.Low, "", "", "", 0)
            { Classification = CleanupClassification.Cleanable }, now, 1, 1, 10, 0, [], []);
        var definition = Assert.Single(CleanupZoneCatalog.CreateDefault(), z => z.Id == CleanupZoneId.RecycleBin);
        var recyclePreview = new PersonalFolderPolicy(new NoFolders()).Apply(new(definition, now, 3, 3, 1024, 0, [], []));
        Assert.True(recyclePreview.Definition.IsPersonalFolder);
        CleanupZonePreview[] selection = [temp, recyclePreview];
        var preview = CleanupSelectionAction.CreatePreview(selection);
        Assert.Equal(RiskLevel.Critical, preview.RiskLevel);
        Assert.Equal(Reversibility.Partial, preview.Reversibility);
        var authority = new ConfirmationService(TimeProvider.System);
        var recycle = new CountingRecycle();
        var disk = new NoEffectDisk(recycle);
        var quarantine = new NoEffectQuarantine();
        var execution = new CleanupExecutionService(disk, authority, () => now, recycle, quarantine: quarantine);
        Assert.Throws<ConfirmationRefusedException>(() => authority.Issue(preview, new(ConfirmationLevel.Reinforced, null)));
        AssertNoEffects();
        Assert.Throws<ConfirmationRefusedException>(() => authority.Issue(preview, new(ConfirmationLevel.Critical, "oui")));
        AssertNoEffects();
        var missing = await execution.ExecuteSelectionAsync(selection, null, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(missing.WasRefused);
        Assert.Empty(missing.Steps);
        AssertNoEffects();
        var temporaryOnly = CleanupSelectionAction.CreatePreview([temp]);
        var other = authority.Issue(temporaryOnly, new(RiskConfirmationPolicy.RequiredLevel(temporaryOnly.RiskLevel), null));
        var mismatch = await execution.ExecuteSelectionAsync(selection, other, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(mismatch.WasRefused);
        Assert.Empty(mismatch.Steps);
        AssertNoEffects();
        var plan = authority.Issue(preview, new(ConfirmationLevel.Critical,
            "JE COMPRENDS ET JE CONFIRME : cleanup.selection : (aucune cible)"));
        var accepted = await execution.ExecuteSelectionAsync(selection, plan, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(accepted.WasRefused);
        Assert.Equal(1, recycle.EmptyCalls);
        Assert.Equal(0, recycle.QueryCalls);
        Assert.Equal(0, disk.Calls);
        Assert.Equal(0, quarantine.Calls);
        var replay = await execution.ExecuteSelectionAsync(selection, plan, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(replay.WasRefused);
        Assert.Empty(replay.Steps);
        Assert.Equal(1, recycle.EmptyCalls);
        Assert.Equal(0, recycle.QueryCalls);
        Assert.Equal(0, disk.Calls);
        Assert.Equal(0, quarantine.Calls);

        void AssertNoEffects()
        {
            Assert.Equal(0, recycle.EmptyCalls);
            Assert.Equal(0, recycle.QueryCalls);
            Assert.Equal(0, disk.Calls);
            Assert.Equal(0, quarantine.Calls);
        }
    }

    private sealed class NoFolders : IKnownFoldersProvider
    {
        public IReadOnlyList<KnownFolderPath> GetFolders() => [];
    }
    private sealed class CountingRecycle : IRecycleBinService
    {
        public int QueryCalls { get; private set; }
        public int EmptyCalls { get; private set; }
        public RecycleBinState Query() { QueryCalls++; return new(true, 3, 1024); }
        public RecycleBinActionResult Empty() { EmptyCalls++; return new(true, 3, 1024); }
    }
    private sealed class NoEffectDisk(IRecycleBinService recycle) : ICleanupDisk
    {
        public int Calls { get; private set; }
        public IRecycleBinService RecycleBin { get { Calls++; return recycle; } }
        public CleanupFileState ReadFile(string path) { Calls++; throw new InvalidOperationException(); }
        public void DeleteFile(string path) { Calls++; }
        public IReadOnlyList<string> GetDirectories(string path) { Calls++; return []; }
        public bool HasEntries(string path) { Calls++; return true; }
        public void DeleteEmptyDirectory(string path) { Calls++; }
    }
    private sealed class NoEffectQuarantine : IQuarantineStore
    {
        public int Calls { get; private set; }
        public IReadOnlyList<QuarantineEntry> List() { Calls++; return []; }
        public QuarantineEntry MoveToQuarantine(string path, CleanupZoneDefinition zone, DateTimeOffset now, int retentionDays)
        { Calls++; throw new InvalidOperationException(); }
        public string? ValidateRestore(QuarantineEntry entry) { Calls++; return null; }
        public void Restore(QuarantineEntry entry) { Calls++; }
        public void Purge(QuarantineEntry entry) { Calls++; }
    }
}
