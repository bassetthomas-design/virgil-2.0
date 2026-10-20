using System.Text.Json;
using System.Text.RegularExpressions;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Cleanup;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class QuarantineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static CleanupZoneDefinition Zone(string root) => new(CleanupZoneId.UserTemporaryFiles, "Temporaires", "Test", root,
        TimeSpan.FromDays(1), CleanupRiskLevel.Low, "Attention", "Effet", "Documents", 0);
    private static CleanupZonePreview Preview(CleanupZoneDefinition zone, string path) => new(zone, Now, 1, 1, 4, 0,
        [new(zone.Id, path, "old.tmp", 4, Now.AddDays(-2), true, null)], []);
    private static QuarantineEntry Entry(int days = 7) => new(Guid.NewGuid(), "zone/old.tmp", Zone("zone"), 4, new string('A', 64), Now, Now.AddDays(days));
    private static ConfirmedPlan Issue(ConfirmationService authority, ActionPreview preview) => authority.Issue(preview,
        new ConfirmationConsent(RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel), null));

    [Theory]
    [InlineData(-1, 7)] [InlineData(0, 7)] [InlineData(1, 1)] [InlineData(7, 7)]
    [InlineData(30, 30)] [InlineData(31, 7)] [InlineData(int.MaxValue, 7)]
    public void SettingsNormalizeRetentionAndReadExistingJson(int value, int expected)
    {
        var settings = JsonSerializer.Deserialize<VirgilSettings>($"{{\"QuarantineRetentionDays\":{value}}}");
        Assert.Equal(expected, settings!.QuarantineRetentionDays);
        Assert.Equal(7, JsonSerializer.Deserialize<VirgilSettings>("{\"SchemaVersion\":1}")!.QuarantineRetentionDays);
        Assert.Equal(7, new VirgilSettings().QuarantineRetentionDays);
    }

    [Fact]
    public async Task PreviewBindsRetentionAndActionFreezesIt()
    {
        var source = Preview(Zone("zone"), "zone/old.tmp");
        var changed = source with { QuarantineRetentionDays = 30 };
        var a = CleanupActionPreviewFactory.Create(source);
        var b = CleanupActionPreviewFactory.Create(changed);
        Assert.NotEqual(ActionPreviewHasher.Hash(a), ActionPreviewHasher.Hash(b));
        Assert.Equal(Reversibility.Full, a.Reversibility);
        Assert.Contains("7 jours", Assert.Single(a.Effects));
        Assert.Contains("effacement définitif proposé avec confirmation", Assert.Single(a.Effects));
        var previous = a with { Effects = ["Mise en quarantaine ; effacement définitif automatique après 7 jours. Restauration vérifiée possible pendant cette durée."] };
        Assert.NotEqual(ActionPreviewHasher.Hash(previous), ActionPreviewHasher.Hash(a));
        var action = new CleanupMaintenanceAction(new CleanupExecutionService(new Disk(), new ConfirmationService(new Clock())), changed);
        Assert.Equal(ActionPreviewHasher.Hash(b), ActionPreviewHasher.Hash(await action.PreviewAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task ModuleCapturesConfiguredRetentionAtScanAndDoesNotChangeExistingActions()
    {
        var days = 3;
        var source = Preview(Zone("zone"), "zone/old.tmp");
        var module = new CleanupMaintenanceModule(new CleanupAnalyzer(new Previews(source),
            new CleanupStorageAnalyzer(Array.Empty<string>())), new CleanupExecutionService(new Disk(),
                new ConfirmationService(new Clock())), () => days);
        var findings = await module.ScanAsync(CancellationToken.None);
        days = 30;
        var frozen = await Assert.Single(module.GetActions(findings)).PreviewAsync(CancellationToken.None);
        Assert.Equal("3", frozen.Parameters["QuarantineRetentionDays"]);
        var current = await Assert.Single(module.GetActions(await module.ScanAsync(CancellationToken.None)))
            .PreviewAsync(CancellationToken.None);
        Assert.Equal("30", current.Parameters["QuarantineRetentionDays"]);
        Assert.NotEqual(ActionPreviewHasher.Hash(frozen), ActionPreviewHasher.Hash(current));
    }

    [Theory]
    [InlineData(false, "absent")] [InlineData(true, "absent")]
    [InlineData(false, "expired")] [InlineData(true, "expired")]
    [InlineData(false, "used")] [InlineData(true, "used")]
    [InlineData(false, "hash")] [InlineData(true, "hash")]
    public async Task QuarantineActionsRefuseInvalidPlansWithoutAnyStoreCall(bool purge, string kind)
    {
        var clock = new Clock(); var authority = new ConfirmationService(clock); var store = new Store();
        var action = new QuarantineMaintenanceAction(store, authority, [Entry()], purge);
        var preview = await action.PreviewAsync(CancellationToken.None);
        var plan = kind == "absent" ? null : Issue(authority, kind == "hash" ? preview with { Effects = ["autre"] } : preview);
        if (kind == "used") Assert.True(authority.TryConsume(plan, preview).IsAccepted);
        if (kind == "expired") clock.Advance(TimeSpan.FromMinutes(10));
        var result = await action.ExecuteAsync(plan, CancellationToken.None);
        Assert.True(result.WasRefused); Assert.False(result.Succeeded); Assert.NotEmpty(result.RefusalReason!);
        Assert.Empty(store.Calls); Assert.Empty(result.Effects);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConfirmedSelectionOnlyAffectsSelectedEntriesAndCannotBeReused(bool purge)
    {
        var authority = new ConfirmationService(new Clock()); var store = new Store();
        var chosen = Entry(); var other = Entry(); store.Entries.AddRange([chosen, other]);
        IMaintenanceAction action = new QuarantineMaintenanceAction(store, authority, [chosen], purge);
        var preview = await action.PreviewAsync(CancellationToken.None);
        Assert.Equal(RiskLevel.Sensitive, preview.RiskLevel);
        Assert.Equal("1", preview.Parameters["Count"]); Assert.Equal("4", preview.Parameters["Bytes"]);
        if (purge) { Assert.Equal(Reversibility.None, preview.Reversibility); Assert.Contains("irréversible", Assert.Single(preview.Effects)); }
        var plan = Issue(authority, preview);
        var result = await action.ExecuteAsync(plan, CancellationToken.None);
        Assert.True(result.Succeeded); Assert.False(result.WasRefused);
        Assert.Equal(other, Assert.Single(store.Entries));
        Assert.Contains((purge ? "Purge:" : "Restore:") + chosen.Id, store.Calls);
        store.Calls.Clear();
        Assert.True((await action.ExecuteAsync(plan, CancellationToken.None)).WasRefused);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task ChangedManifestAndFailedPreflightRefuseEntireSelectionBeforeEffects()
    {
        var authority = new ConfirmationService(new Clock()); var store = new Store();
        var first = Entry(); var second = Entry(); store.Entries.AddRange([first, second]);
        var action = new QuarantineMaintenanceAction(store, authority, [first, second], false);
        var preview = await action.PreviewAsync(CancellationToken.None);
        store.Reason = "La taille ou le SHA-256 diffère.";
        Assert.True((await action.ExecuteAsync(Issue(authority, preview), CancellationToken.None)).WasRefused);
        Assert.Equal(2, store.Entries.Count); Assert.DoesNotContain(store.Calls, c => c.StartsWith("Restore:", StringComparison.Ordinal));
        store.Reason = null; store.Entries[0] = first with { SizeBytes = 5 };
        Assert.True((await action.ExecuteAsync(Issue(authority, preview), CancellationToken.None)).WasRefused);
        Assert.DoesNotContain(store.Calls, c => c.StartsWith("Restore:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExpirationOnlyPurgesDueEntriesWithTestClock()
    {
        var store = new Store(); var clock = new Clock();
        var expired = Entry() with { QuarantinedAtUtc = Now.AddDays(-8), ExpiresAtUtc = Now.AddSeconds(-1) };
        var due = Entry() with { QuarantinedAtUtc = Now.AddDays(-7), ExpiresAtUtc = Now };
        var future = Entry();
        store.Entries.AddRange([expired, due, future]);
        var authority = new ConfirmationService(clock);
        var service = new QuarantineService(store, authority, clock);
        var action = Assert.IsType<QuarantineMaintenanceAction>(service.GetExpiredPurgeAction());
        var preview = await action.PreviewAsync(CancellationToken.None);
        Assert.Equal("2", preview.Parameters["Count"]);
        Assert.Equal("8", preview.Parameters["Bytes"]);
        Assert.Contains(expired.Id.ToString(), preview.Parameters.Keys);
        Assert.Contains(due.Id.ToString(), preview.Parameters.Keys);
        Assert.DoesNotContain(future.Id.ToString(), preview.Parameters.Keys);
        Assert.Equal(RiskLevel.Sensitive, preview.RiskLevel);
        Assert.Equal(Reversibility.None, preview.Reversibility);
        Assert.Contains("irréversible", Assert.Single(preview.Effects));
        Assert.Equal(3, store.Entries.Count);
        Assert.DoesNotContain(store.Calls, call => call.StartsWith("Purge:", StringComparison.Ordinal));
        Assert.True((await action.ExecuteAsync(Issue(authority, preview), CancellationToken.None)).Succeeded);
        Assert.Equal(future, Assert.Single(store.Entries));
        Assert.Equal(2, store.Calls.Count(c => c.StartsWith("Purge:", StringComparison.Ordinal)));
        Assert.Null(service.GetExpiredPurgeAction());
        clock.Advance(TimeSpan.FromDays(7));
        var next = Assert.IsType<QuarantineMaintenanceAction>(service.GetExpiredPurgeAction());
        Assert.Single(store.Entries); // Le changement d'horloge ne vaut pas consentement.
        Assert.True((await next.ExecuteAsync(Issue(authority, await next.PreviewAsync(CancellationToken.None)), CancellationToken.None)).Succeeded);
        Assert.Empty(store.Entries);
        Assert.Null(service.GetExpiredPurgeAction());
    }

    [Theory]
    [InlineData("absent")] [InlineData("expired")] [InlineData("used")] [InlineData("hash")]
    public async Task ExpiredEntryPurgeRefusesInvalidPlansWithoutAnyEffect(string kind)
    {
        var clock = new Clock(); var authority = new ConfirmationService(clock); var store = new Store();
        var expired = Entry() with { QuarantinedAtUtc = Now.AddDays(-8), ExpiresAtUtc = Now.AddDays(-1) };
        var future = Entry(); store.Entries.AddRange([expired, future]);
        var service = new QuarantineService(store, authority, clock);
        var action = Assert.IsType<QuarantineMaintenanceAction>(service.GetExpiredPurgeAction());
        var preview = await action.PreviewAsync(CancellationToken.None);
        var plan = kind == "absent" ? null : Issue(authority, kind == "hash" ? preview with { Effects = ["Autre sélection"] } : preview);
        if (kind == "used") Assert.True(authority.TryConsume(plan, preview).IsAccepted);
        if (kind == "expired") clock.Advance(TimeSpan.FromMinutes(10));
        store.Calls.Clear();
        var result = await action.ExecuteAsync(plan, CancellationToken.None);
        Assert.True(result.WasRefused); Assert.False(result.Succeeded);
        Assert.Empty(result.Effects); Assert.Empty(store.Calls);
        Assert.Equal(new[] { expired, future }, store.Entries);
    }

    [Fact]
    public async Task ScanWithExpiredEntriesNeverPurges()
    {
        var store = new Store(); var expired = Entry() with { ExpiresAtUtc = Now.AddDays(-1) };
        store.Entries.Add(expired);
        var authority = new ConfirmationService(new Clock());
        var execution = new CleanupExecutionService(new Disk(), authority, quarantine: store);
        var module = new CleanupMaintenanceModule(new CleanupAnalyzer(new Previews(Preview(Zone("zone"), "zone/old.tmp")),
            new CleanupStorageAnalyzer(Array.Empty<string>())), execution);
        await module.ScanAsync(CancellationToken.None);
        Assert.Equal(expired, Assert.Single(store.Entries));
        Assert.Empty(store.Calls);
    }

    [Theory]
    [InlineData(CleanupClassification.Cleanable, false)]
    [InlineData(CleanupClassification.AdvancedCleanable, false)]
    [InlineData(CleanupClassification.Cleanable, true)]
    [Trait("Category", "Windows")]
    public async Task CleanupUsesQuarantinePortAndNeverDeleteEvenWhenMoveFails(CleanupClassification classification, bool fail)
    {
        using var sandbox = new Sandbox(); var disk = new Disk(); var store = new Store { FailMove = fail };
        var expired = Entry() with { QuarantinedAtUtc = Now.AddDays(-8), ExpiresAtUtc = Now.AddDays(-1) };
        store.Entries.Add(expired);
        var authority = new ConfirmationService(new Clock());
        var preview = Preview(sandbox.Zone with { Classification = classification }, sandbox.Path);
        var result = await new CleanupExecutionService(disk, authority, () => Now, quarantine: store)
            .ExecuteZoneAsync(preview, Issue(authority, CleanupActionPreviewFactory.Create(preview)), null, CancellationToken.None);
        Assert.Equal(0, disk.Deletes); Assert.Equal(0, result.DeletedBytes); Assert.Equal(0, result.DeletedFiles);
        Assert.Equal(fail ? 0 : 1, result.QuarantinedFiles); Assert.Equal(fail ? 1 : 0, result.ErrorFiles);
        Assert.Contains("Move", store.Calls);
        Assert.Contains(expired, store.Entries);
        Assert.DoesNotContain(store.Calls, call => call.StartsWith("Purge:", StringComparison.Ordinal));
        if (fail) { Assert.True(File.Exists(sandbox.Path)); Assert.Contains("quarantaine impossible", Assert.Single(result.Errors)); }
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task RealManifestIsCompleteAtomicAndRestoreChecksBeforeAndAfterMove()
    {
        using var sandbox = new Sandbox(); var logs = new List<string>(); var store = new QuarantineStore(sandbox.Quarantine, logs.Add);
        var entry = store.MoveToQuarantine(sandbox.Path, sandbox.Zone, Now, 7);
        Assert.False(File.Exists(sandbox.Path)); Assert.Equal(4, entry.SizeBytes); Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3, 4 })), entry.Sha256);
        Assert.Equal(Now, entry.QuarantinedAtUtc); Assert.Equal(Now.AddDays(7), entry.ExpiresAtUtc);
        Assert.Equal(sandbox.Path, entry.OriginalPath); Assert.Equal(sandbox.Zone.Id, entry.Zone.Id);
        Assert.Single(Directory.GetFiles(sandbox.Quarantine, "*.json")); Assert.Empty(Directory.GetFiles(sandbox.Quarantine, "*.tmp"));
        var persisted = Assert.Single(new QuarantineStore(sandbox.Quarantine).List());
        Assert.Equal(JsonSerializer.Serialize(entry), JsonSerializer.Serialize(persisted));
        var authority = new ConfirmationService(new Clock()); var action = new QuarantineMaintenanceAction(store, authority, [persisted], false);
        var result = await action.ExecuteAsync(Issue(authority, await action.PreviewAsync(CancellationToken.None)), CancellationToken.None);
        Assert.True(result.Succeeded); Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(sandbox.Path));
        Assert.Empty(store.List()); Assert.Empty(Directory.GetFiles(sandbox.Quarantine));
        Assert.Contains(logs, log => log.Contains("après déplacement", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("size")] [InlineData("hash")] [InlineData("destination")] [InlineData("guard")]
    [Trait("Category", "Windows")]
    public async Task RealRestoreRefusesChangedContentExistingDestinationOrUnsafeOriginWithoutEffects(string reason)
    {
        using var sandbox = new Sandbox(); var store = new QuarantineStore(sandbox.Quarantine);
        var entry = store.MoveToQuarantine(sandbox.Path, sandbox.Zone, Now, 7);
        var data = Directory.GetFiles(sandbox.Quarantine, "*.data").Single();
        if (reason == "size") File.WriteAllBytes(data, [1]);
        if (reason == "hash") File.WriteAllBytes(data, [4, 3, 2, 1]);
        if (reason == "destination") File.WriteAllText(sandbox.Path, "Ne pas écraser");
        if (reason == "guard")
        {
            entry = entry with { OriginalPath = System.IO.Path.Combine(sandbox.Root, "outside.tmp") };
            File.WriteAllText(Directory.GetFiles(sandbox.Quarantine, "*.json").Single(), JsonSerializer.Serialize(entry));
        }
        var before = File.ReadAllBytes(data);
        var authority = new ConfirmationService(new Clock()); var action = new QuarantineMaintenanceAction(store, authority, [entry], false);
        var result = await action.ExecuteAsync(Issue(authority, await action.PreviewAsync(CancellationToken.None)), CancellationToken.None);
        Assert.True(result.WasRefused); Assert.Equal(before, File.ReadAllBytes(data)); Assert.Single(store.List());
        if (reason == "destination") Assert.Equal("Ne pas écraser", File.ReadAllText(sandbox.Path));
        else Assert.False(File.Exists(entry.OriginalPath));
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void RealLockedMoveLeavesSourceIntactWithoutManifestOrFallbackDeletion()
    {
        using var sandbox = new Sandbox(); var store = new QuarantineStore(sandbox.Quarantine);
        using var locked = new FileStream(sandbox.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.ThrowsAny<IOException>(() => store.MoveToQuarantine(sandbox.Path, sandbox.Zone, Now, 7));
        Assert.True(File.Exists(sandbox.Path)); Assert.Empty(store.List());
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void CrossVolumeMoveIsRefusedBeforeManifestOrAnyChangeToSource()
    {
        using var sandbox = new Sandbox();
        var store = new QuarantineStore(sandbox.Quarantine, volumeIdentity: path => path == sandbox.Path ? "volume-A" : "volume-B");
        var exception = Assert.Throws<IOException>(() => store.MoveToQuarantine(sandbox.Path, sandbox.Zone, Now, 7));
        Assert.Contains("Autre volume", exception.Message);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(sandbox.Path));
        Assert.Empty(Directory.GetFiles(sandbox.Quarantine));
    }

    [Fact]
    public void QuarantineCoreContainsNoFileDirectoryOrNativeCalls()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        foreach (var name in new[] { "IQuarantineStore.cs", "QuarantineMaintenanceAction.cs" })
        {
            var source = File.ReadAllText(System.IO.Path.Combine(root, "src", "Virgil.Core", "Cleanup", name));
            Assert.DoesNotMatch(new Regex(@"\b(?:System\.IO\.)?(?:File|Directory|FileInfo|DirectoryInfo|DriveInfo)\s*(?:\.|\()"), source);
            Assert.DoesNotContain("DllImport", source);
        }
    }

    private sealed class Previews(CleanupZonePreview preview) : ICleanupPreviewService
    {
        public IReadOnlyList<CleanupZoneDefinition> GetZones() => [preview.Definition];
        public Task<IReadOnlyList<CleanupZonePreview>> PreviewAsync(IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CleanupZonePreview>>([preview]);
    }

    private sealed class Clock : TimeProvider
    {
        private TimeSpan _elapsed;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _elapsed.Ticks;
        public override DateTimeOffset GetUtcNow() => Now + _elapsed;
        public void Advance(TimeSpan value) => _elapsed += value;
    }
    private sealed class Store : IQuarantineStore
    {
        public List<string> Calls { get; } = [];
        public List<QuarantineEntry> Entries { get; } = [];
        public bool FailMove { get; init; }
        public string? Reason { get; set; }
        public IReadOnlyList<QuarantineEntry> List() { Calls.Add("List"); return Entries.ToArray(); }
        public QuarantineEntry MoveToQuarantine(string path, CleanupZoneDefinition zone, DateTimeOffset now, int retentionDays)
        {
            Calls.Add("Move"); if (FailMove) throw new IOException();
            var entry = Entry(retentionDays) with { OriginalPath = path, Zone = zone }; Entries.Add(entry); return entry;
        }
        public string? ValidateRestore(QuarantineEntry entry) { Calls.Add("Validate:" + entry.Id); return Reason; }
        public void Restore(QuarantineEntry entry) { Calls.Add("Restore:" + entry.Id); Entries.RemoveAll(e => e.Id == entry.Id); }
        public void Purge(QuarantineEntry entry) { Calls.Add("Purge:" + entry.Id); Entries.RemoveAll(e => e.Id == entry.Id); }
    }
    private sealed class Disk : ICleanupDisk
    {
        public int Deletes { get; private set; }
        public IRecycleBinService RecycleBin { get; } = new Recycle();
        public CleanupFileState ReadFile(string path) => new(4, Now.AddDays(-2));
        public void DeleteFile(string path) { Deletes++; throw new InvalidOperationException("Suppression interdite"); }
        public IReadOnlyList<string> GetDirectories(string path) => [];
        public bool HasEntries(string path) => true;
        public void DeleteEmptyDirectory(string path) => throw new InvalidOperationException("Dossier à conserver");
    }
    private sealed class Recycle : IRecycleBinService
    {
        public RecycleBinState Query() => new(true, 0, 0);
        public RecycleBinActionResult Empty() => throw new InvalidOperationException("Corbeille inattendue");
    }
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VirgilQuarantine-" + Guid.NewGuid());
        public string Path => System.IO.Path.Combine(Root, "zone", "old.tmp");
        public string Quarantine => System.IO.Path.Combine(Root, "quarantine");
        public CleanupZoneDefinition Zone => QuarantineTests.Zone(System.IO.Path.Combine(Root, "zone"));
        public Sandbox() { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!); File.WriteAllBytes(Path, [1, 2, 3, 4]); File.SetLastWriteTimeUtc(Path, Now.AddDays(-2).UtcDateTime); }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
