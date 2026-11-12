using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;
using static Virgil.Tests.D2SingleItemTests;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class D2SourceCardsTests
{
    [Fact]
    public Task Existing_driver_scan_exposes_individual_cards_without_startup_scan() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new Fixture();
        Assert.Equal(0, f.Backend.Scans);
        var preview = f.Vm.RunAllCommand.ExecuteAsync(null);
        f.Vm.SkipDecisionCommand.Execute(null);
        await preview;
        Assert.Equal(1, f.Backend.Scans);
        var first = Card(f.Vm, "Driver:WU:" + Backend.First);
        Assert.Equal("Pilote A", Read<string>(first, "Title"));
        Assert.Equal("Mettre à jour", Read<string>(first, "ButtonText"));
        Assert.True(Read<bool>(first, "CanUpdate"));
        var bios = Card(f.Vm, "Driver:BIOS");
        Assert.False(Read<bool>(bios, "CanUpdate"));
        Assert.Equal("non pris en charge automatiquement", Read<string>(bios, "Status"));
        Assert.Equal("Virgil n'installe jamais le BIOS ; passez par l'outil officiel du fabricant de l'ordinateur si une mise à jour est proposée.", Read<string>(bios, "Detail"));
        await Capture(f.Vm, "05-bios-sans-bouton");
        var unsupported = Card(f.Vm, "Driver:WinGet:Intel.IntelDriverAndSupportAssistant");
        Assert.False(Read<bool>(unsupported, "CanUpdate"));
        Assert.Equal("non pris en charge automatiquement", Read<string>(unsupported, "Status"));
        Assert.Equal("l'outil du fabricant n'installe pas les pilotes tout seul", Read<string>(unsupported, "Detail"));
        await Capture(f.Vm, "06-non-pris-en-charge");
        var pending = Command(f.Vm).ExecuteAsync(Read<UpdateSequenceStep>(first, "Step"));
        try
        {
            Assert.Equal("Mettre à jour Pilote A", f.Vm.ValidationTitle);
            Assert.Equal("Pilote A\nversion non disponible\nMême méthode que « Tout mettre à jour ».", f.Vm.ValidationDetails);
            Assert.Equal("Un point de restauration de la session doit être vérifié. Un point de restauration récent (moins de 24 h) pourra être réutilisé si Windows refuse d'en créer un. Aucun redémarrage automatique.", f.Vm.ValidationSafety);
            await Capture(f.Vm, "03-confirmation-pilote");
        }
        finally { f.Vm.ExecuteDecisionCommand.Execute(null); await pending; }
        Assert.Equal(new[] { "WU:" + Backend.First }, f.Backend.Installed);
        Assert.Equal(1, f.Backend.Scans);
        Assert.Equal("mis à jour", Read<string>(first, "Status"));
        Assert.Equal("reste à installer", Read<string>(Card(f.Vm, "Driver:WU:" + Backend.Second), "Status"));
        Assert.Contains("Pilotes : reste à installer", Read<string>(f.Vm, "SingleSourceSummary"));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Official_package_is_the_only_installable_offer_and_restore_failure_blocks_it(bool blocked) => ThemeContrastTests.OnSta(async () =>
    {
        var f = new Fixture(); f.Backend.BlockRestore = blocked;
        var preview = f.Vm.RunAllCommand.ExecuteAsync(null); f.Vm.SkipDecisionCommand.Execute(null); await preview;
        var card = Card(f.Vm, "Driver:WinGet:AMD.AMDSoftwareCloudEdition");
        Assert.True(Read<bool>(card, "CanUpdate"));
        Assert.Equal("AMD Software: Cloud Edition", Read<string>(card, "Title"));
        var pending = Command(f.Vm).ExecuteAsync(Read<UpdateSequenceStep>(card, "Step"));
        f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal(blocked ? Array.Empty<string>() : new[] { "WinGet:AMD.AMDSoftwareCloudEdition" }, f.Backend.Installed);
        Assert.Equal(blocked ? "bloquée" : "mis à jour", Read<string>(card, "Status"));
    });

    [Fact]
    public Task Bios_information_does_not_replace_current_driver_source_status() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new Fixture(); f.Backend.Empty = true;
        var preview = f.Vm.RunAllCommand.ExecuteAsync(null); f.Vm.SkipDecisionCommand.Execute(null); await preview;
        Assert.Contains("Pilotes : à jour", Read<string>(f.Vm, "SingleSourceSummary"));
        Assert.DoesNotContain("0 ", Read<string>(f.Vm, "SingleSourceSummary"));
        Assert.False(Read<bool>(Card(f.Vm, "Driver:BIOS"), "CanUpdate"));
    });

    private sealed class Fixture
    {
        public Backend Backend { get; } = new();
        public UpdatesViewModel Vm { get; }
        public Fixture()
        {
            var confirmation = new RecordingConfirmation();
            var runner = new UpdatesSequenceViewModelTests.Runner();
            Vm = new(new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(runner), runner, confirmation.Authority),
                new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), runner, new UpdatesSequenceViewModelTests.History(), confirmation,
                new UpdateSequenceService(Backend, confirmation.Authority, new EmptyPendingRebootProbe()));
        }
    }
    private sealed class Scan : IUpdateScanService
    {
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new() { IsAvailable = true } });
    }
    private sealed class Backend : IUpdateSequenceBackend, IDriverSequenceBackend, IDriverInventoryReader, IBiosInformationReader, IDriverPackageSource
    {
        public const string First = "f813d575-a123-4788-b34a-d547c15c5663:7";
        public const string Second = "a313d575-a123-4788-b34a-d547c15c5663:1";
        public int Scans { get; private set; }
        public bool Empty { get; set; }
        public bool BlockRestore { get; set; }
        public IDriverInventoryReader DriverInventory => this;
        public IBiosInformationReader Bios => this;
        public IDriverPackageSource DriverPackages => this;
        public List<string> Installed { get; } = [];
        Task<DriverInventoryReport> IDriverInventoryReader.ReadAsync(CancellationToken token) => Task.FromResult(new DriverInventoryReport { WasAnalyzed = true });
        Task<BiosInformation> IBiosInformationReader.ReadAsync(CancellationToken token) => Task.FromResult(new BiosInformation("OEM", "1.0"));
        public Task<IReadOnlyList<DriverPackageOffer>> ScanAsync(CancellationToken token)
        {
            Scans++;
            return Task.FromResult<IReadOnlyList<DriverPackageOffer>>(Empty ? [] : [
                new(new() { Id = "AMD.AMDSoftwareCloudEdition", Name = "AMD Software: Cloud Edition", AvailableVersion = "2.0" }, true, ""),
                new(new() { Id = "Intel.IntelDriverAndSupportAssistant", Name = "Intel Driver & Support Assistant", AvailableVersion = "2.0" }, false, "l'outil du fabricant n'installe pas les pilotes tout seul")]);
        }
        public Task<UpdateSequenceStepResult> InstallAsync(UpdateSequenceStep step, CancellationToken token)
        { Installed.Add(step.Id); return Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Paquet officiel installé.") { VerifiedStatus = "mis à jour" }); }
        public Task<UpdateSourceVerification> VerifyAsync(UpdateComponent component, CancellationToken token) => Task.FromResult(
            new UpdateSourceVerification(component, Empty ? "à jour" : "reste à installer", "Contrôle en lecture seule.") { DriverItems = Empty ? [] : new[] {
                new DriverUpdateOutcome(First, "Pilote A", "reste à installer", "Ce pilote reste proposé.", false),
                new DriverUpdateOutcome(Second, "Pilote B", "reste à installer", "Ce pilote reste proposé.", false) }.Where(d => !Installed.Contains("WU:" + d.Identity)).ToArray() });
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) => throw new InvalidOperationException("Aucun autre élément autorisé.");
        public Task<IReadOnlyList<UpdateSequenceStepResult>> ExecuteGroupAsync(IReadOnlyList<UpdateSequenceStep> steps, CancellationToken token)
        {
            if (steps.Count == 0) return Task.FromResult<IReadOnlyList<UpdateSequenceStepResult>>([
                new(new("restore", "Point de restauration", UpdateComponent.Restore), !BlockRestore, false, BlockRestore ? "Installation bloquée : point de restauration non vérifié." : "Point vérifié.") { RestorePointVerified = !BlockRestore }]);
            var step = Assert.Single(steps); Assert.Equal("WU:" + First, step.Id);
            Installed.Add(step.Id);
            return Task.FromResult<IReadOnlyList<UpdateSequenceStepResult>>([new(step, true, false, "Pilote installé.") { VerifiedStatus = "mis à jour" }]);
        }
    }
}
