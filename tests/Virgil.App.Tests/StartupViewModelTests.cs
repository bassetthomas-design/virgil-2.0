using Virgil.App.ViewModels;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Startup;
using Xunit;

namespace Virgil.Tests;

public sealed class StartupViewModelTests
{
    private const string Enabled = "020000000000000000000000";
    private const string Disabled = "030000000000000000000000";
    private static readonly StartupApprovalTarget Target = new(StartupApprovalHive.CurrentUser, StartupApprovalKind.Run, "Exemple");
    private static readonly StartupItem Item = new("Exemple", "Run", "exemple.exe", "Activé", "Éditeur", "Non vérifiée", "Non mesuré", Target);

    [Fact]
    public async Task Preparing_change_reads_preview_without_issuing_consent_or_effects()
    {
        var context = new Context();
        await context.Model.DisableCommand.ExecuteAsync(null);
        Assert.True(context.Model.IsConfirmationOpen);
        Assert.False(context.Model.ConfirmationAccepted);
        Assert.Contains("sauvegarde vérifiée", context.Model.ConfirmationText);
        Assert.Empty(context.Confirmation.Plans);
        context.AssertNoEffects();
    }

    [Fact]
    public async Task Confirming_without_checking_acceptance_has_no_effect()
    {
        var context = new Context();
        await context.Model.DisableCommand.ExecuteAsync(null);
        await context.Model.ConfirmCommand.ExecuteAsync(null);
        Assert.True(context.Model.IsConfirmationOpen);
        Assert.Empty(context.Confirmation.Plans);
        context.AssertNoEffects();
    }

    [Fact]
    public async Task Cancel_discards_pending_change_even_when_acceptance_was_checked()
    {
        var context = new Context();
        await context.Model.DisableCommand.ExecuteAsync(null);
        context.Model.ConfirmationAccepted = true;
        context.Model.CancelConfirmationCommand.Execute(null);
        await context.Model.ConfirmCommand.ExecuteAsync(null);
        Assert.False(context.Model.IsConfirmationOpen);
        Assert.False(context.Model.ConfirmationAccepted);
        Assert.Empty(context.Confirmation.Plans);
        context.AssertNoEffects();
    }

    [Fact]
    public async Task Explicit_confirmation_executes_once_and_cannot_be_replayed_by_clicking_confirm()
    {
        var context = new Context();
        await context.Model.DisableCommand.ExecuteAsync(null);
        context.Model.ConfirmationAccepted = true;
        await context.Model.ConfirmCommand.ExecuteAsync(null);
        await context.Model.ConfirmCommand.ExecuteAsync(null);
        Assert.Single(context.Confirmation.Plans);
        Assert.Equal(1, context.Store.Writes);
        Assert.Equal(1, context.Store.Backups);
        Assert.Equal(1, context.RestorePoints.Calls);
        Assert.Equal(Disabled, context.Store.Current);
        Assert.False(context.Model.IsConfirmationOpen);
        Assert.False(context.Model.IsChanging);
        Assert.False(context.Model.ConfirmationAccepted);
    }

    [Fact]
    public async Task Restore_is_only_a_preview_until_separately_confirmed_with_a_distinct_plan()
    {
        var context = new Context();
        await context.Model.DisableCommand.ExecuteAsync(null);
        context.Model.ConfirmationAccepted = true;
        await context.Model.ConfirmCommand.ExecuteAsync(null);
        await context.Model.RestoreCommand.ExecuteAsync(null);
        Assert.True(context.Model.IsConfirmationOpen);
        Assert.False(context.Model.ConfirmationAccepted);
        Assert.Single(context.Confirmation.Plans);
        Assert.Equal(1, context.Store.Writes);
        context.Model.ConfirmationAccepted = true;
        await context.Model.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(2, context.Confirmation.Plans.Count);
        Assert.NotSame(context.Confirmation.Plans[0], context.Confirmation.Plans[1]);
        Assert.NotEqual(context.Confirmation.Plans[0].ActionId, context.Confirmation.Plans[1].ActionId);
        Assert.Equal(Enabled, context.Store.Current);
        Assert.Equal(2, context.Store.Writes);
        Assert.Equal(2, context.RestorePoints.Calls);
    }

