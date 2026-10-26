using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Startup;
using Xunit;

namespace Virgil.Tests;

public sealed class StartupChangeTests
{
    private static readonly StartupApprovalTarget Target = new(StartupApprovalHive.CurrentUser, StartupApprovalKind.Run, "Exemple");
    private const string Enabled = "020000000000000000000000";
    private const string Disabled = "030000000000000000000000";

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("expired")]
    public async Task Invalid_plan_causes_no_backup_restore_point_or_write(string scenario)
    {
        var clock = new Clock();
        var authority = new ConfirmationService(clock);
        var store = new Store();
        var restore = new RestorePoints();
        var service = new StartupChangeService(store, restore, authority);
        var change = Assert.IsType<StartupChange>(await service.PreviewAsync(Target, false));
        var plan = scenario == "missing" ? null : Issue(authority, change);
        if (scenario == "changed") change = change with { NewValue = Enabled };
        if (scenario == "expired") clock.Advance(TimeSpan.FromMinutes(11));
        Assert.False((await service.ExecuteAsync(change, plan)).Succeeded);
        Assert.Equal(0, store.Backups);
        Assert.Equal(0, restore.Calls);
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Failed_safeguard_blocks_registry_mutation(bool verifiedBackup, bool verifiedRestorePoint)
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var store = new Store { BackupVerified = verifiedBackup };
        var restore = new RestorePoints { Verified = verifiedRestorePoint };
        var service = new StartupChangeService(store, restore, authority);
        var change = Assert.IsType<StartupChange>(await service.PreviewAsync(Target, false));
        var plan = Issue(authority, change);
        Assert.False((await service.ExecuteAsync(change, plan)).Succeeded);
        Assert.Equal(0, store.Writes);
        Assert.Equal(verifiedBackup ? 1 : 0, restore.Calls);
        Assert.False((await service.ExecuteAsync(change, plan)).Succeeded);
        Assert.Equal(1, store.Backups); // The failed operation also consumes consent.
    }

    [Fact]
    public async Task Change_and_restore_preserve_exact_previous_value_and_require_separate_plans()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var store = new Store();
        var restore = new RestorePoints();
        var service = new StartupChangeService(store, restore, authority);
        var change = Assert.IsType<StartupChange>(await service.PreviewAsync(Target, false));
        var plan = Issue(authority, change);
        var result = await service.ExecuteAsync(change, plan);
        Assert.True(result.Succeeded);
        Assert.Equal(Disabled, store.Current);
        Assert.False((await service.ExecuteAsync(change, plan)).Succeeded);
        var restoreChange = Assert.IsType<StartupChange>(await service.PreviewRestoreAsync(result.BackupId!));
        Assert.False((await service.ExecuteAsync(restoreChange, null)).Succeeded);
        Assert.True((await service.ExecuteAsync(restoreChange, Issue(authority, restoreChange))).Succeeded);
        Assert.Equal(Enabled, store.Current);
        Assert.Equal(2, store.Writes);
        Assert.Equal(2, restore.Calls);
    }

    [Fact]
    public async Task Stale_state_or_missing_restore_backup_causes_no_effect()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var store = new Store();
        var restore = new RestorePoints();
        var service = new StartupChangeService(store, restore, authority);
        var change = Assert.IsType<StartupChange>(await service.PreviewAsync(Target, false));
        var plan = Issue(authority, change);
        store.Current = Disabled;
        Assert.False((await service.ExecuteAsync(change, plan)).Succeeded);
        change = change with { PreviousValue = Disabled, NewValue = Enabled, RestoreBackupId = "absent" };
        Assert.False((await service.ExecuteAsync(change, Issue(authority, change))).Succeeded);
        Assert.Equal(0, store.Backups);
        Assert.Equal(0, restore.Calls);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Unsupported_hive_and_unknown_metadata_have_no_executable_preview()
    {
        var store = new Store();
        var service = new StartupChangeService(store, new RestorePoints(), new ConfirmationService(TimeProvider.System));
        Assert.Null(await service.PreviewAsync(Target with { Hive = (StartupApprovalHive)99 }, false));
        Assert.Null(await service.PreviewAsync(Target with { ValueName = "../autre" }, false));
        store.Current = "000000000000000000000000";
        Assert.Null(await service.PreviewAsync(Target, false));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Machine_change_is_bound_to_admin_preview_and_uses_protected_elevated_writer()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var store = new Store();
        var restore = new RestorePoints();
        var service = new StartupChangeService(store, restore, authority);
        var change = Assert.IsType<StartupChange>(await service.PreviewAsync(Target with { Hive = StartupApprovalHive.LocalMachine }, false));
        Assert.True(StartupChangeService.CreatePreview(change).RequiresAdmin);
        Assert.False((await service.ExecuteAsync(change, null)).Succeeded);
        Assert.Equal(0, store.Writes);
        Assert.True((await service.ExecuteAsync(change, Issue(authority, change))).Succeeded);
        Assert.Equal(1, store.Backups);
        Assert.Equal(1, store.Writes);
        Assert.Equal(0, restore.Calls); // The machine writer creates and verifies its own restore point.
    }

    private static ConfirmedPlan Issue(ConfirmationService authority, StartupChange change) =>
        authority.Issue(StartupChangeService.CreatePreview(change), new(ConfirmationLevel.Reinforced, null));

    private sealed class Store : IStartupApprovalStore
    {
        public string Current { get; set; } = Enabled;
        public bool BackupVerified { get; set; } = true;
        public int Backups { get; private set; }
        public int Writes { get; private set; }
        private readonly Dictionary<string, StartupBackup> _backups = new();
        public Task<string?> ReadAsync(StartupApprovalTarget target, CancellationToken token) => Task.FromResult<string?>(Current);
        public Task<string?> SaveVerifiedBackupAsync(StartupBackup backup, CancellationToken token)
        {
            Backups++;
            if (!BackupVerified) return Task.FromResult<string?>(null);
            var id = Backups.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _backups[id] = backup;
            return Task.FromResult<string?>(id);
        }
        public Task<StartupBackup?> ReadBackupAsync(string id, CancellationToken token) => Task.FromResult(_backups.GetValueOrDefault(id));
        public Task<bool> WriteIfUnchangedAsync(StartupApprovalTarget target, string expected, string value, CancellationToken token)
        {
            if (Current != expected) return Task.FromResult(false);
            Writes++;
            Current = value;
            return Task.FromResult(true);
        }
    }
    private sealed class RestorePoints : IStartupRestorePointService
    {
        public bool Verified { get; set; } = true;
        public int Calls { get; private set; }
        public Task<bool> CreateAndVerifyAsync(CancellationToken token) { Calls++; return Task.FromResult(Verified); }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan elapsed) { _now += elapsed; _timestamp += elapsed.Ticks; }
    }
}
