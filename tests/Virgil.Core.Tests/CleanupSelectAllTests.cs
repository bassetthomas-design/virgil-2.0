using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Cleanup;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupSelectAllTests
{
    [Fact]
    public async Task Tampered_row_after_confirmation_is_refused_before_any_effect()
    {
        var first = CleanupConfirmationSummaryTests.Row(CleanupZoneId.UserTemporaryFiles, "Temp");
        var second = CleanupConfirmationSummaryTests.Row(CleanupZoneId.SoftwareLeftovers, "Ancien outil");
        var authority = new ConfirmationService(TimeProvider.System);
        var plan = Issue(authority, CleanupSelectionAction.CreatePreview([first, second]));
        var disk = new NoEffectDisk();
        var quarantine = new NoEffectQuarantine();
        var execution = new CleanupExecutionService(disk, authority, () => first.GeneratedAt, quarantine: quarantine);
        // La deuxième ligne a changé : la première ne doit pas être exécutée.
        second = second with { EligibleBytes = second.EligibleBytes + 1 };
        var outcome = await execution.ExecuteSelectionAsync([first, second], plan, null, CancellationToken.None);
        Assert.True(outcome.WasRefused);
        Assert.Empty(outcome.Steps);
        Assert.Equal(0, disk.Calls);
        Assert.Equal(0, quarantine.Calls);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task Select_all_then_confirm_quarantines_and_restore_returns_identical_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "VirgilSelectAll-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var expected = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var selection = new List<CleanupZonePreview>();
            for (var i = 0; i < 2; i++)
            {
                var zoneRoot = Path.Combine(root, "zone" + i);
                Directory.CreateDirectory(zoneRoot);
                var zone = new CleanupZoneDefinition(CleanupZoneId.UserTemporaryFiles, "Temporaires " + i, "", zoneRoot,
                    TimeSpan.FromDays(1), CleanupRiskLevel.Low, "", "", "", i);
                var candidates = new List<CleanupCandidate>();
                for (var j = 0; j < 2; j++)
                {
                    var path = Path.Combine(zoneRoot, "old-" + j + ".tmp");
                    byte[] bytes = [(byte)i, (byte)j, 0, 127, 255];
                    await File.WriteAllBytesAsync(path, bytes);
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
                    expected.Add(path, bytes);
                    candidates.Add(new(zone.Id, path, Path.GetFileName(path), bytes.Length, File.GetLastWriteTimeUtc(path), true, null));
                }
                selection.Add(new(zone, DateTimeOffset.UtcNow, 2, 2, 10, 0, candidates, []));
            }
            var store = new QuarantineStore(Path.Combine(root, "quarantine"));
            var authority = new ConfirmationService(TimeProvider.System);
            var execution = new CleanupExecutionService(new CleanupDisk(), authority, quarantine: store);
            var action = new CleanupSelectionAction(execution, selection);
            var preview = await action.PreviewAsync(CancellationToken.None);
            Assert.Equal(4, preview.Targets.Count);
            Assert.Empty(store.List());
            Assert.All(expected.Keys, path => Assert.True(File.Exists(path)));
            var result = Assert.IsType<CleanupSelectionActionResult>(
                await action.ExecuteAsync(Issue(authority, preview), CancellationToken.None));
            Assert.True(result.Succeeded, string.Join("\n", result.Errors));
            Assert.Equal(4, result.Selection.Steps.Sum(s => s.QuarantinedFiles));
            Assert.All(result.Selection.Steps, s => Assert.Equal(0, s.DeletedFiles));
            Assert.All(expected.Keys, path => Assert.False(File.Exists(path)));
            var entries = store.List();
            Assert.Equal(expected.Keys.OrderBy(p => p), entries.Select(e => e.OriginalPath).OrderBy(p => p));
            var restore = new QuarantineMaintenanceAction(store, authority, entries, false);
            var restored = await restore.ExecuteAsync(Issue(authority, await restore.PreviewAsync(CancellationToken.None)), CancellationToken.None);
            Assert.True(restored.Succeeded, string.Join("\n", restored.Errors));
            foreach (var pair in expected) Assert.Equal(pair.Value, await File.ReadAllBytesAsync(pair.Key));
            Assert.Empty(store.List());
            Assert.Equal(expected.Keys.OrderBy(p => p), Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).OrderBy(p => p));
        }
        finally { Directory.Delete(root, true); }
    }

    private static ConfirmedPlan Issue(ConfirmationService authority, ActionPreview preview) =>
        authority.Issue(preview, new ConfirmationConsent(RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel), null));

    private sealed class NoEffectDisk : ICleanupDisk, IRecycleBinService
    {
        public int Calls { get; private set; }
        public IRecycleBinService RecycleBin => this;
        public CleanupFileState ReadFile(string path) { Calls++; throw new InvalidOperationException(); }
        public void DeleteFile(string path) { Calls++; }
        public IReadOnlyList<string> GetDirectories(string path) { Calls++; return []; }
        public bool HasEntries(string path) { Calls++; return true; }
        public void DeleteEmptyDirectory(string path) { Calls++; }
        public RecycleBinState Query() { Calls++; return new(true, 0, 0); }
        public RecycleBinActionResult Empty() { Calls++; return new(true, 0, 0); }
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
