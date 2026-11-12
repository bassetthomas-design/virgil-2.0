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
        Assert.Equal("Virgil n'installe jamais le BIOS ni le firmware ; passez par l'outil officiel du fabricant de l'ordinateur si une mise à jour est proposée.", Read<string>(bios, "Detail"));
        f.Vm.StatusText = "BIOS : information seulement, aucune installation.";
        await Capture(f.Vm, "05-bios-sans-bouton");
        var unsupported = Card(f.Vm, "Driver:WinGet:Intel.IntelDriverAndSupportAssistant");
        Assert.False(Read<bool>(unsupported, "CanUpdate"));
        Assert.Equal("non pris en charge automatiquement", Read<string>(unsupported, "Status"));
        Assert.Equal("l'outil du fabricant n'installe pas les pilotes tout seul", Read<string>(unsupported, "Detail"));
        f.Vm.StatusText = "Intel Driver & Support Assistant : non pris en charge automatiquement.";
        await Capture(f.Vm, "06-non-pris-en-charge");
        var pngDirectory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-d2");
        Assert.NotEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(pngDirectory, "05-bios-sans-bouton.png")))),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(pngDirectory, "06-non-pris-en-charge.png")))));
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

    [Fact]
    public Task Partial_applications_and_BIOS_share_one_screen_with_independent_source_lines() => ThemeContrastTests.OnSta(async () =>
    {
        var scan = new MixedScan(); var f = new Fixture(scan); f.Backend.Empty = true;
        var calls = new List<string>();
        f.Backend.ApplicationHandler = step =>
        {
            Assert.Equal(UpdateComponent.Winget, step.Component);
            Assert.Contains(step.Id, new[] { "a", "b" }); calls.Add(step.Id);
            if (step.Id == "a") scan.Remaining.Remove("a");
            return new(step, step.Id == "a", false, step.Id == "a" ? "Installation réussie." : "Installation impossible.");
        };
        await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
        var preview = f.Vm.RunAllCommand.ExecuteAsync(null); f.Vm.SkipDecisionCommand.Execute(null); await preview;
        foreach (var id in new[] { "a", "b" })
        {
            var pending = Command(f.Vm).ExecuteAsync(Read<UpdateSequenceStep>(Card(f.Vm, "Winget:" + id), "Step"));
            f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        }
        Assert.Equal(new[] { "a", "b" }, calls);
        Assert.Empty(f.Backend.Installed);
        Assert.Equal("mis à jour", Read<string>(Card(f.Vm, "Winget:a"), "Status"));
        Assert.Equal("en erreur", Read<string>(Card(f.Vm, "Winget:b"), "Status"));
        Assert.Equal("reste à installer", Read<string>(Card(f.Vm, "Winget:c"), "Status"));
        Assert.False(Read<bool>(Card(f.Vm, "Driver:BIOS"), "CanUpdate"));
        Assert.Contains("Applications : en erreur", Read<string>(f.Vm, "SingleSourceSummary"));
        Assert.Contains("Pilotes : à jour", Read<string>(f.Vm, "SingleSourceSummary"));
        Assert.DoesNotContain("Tout est à jour.", f.Vm.Operation.FinalSummary);
        await Capture(f.Vm, "09-bilan-partiel-avec-bios");
    });
    private sealed class MixedScan : IUpdateScanService
    {
        public HashSet<string> Remaining { get; } = ["a", "b", "c"];
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new() { IsAvailable = true }, Items = Remaining.Select(id => new UpdateItem
            { Id = id, Name = "Application " + id.ToUpperInvariant(), InstalledVersion = "1.0", AvailableVersion = "2.0" }).ToArray() });
    }

    [Fact]
    public Task Native_Store_without_confirmed_proposed_version_is_information_only() => ThemeContrastTests.OnSta(async () =>
    {
        var authority = new RecordingConfirmation(); var runner = new UpdatesSequenceViewModelTests.Runner();
        var backend = new StoreSourceBackend();
        var vm = new UpdatesViewModel(new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(runner), runner, authority.Authority),
            new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), runner, new UpdatesSequenceViewModelTests.History(), authority,
            new UpdateSequenceService(backend, authority.Authority, new EmptyPendingRebootProbe()));
        var pending = vm.RunAllCommand.ExecuteAsync(null); vm.SkipDecisionCommand.Execute(null); await pending;
        var card = Card(vm, "Store:native");
        Assert.Equal("Store A", Read<string>(card, "Title"));
        Assert.Equal("non pris en charge automatiquement", Read<string>(card, "Status"));
        Assert.Equal("La version proposée ne peut pas être confirmée.", Read<string>(card, "Detail"));
        Assert.Equal("Mettre à jour", Read<string>(card, "ButtonText"));
        Assert.Equal("Même méthode que « Tout mettre à jour ».", Read<string>(card, "MethodText"));
        Assert.False(Read<bool>(card, "HasUpdateAction"));
        Assert.False(Read<bool>(card, "CanUpdate"));
        Assert.False(Command(vm).CanExecute(Read<UpdateSequenceStep>(card, "Step")));
        await Command(vm).ExecuteAsync(Read<UpdateSequenceStep>(card, "Step"));
        Assert.False(vm.IsValidationOpen); Assert.Equal(0, authority.Calls);
    });
    private sealed class StoreSourceBackend : IUpdateSequenceBackend, IStoreUpdateBackend
    {
        public StoreUpdateService StoreUpdates { get; } = new(new StoreReadOnlyClient());
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) => throw new InvalidOperationException("Aucune commande permise.");
    }
    private sealed class StoreReadOnlyClient : IStoreUpdateClient
    {
        public Task<StoreSearch> SearchAsync(CancellationToken token) => Task.FromResult(new StoreSearch(true, "", [new("native", "family", "Store A", "1.0")]));
        public Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token) => throw new InvalidOperationException("Aucune exécution permise.");
        public Task<StoreStart> StartAsync(StorePackage package, CancellationToken token) => throw new InvalidOperationException("Aucune installation permise.");
    }

    [Fact]
    public Task Preview_uses_cached_cards_without_an_extra_driver_read() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new ReviewFixture();
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        try
        {
            Assert.True(f.Vm.IsValidationOpen);
            Assert.Equal(1, f.Scan.Calls);
            Assert.Equal(1, f.Sequence.Scans);
            Assert.Equal(0, f.Sequence.Reads);
            Assert.Equal(0, f.Backend.Verifications);
            Assert.Equal("Epic Games", Read<string>(Card(f.Vm, "Launcher:Epic Games"), "Title"));
        }
        finally { f.Vm.SkipDecisionCommand.Execute(null); await pending; }
    });

    [Fact]
    public Task Driver_examination_uses_its_report_without_scanning_the_sequence() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new ReviewFixture();
        await f.Vm.InspectDriversCommand.ExecuteAsync(null);
        Assert.Equal(1, f.Scan.Calls);
        Assert.False(Assert.Single(f.Scan.Requests).IncludeApplicationUpdates);
        Assert.Equal(0, f.Sequence.Scans);
        Assert.Equal(0, f.Sequence.Reads);
        Assert.Equal(0, f.Backend.Verifications);
        Assert.Contains(f.Vm.DriverCards, line => line.Contains("Fabricant détecté", StringComparison.Ordinal));
    });

    [Theory]
    [InlineData("à vérifier")]
    [InlineData("en erreur")]
    public Task Informational_Steam_game_does_not_change_the_launcher_source_line(string status) => ThemeContrastTests.OnSta(async () =>
    {
        var f = new ReviewFixture();
        var preview = f.Vm.RunAllCommand.ExecuteAsync(null); f.Vm.SkipDecisionCommand.Execute(null); await preview;
        foreach (var card in f.Vm.SingleCards.Where(c => c.Step?.Component == UpdateComponent.Launcher)) card.Status = "à jour";
        await f.Vm.InspectDriversCommand.ExecuteAsync(null);
        var before = Assert.Single(f.Vm.SingleSourceSummary.Split('\n'), line => line.StartsWith("Lanceurs :", StringComparison.Ordinal));
        Assert.Equal("Lanceurs : à jour", before);
        var game = new UpdateTextCardViewModel(string.Empty)
        {
            Step = new("SteamGame:1", "Jeu Steam", UpdateComponent.Launcher) { InformationOnly = true },
            Status = status, Detail = "Jeu contrôlé en lecture seule."
        };
        f.Vm.SingleCards.Add(game);
        await f.Vm.InspectDriversCommand.ExecuteAsync(null);
        Assert.Equal(before, Assert.Single(f.Vm.SingleSourceSummary.Split('\n'), line => line.StartsWith("Lanceurs :", StringComparison.Ordinal)));
        Assert.Equal(status, game.Status);
        Assert.False(game.HasUpdateAction);
        Assert.Equal(1, f.Sequence.Scans);
        Assert.Equal(0, f.Sequence.Reads);
        Assert.Equal(0, f.Backend.Verifications);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(64)]
    public Task Driver_examination_does_not_create_inventory_cards_and_preserves_known_cards(int count) => ThemeContrastTests.OnSta(async () =>
    {
        var f = new ReviewFixture(); f.Scan.InventoryCount = count;
        var preview = f.Vm.RunAllCommand.ExecuteAsync(null); f.Vm.SkipDecisionCommand.Execute(null); await preview;
        var known = Card(f.Vm, "Driver:BIOS");
        await f.Vm.InspectDriversCommand.ExecuteAsync(null);
        Assert.Equal(count, f.Scan.LastInventoryCount);
        Assert.DoesNotContain(f.Vm.SingleCards, card => card.Step?.Id.StartsWith("Inventory:", StringComparison.Ordinal) == true);
        Assert.Same(known, Card(f.Vm, "Driver:BIOS"));
        Assert.Equal(2, f.Scan.Calls);
        Assert.Equal(1, f.Sequence.Scans);
        Assert.Equal(0, f.Sequence.Reads);
        Assert.Equal(0, f.Backend.Verifications);
    });

    [Fact]
    public async Task Explicit_driver_read_failure_has_a_visible_simple_reason()
    {
        var backend = new ReviewBackend { ReadFails = true };
        var service = new UpdateSequenceService(backend, new RecordingConfirmation().Authority);
        var cards = await service.ReadSingleItemsAsync(default);
        var card = Assert.Single(cards);
        Assert.Equal("Pilotes", card.Step.Title);
        Assert.Equal("indisponible", card.VerifiedStatus);
        Assert.Equal("Les pilotes n'ont pas pu être lus. Relancez leur examen.", card.Message);
        Assert.True(card.Step.IsInformational);
        Assert.Equal(1, backend.Verifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Firmware_has_the_same_information_text_in_each_sequence_path(bool finalPass)
    {
        var backend = new ReviewBackend { Firmware = true };
        var confirmation = new RecordingConfirmation();
        var service = new UpdateSequenceService(backend, confirmation.Authority, new EmptyPendingRebootProbe());
        await service.ScanAsync([], default);
        var bios = Assert.Single(service.SingleItems, c => c.Step.Id == "BIOS");
        const string expected = "Virgil n'installe jamais le BIOS ni le firmware ; passez par l'outil officiel du fabricant de l'ordinateur si une mise à jour est proposée.";
        Assert.Equal(expected, bios.Message);
        if (finalPass)
        {
            UpdateSequenceStep[] selection = [new("winget-final", "Applications", UpdateComponent.WingetFinal)];
            var result = await service.ExecuteAsync(selection, confirmation.Authority.Issue(service.CreatePreview(selection),
                new(Virgil.Contracts.Actions.ConfirmationLevel.Reinforced, null)), null, default);
            Assert.Equal(expected, Assert.Single(result.Steps, c => c.Step.Application?.Id == "Firmware.Package").Message);
        }
        else
        {
            var cards = await service.ReadSingleItemsAsync(default);
            Assert.Equal(expected, Assert.Single(cards, c => c.Step.Id.StartsWith("WU:", StringComparison.Ordinal)).Message);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public Task Launcher_package_closes_before_command_and_verifies_its_own_result(bool nativeCard, bool game, bool remaining) => ThemeContrastTests.OnSta(async () =>
    {
        var f = new ReviewFixture(); f.Backend.Game = game; f.Scan.Remaining = remaining;
        var preview = f.Vm.RunAllCommand.ExecuteAsync(null); f.Vm.SkipDecisionCommand.Execute(null); await preview;
        var card = Card(f.Vm, nativeCard ? "Launcher:Epic Games" : "Winget:Epic.Package");
        Assert.Equal("Epic Games", Read<string>(card, "Title"));
        Assert.Equal("reste à installer", Read<string>(card, "Status"));
        Assert.Equal("Cette mise à jour reste à installer.", Read<string>(card, "Detail"));
        Assert.Equal("Mettre à jour", Read<string>(card, "ButtonText"));
        var pending = Command(f.Vm).ExecuteAsync(Read<UpdateSequenceStep>(card, "Step"));
        Assert.Equal("Mettre à jour Epic Games", f.Vm.ValidationTitle);
        f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal(game ? Array.Empty<string>() : new[] { "close:EpicGamesLauncher.exe", "command:Epic.Package" }, f.Backend.Events);
        Assert.Equal(game ? 1 : 2, f.Scan.Calls);
        Assert.Equal(game ? "non lancée" : remaining ? "reste à installer" : "mis à jour", Read<string>(card, "Status"));
        Assert.Equal(game ? "Non lancée : un jeu tourne ou la fermeture du lanceur n'est pas vérifiée."
            : remaining ? nativeCard ? "Cet élément reste proposé au contrôle final." : "Étape exécutée ; contrôle final : cette application reste à installer."
            : nativeCard ? "Cet élément n'est plus proposé au contrôle final." : "Cette application n'est plus proposée au contrôle final.", Read<string>(card, "Detail"));
        var other = Card(f.Vm, nativeCard ? "Winget:Epic.Package" : "Launcher:Epic Games");
        Assert.Equal(game || remaining ? "reste à installer" : "mis à jour", Read<string>(other, "Status"));
        Assert.Equal(game || remaining ? "Applications : reste à installer — 1 reste à installer" : "Applications : mis à jour — 1 mis à jour",
            Assert.Single(f.Vm.SingleSourceSummary.Split('\n'), line => line.StartsWith("Applications :", StringComparison.Ordinal)));
        if (nativeCard && !game && !remaining) await Capture(f.Vm, "10-lanceur-controle-final");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Steam_screen_labels_and_final_status_are_exact(bool session) => ThemeContrastTests.OnSta(async () =>
    {
        var f = new ReviewFixture(); f.Backend.Session = session;
        var preview = f.Vm.RunAllCommand.ExecuteAsync(null); f.Vm.SkipDecisionCommand.Execute(null); await preview;
        var card = Card(f.Vm, "Launcher:Steam");
        Assert.Equal("Steam", Read<string>(card, "Title"));
        Assert.Equal(session ? "reste à installer" : "non pris en charge automatiquement", Read<string>(card, "Status"));
        Assert.Equal(session ? "Cette mise à jour reste à installer." : LauncherCardSummary.NoSession, Read<string>(card, "Detail"));
        Assert.Equal(session, Read<bool>(card, "HasUpdateAction"));
        Assert.Equal(session, Read<bool>(card, "CanUpdate"));
        var pending = Command(f.Vm).ExecuteAsync(Read<UpdateSequenceStep>(card, "Step"));
        if (session) f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(session ? new[] { "close:steam.exe", "-silent" } : [], f.Backend.Events);
        Assert.Equal(session ? "à jour selon Steam" : "non pris en charge automatiquement", Read<string>(card, "Status"));
        Assert.Equal(session ? "Steam ouvert en silencieux ; jeux contrôlés en lecture seule."
            : LauncherCardSummary.NoSession, Read<string>(card, "Detail"));
        if (session) await Capture(f.Vm, "11-steam-controle-final");
    });

    private sealed class ReviewFixture
    {
        public ReviewBackend Backend { get; } = new();
        public ReviewScan Scan { get; }
        public CountedSequence Sequence { get; }
        public UpdatesViewModel Vm { get; }
        public ReviewFixture()
        {
            var confirmation = new RecordingConfirmation(); var runner = new UpdatesSequenceViewModelTests.Runner();
            Scan = new(Backend);
            Sequence = new(new UpdateSequenceService(Backend, confirmation.Authority, new EmptyPendingRebootProbe(), new(Backend, TimeProvider.System)));
            Vm = new(Scan, new WingetUpdateExecutionService(new WingetAvailabilityService(runner), runner, confirmation.Authority),
                new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), runner, new UpdatesSequenceViewModelTests.History(), confirmation, Sequence);
        }
    }
    private sealed class CountedSequence(IUpdateSequenceService service) : IUpdateSequenceService
    {
        public int Scans { get; private set; }
        public int Reads { get; private set; }
        public IReadOnlyList<UpdateSequenceStepResult> SingleItems => service.SingleItems;
        public Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> items, CancellationToken token) { Scans++; return service.ScanAsync(items, token); }
        public Task<IReadOnlyList<UpdateSequenceStepResult>> ReadSingleItemsAsync(CancellationToken token) { Reads++; return service.ReadSingleItemsAsync(token); }
        public Virgil.Contracts.Actions.ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> steps) => service.CreatePreview(steps);
        public Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> steps, Virgil.Core.Actions.ConfirmedPlan? plan,
            IProgress<UpdateSequenceProgress>? progress, CancellationToken token) => service.ExecuteAsync(steps, plan, progress, token);
    }
    private sealed class ReviewScan(ReviewBackend backend) : IUpdateScanService
    {
        public int Calls { get; private set; }
        public bool Remaining { get; set; }
        public int InventoryCount { get; set; } = 1;
        public int LastInventoryCount { get; private set; }
        public List<UpdateScanRequest> Requests { get; } = [];
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token)
        {
            Calls++; Requests.Add(request); LastInventoryCount = InventoryCount;
            return Task.FromResult(new UpdateScanReport { Winget = new() { IsAvailable = true },
                Items = request.IncludeApplicationUpdates && (!backend.Updated || Remaining) ? [ReviewBackend.Package] : [],
                Drivers = new() { WasAnalyzed = true, Drivers = Enumerable.Range(1, InventoryCount).Select(i => new DriverInformation
                { Provider = "Fabricant détecté", PublishedName = "oem" + i + ".inf" }).ToArray() } });
        }
    }
    private sealed class ReviewBackend : IUpdateSequenceBackend, ILauncherShutdownBackend, ISteamUpdateBackend, ISteamClient,
        IDriverSequenceBackend, IDriverInventoryReader, IBiosInformationReader, IDriverPackageSource, IDriverFinalPassBackend
    {
        public static UpdateItem Package => new() { Id = "Epic.Package", Name = "Epic Games", InstalledVersion = "1.0", AvailableVersion = "2.0" };
        public List<string> Events { get; } = [];
        private readonly HashSet<string> _closed = [];
        public bool Updated { get; private set; }
        public bool Game { get; set; }
        public bool Session { get; set; } = true;
        public bool ReadFails { get; set; }
        public bool Firmware { get; set; }
        public int Verifications { get; private set; }
        public IDriverInventoryReader DriverInventory => this;
        public IBiosInformationReader Bios => this;
        public IDriverPackageSource DriverPackages => this;
        public ISteamClient SteamClient => this;
        public TimeProvider SteamClock => TimeProvider.System;
        public TimeSpan SteamWaitLimit => TimeSpan.Zero;
        public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["Epic Games", "Steam"]);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token)
        {
            Assert.Equal(UpdateComponent.Winget, step.Component); Assert.Equal(Package.Id, step.Id);
            Assert.Contains("EpicGamesLauncher.exe", _closed); Events.Add("command:" + step.Id); Updated = true;
            return Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Installation réussie."));
        }
        public Task<LauncherSnapshot> ReadAsync(string name, CancellationToken token) => Task.FromResult(new LauncherSnapshot(true,
            _closed.Contains(LauncherCatalog.Executables[name]) ? [] : Game
                ? [new(1, 0, "C:/Launcher/" + LauncherCatalog.Executables[name]), new(2, 1, "C:/Games/game.exe")]
                : [new(1, 0, "C:/Launcher/" + LauncherCatalog.Executables[name])], ["C:/Games"]));
        public Task CloseAsync(string executable, bool force, CancellationToken token) { _closed.Add(executable); Events.Add("close:" + executable); return Task.CompletedTask; }
        public Task<SteamSession> ReadSessionAsync(CancellationToken token) => Task.FromResult(new SteamSession(Session, "C:/Launcher/steam.exe"));
        public Task<IReadOnlyList<SteamGameState>> ReadGamesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<SteamGameState>>([]);
        public Task<SteamExecutableCheck> CheckExecutableAsync(string? path, CancellationToken token) => Task.FromResult(new SteamExecutableCheck(true, ""));
        public Task<bool> LaunchSilentAsync(string path, CancellationToken token) { Events.Add("-silent"); return Task.FromResult(true); }
        Task<DriverInventoryReport> IDriverInventoryReader.ReadAsync(CancellationToken token) => Task.FromResult(new DriverInventoryReport { WasAnalyzed = true });
        Task<BiosInformation> IBiosInformationReader.ReadAsync(CancellationToken token) => Task.FromResult(new BiosInformation("OEM", "1.0"));
        public Task<IReadOnlyList<DriverPackageOffer>> ScanAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<DriverPackageOffer>>([]);
        public Task<UpdateSequenceStepResult> InstallAsync(UpdateSequenceStep step, CancellationToken token) => throw new InvalidOperationException("Aucun pilote à installer.");
        public Task<UpdateSourceVerification> VerifyAsync(UpdateComponent component, CancellationToken token)
        {
            Verifications++;
            if (ReadFails) throw new InvalidOperationException("Lecture refusée.");
            return Task.FromResult(new UpdateSourceVerification(component, "à jour", "Contrôle en lecture seule.")
                { DriverItems = Firmware ? [new("f813d575-a123-4788-b34a-d547c15c5663:1", "System Firmware", "non pris en charge automatiquement", "", false)] : [] });
        }
        public Task<DriverFinalPassInventory> ReadFinalApplicationsAsync(CancellationToken token) => Task.FromResult(new DriverFinalPassInventory(true,
            Firmware ? [new() { Id = "Firmware.Package", Name = "System Firmware", Source = UpdateSource.FirmwareInformation }] : [], "Lecture effectuée."));
    }

    private sealed class Fixture
    {
        public Backend Backend { get; } = new();
        public UpdatesViewModel Vm { get; }
        public Fixture(IUpdateScanService? scan = null)
        {
            var confirmation = new RecordingConfirmation();
            var runner = new UpdatesSequenceViewModelTests.Runner();
            Vm = new(scan ?? new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(runner), runner, confirmation.Authority),
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
        public IReadOnlyList<DriverUpdateOutcome> KnownDriverUpdates => Empty ? [] : [
            new(First, "Pilote A", "reste à installer", "Ce pilote reste proposé."), new(Second, "Pilote B", "reste à installer", "Ce pilote reste proposé.")];
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
        public Func<UpdateSequenceStep, UpdateSequenceStepResult>? ApplicationHandler { get; set; }
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) => ApplicationHandler is null
            ? throw new InvalidOperationException("Aucun autre élément autorisé.") : Task.FromResult(ApplicationHandler(step));
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
