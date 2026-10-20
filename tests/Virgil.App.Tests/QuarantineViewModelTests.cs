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
        EmptyCleanupSource.Register(services);
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

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OpeningWithExpiredEntriesOnlyOffersPurgeAndRequiresOverlayConsent(bool cancel)
    {
        var store = new Store(expired: true);
        store.AddFuture();
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        services.AddSingleton<IQuarantineStore>(store);
        services.AddTransient<ICleanupPreviewService>(_ => new EmptyPreviews());
        services.AddTransient<CleanupStorageAnalyzer>(_ => new CleanupStorageAnalyzer(Array.Empty<string>()));
        EmptyCleanupSource.Register(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var vm = provider.GetRequiredService<CleanupViewModel>();
        var notifications = 0;
        vm.VirgilMessageRequested += _ => notifications++;
        vm.InitializeQuarantineCommand.Execute(null);
        Assert.Equal(0, store.Effects); Assert.Equal(0, notifications);
        Assert.False(vm.IsValidationOpen); Assert.True(vm.HasExpiredQuarantine);
        Assert.Contains("(1,", vm.ExpiredQuarantineLabel);
        Assert.Equal("Expiré", vm.QuarantineItems.Single(item => item.IsExpired).ExpirationStatus);
        Assert.Equal(2, vm.QuarantineItems.Count);
        await provider.GetRequiredService<CleanupMaintenanceModule>().ScanAsync(CancellationToken.None);
        Assert.Equal(0, store.Effects); Assert.Equal(2, store.List().Count);
        // La sélection manuelle d'un fichier non expiré ne doit pas élargir cette action.
        vm.QuarantineItems.Single(item => !item.IsExpired).IsSelected = true;
        var pending = vm.PurgeExpiredQuarantineCommand.ExecuteAsync(null);
        Assert.True(vm.IsValidationOpen);
        Assert.Equal("Effacer les éléments expirés", vm.ValidationTitle);
        Assert.Contains("1 fichiers", vm.ValidationMetrics);
        Assert.Contains("irréversible", vm.ValidationEffect);
        Assert.DoesNotContain("future.tmp", vm.ValidationRoot);
        Assert.Equal(0, store.Effects);
        if (cancel) vm.CancelDecisionCommand.Execute(null); else vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(cancel ? 0 : 1, store.Effects);
        Assert.Equal(cancel ? 2 : 1, store.List().Count);
        Assert.Contains(store.List(), entry => entry.OriginalPath.EndsWith("future.tmp", StringComparison.Ordinal));
        Assert.Equal(cancel, vm.HasExpiredQuarantine);
    }

    private sealed class EmptyPreviews : ICleanupPreviewService
    {
        public IReadOnlyList<CleanupZoneDefinition> GetZones() => [];
        public Task<IReadOnlyList<CleanupZonePreview>> PreviewAsync(IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CleanupZonePreview>>([]);
    }

    private sealed class Store(bool expired = false) : IQuarantineStore
    {
        private readonly List<QuarantineEntry> _entries = [new(Guid.NewGuid(), "zone/old.tmp",
            new CleanupZoneDefinition(CleanupZoneId.UserTemporaryFiles, "Temp", "Temp", "zone", TimeSpan.Zero,
                CleanupRiskLevel.Low, "Attention", "Effet", "Documents", 0), 4, new string('A', 64),
            DateTimeOffset.UtcNow.AddDays(expired ? -8 : 0), DateTimeOffset.UtcNow.AddDays(expired ? -1 : 7))];
        public void AddFuture() => _entries.Add(_entries[0] with
        {
            Id = Guid.NewGuid(), OriginalPath = "zone/future.tmp", QuarantinedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
        });
        public int Effects { get; private set; }
        public IReadOnlyList<QuarantineEntry> List() => _entries.ToArray();
        public QuarantineEntry MoveToQuarantine(string path, CleanupZoneDefinition zone, DateTimeOffset now, int retentionDays) => throw new NotSupportedException();
        public string? ValidateRestore(QuarantineEntry entry) => null;
        public void Restore(QuarantineEntry entry) { Effects++; _entries.RemoveAll(e => e.Id == entry.Id); }
        public void Purge(QuarantineEntry entry) { Effects++; _entries.RemoveAll(e => e.Id == entry.Id); }
    }
}