    [Fact]
    public async Task Confirmation_authority_failure_is_reported_without_effects()
    {
        var context = new Context();
        context.Confirmation.Refuse = true;
        await context.Model.DisableCommand.ExecuteAsync(null);
        context.Model.ConfirmationAccepted = true;
        await context.Model.ConfirmCommand.ExecuteAsync(null);
        Assert.Contains("n’a pas abouti", context.Model.Status);
        Assert.False(context.Model.IsChanging);
        Assert.False(context.Model.ConfirmationAccepted);
        context.AssertNoEffects();
    }

    [Fact]
    public async Task Read_failure_is_reported_and_read_only_entries_have_no_confirmation()
    {
        var context = new Context();
        context.Inventory.Fail = true;
        await context.Model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Impossible de lire les éléments au démarrage.", context.Model.Status);
        context.Model.SelectedItem = Item with { ApprovalTarget = null };
        await context.Model.EnableCommand.ExecuteAsync(null);
        Assert.Contains("Lecture seule", context.Model.Status);
        Assert.False(context.Model.IsConfirmationOpen);
        Assert.Empty(context.Confirmation.Plans);
        context.AssertNoEffects();
    }

    [Fact, Trait("Category", "Safety")]
    public void Startup_buttons_are_disabled_without_approval_target()
    {
        var context = new Context();
        Assert.True(context.Model.EnableCommand.CanExecute(null));
        Assert.True(context.Model.DisableCommand.CanExecute(null));
        Assert.False(context.Model.RestoreCommand.CanExecute(null));
        context.Model.SelectedItem = Item with { ApprovalTarget = null };
        Assert.False(context.Model.EnableCommand.CanExecute(null));
        Assert.False(context.Model.DisableCommand.CanExecute(null));
        Assert.Contains("Lecture seule", context.Model.SelectionDetails);
        Assert.Equal(0, context.Store.Reads);
        Assert.False(context.Model.IsConfirmationOpen);
        context.AssertNoEffects();
    }

    private sealed class Context
    {
        public Store Store { get; } = new();
        public RestorePoints RestorePoints { get; } = new();
        public Confirmation Confirmation { get; } = new();
        public Inventory Inventory { get; } = new();
        public StartupViewModel Model { get; }
        public Context() => Model = new(Inventory, new(Store, RestorePoints, Confirmation.Authority), Confirmation) { SelectedItem = Item };
        public void AssertNoEffects()
        {
            Assert.Equal(0, Store.Writes);
            Assert.Equal(0, Store.Backups);
            Assert.Equal(0, RestorePoints.Calls);
        }
    }

    private sealed class Inventory : IStartupInventorySource
    {
        public bool Fail { get; set; }
        public Task<StartupInventory> ReadAsync(CancellationToken token) => Fail
            ? Task.FromException<StartupInventory>(new InvalidOperationException())
            : Task.FromResult(new StartupInventory(new[] { Item }, Array.Empty<string>()));
    }

    private sealed class Confirmation : IConfirmationService
    {
        public ConfirmationService Authority { get; } = new(TimeProvider.System);
        public List<ConfirmedPlan> Plans { get; } = new();
        public bool Refuse { get; set; }
        public ConfirmedPlan Issue(ActionPreview preview, ConfirmationConsent consent)
        {
            if (Refuse) throw new ConfirmationRefusedException();
            var plan = Authority.Issue(preview, consent);
            Plans.Add(plan);
            return plan;
        }
    }

    private sealed class Store : IStartupApprovalStore
    {
        public string Current { get; set; } = Enabled;
        public int Backups { get; private set; }
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        private readonly Dictionary<string, StartupBackup> _backups = new();
        public Task<string?> ReadAsync(StartupApprovalTarget target, CancellationToken token) { Reads++; return Task.FromResult<string?>(Current); }
        public Task<string?> SaveVerifiedBackupAsync(StartupBackup backup, CancellationToken token)
        {
            var id = (++Backups).ToString(System.Globalization.CultureInfo.InvariantCulture);
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
        public int Calls { get; private set; }
        public Task<bool> CreateAndVerifyAsync(CancellationToken token) { Calls++; return Task.FromResult(true); }
    }
}
