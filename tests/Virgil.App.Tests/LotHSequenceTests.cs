using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotHSequenceTests
{
    [Fact]
    public Task Whole_word_application_identity_keeps_success_text_intact_on_real_sequence() => ThemeContrastTests.OnSta(async () =>
    {
        var scan = new Scan(); var f = new UpdatesSequenceViewModelTests.Fixture(scan: scan); f.Backend.Reboot = false;
        f.Backend.Handler = (step, _) =>
        {
            scan.Remaining.Remove(step.Id);
            return Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Installation réussie pour a."));
        };
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Winget;
        f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        var card = Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Winget:a");
        Assert.Equal("mis à jour", card.Status);
        Assert.Equal("Installation réussie pour cette application.\nContrôle final : cette application n'est plus proposée.", card.Detail);
    });
    [Fact]
    public Task Single_source_counts_both_checked_launchers() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new UpdatesSequenceViewModelTests.Fixture();
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null); f.Vm.SkipDecisionCommand.Execute(null); await pending;
        var launchers = f.Vm.SingleCards.Where(c => c.Step?.Component == UpdateComponent.Launcher).ToArray();
        Assert.Equal(2, launchers.Length);
        Assert.Equal(new[] { "Battle.net", "Steam" }, launchers.Select(c => c.Step!.Id).OrderBy(id => id, StringComparer.Ordinal));
        foreach (var card in launchers) card.Status = "à jour selon Steam";
        await f.Vm.InspectDriversCommand.ExecuteAsync(null);
        Assert.Equal("Lanceurs : à jour selon Steam — 2 à jour selon Steam", Assert.Single(f.Vm.SingleSourceSummary.Split('\n'), line => line.StartsWith("Lanceurs :", StringComparison.Ordinal)));
    });
    [Fact]
    public Task Real_driver_read_failure_produces_unavailable_card_and_source_summary() => ThemeContrastTests.OnSta(async () =>
    {
        var backend = new ReviewBackend { ReadFails = true };
        var confirmation = new RecordingConfirmation();
        var f = new UpdatesSequenceViewModelTests.Fixture(wrap: _ => new UpdateSequenceService(backend, confirmation.Authority, new EmptyPendingRebootProbe()));
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null); f.Vm.SkipDecisionCommand.Execute(null); await pending;
        var card = Assert.Single(f.Vm.SingleCards, c => c.Step?.Component == UpdateComponent.Driver && c.Step.Id == "driver-read");
        Assert.Equal("Pilotes", card.Title); Assert.Equal("indisponible", card.Status);
        Assert.Equal("Les pilotes n'ont pas pu être lus. Relancez leur examen.", card.Detail);
        Assert.Equal("Pilotes : indisponible — 1 indisponible", Assert.Single(f.Vm.SingleSourceSummary.Split('\n'), line => line.StartsWith("Pilotes :", StringComparison.Ordinal)));
    });
    private sealed class Scan : IUpdateScanService
    {
        public HashSet<string> Remaining { get; } = ["a"];
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new() { IsAvailable = true }, Items = Remaining.Select(id =>
                new UpdateItem { Id = id, Name = "Application " + id, InstalledVersion = "1", AvailableVersion = "2" }).ToArray() });
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
}
