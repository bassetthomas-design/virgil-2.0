using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Actions;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class QuarantineViewModelTests
{
    [Fact]
    public void QuarantinePortAndViewModelResolveWithValidatedHosting()
    {
        using var provider = new ServiceCollection().AddVirgilServices().AddVirgilViews()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<Virgil.Infrastructure.Windows.QuarantineStore>(provider.GetRequiredService<IQuarantineStore>());
        Assert.Same(provider.GetRequiredService<IQuarantineStore>(), provider.GetRequiredService<IQuarantineStore>());
        Assert.NotNull(provider.GetRequiredService<QuarantineService>());
        Assert.NotNull(provider.GetRequiredService<CleanupViewModel>().RestoreQuarantineCommand);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task RestoreAndPurgeUseExistingOverlayAndOnlyExecuteAfterConsent(bool purge, bool cancel)
    {
        var store = new Store();
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        services.AddSingleton<IQuarantineStore>(store);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var vm = provider.GetRequiredService<CleanupViewModel>();
        vm.InitializeQuarantineCommand.Execute(null);
        var item = Assert.Single(vm.QuarantineItems);
        Assert.Equal("old.tmp", item.Name);
        item.IsSelected = true;
        var pending = purge ? vm.PurgeQuarantineCommand.ExecuteAsync(null) : vm.RestoreQuarantineCommand.ExecuteAsync(null);
        Assert.True(vm.IsValidationOpen);
        Assert.Equal(purge ? "Vider la quarantaine maintenant" : "Restaurer", vm.ValidationTitle);
        Assert.Contains("1 fichiers", vm.ValidationMetrics);
        if (purge) Assert.Contains("irréversible", vm.ValidationEffect);
        Assert.Equal(0, store.Effects);
        if (cancel) vm.CancelDecisionCommand.Execute(null); else vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.False(vm.IsValidationOpen); Assert.False(vm.IsBusy);
        Assert.Equal(cancel ? 0 : 1, store.Effects);
        if (cancel) Assert.Single(store.List()); else Assert.Empty(store.List());
    }

    private sealed class Store : IQuarantineStore
    {
        private readonly List<QuarantineEntry> _entries = [new(Guid.NewGuid(), "zone/old.tmp",
            new CleanupZoneDefinition(CleanupZoneId.UserTemporaryFiles, "Temp", "Temp", "zone", TimeSpan.Zero,
                CleanupRiskLevel.Low, "Attention", "Effet", "Documents", 0), 4, new string('A', 64),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7))];
        public int Effects { get; private set; }
        public IReadOnlyList<QuarantineEntry> List() => _entries.ToArray();
        public QuarantineEntry MoveToQuarantine(string path, CleanupZoneDefinition zone, DateTimeOffset now, int retentionDays) => throw new NotSupportedException();
        public string? ValidateRestore(QuarantineEntry entry) => null;
        public void Restore(QuarantineEntry entry) { Effects++; _entries.Remove(entry); }
        public void Purge(QuarantineEntry entry) { Effects++; _entries.Remove(entry); }
    }
}
