using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Processes;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotMisesAJourSteamViewTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public Task UpdatesView_scan_displays_steam_games_statuses_freshness_and_preserves_store_cards() =>
        ThemeContrastTests.OnSta(async () =>
        {
            var fixture = new Fixture(running: true, connected: true);
            var view = new UpdatesView(fixture.Vm);
            using var window = Show(view);
            await Scan(fixture.Vm, view);

            var texts = VisibleTexts(view);
            var buttons = VisibleButtons(view);
            await Write("steam-scan.txt", texts.Concat(buttons.Select(button => button.Content?.ToString() ?? "")));
            Assert.Equal("Launcher:Steam", Assert.Single(fixture.Vm.SingleCards, card => card.Step?.Id == "Steam").Key);
            var games = fixture.Vm.SingleCards.Where(card => card.Step?.Id.StartsWith("SteamGame:", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(new[] { "SteamGame:10", "SteamGame:20", "SteamGame:30", "SteamGame:40" },
                games.Select(card => card.Step!.Id).OrderBy(id => id));
            Assert.Equal(new[] { "à jour localement", "en file d’attente", "en téléchargement", "téléchargement arrêté" },
                games.OrderBy(card => card.Step!.Id).Select(card => card.Status));
            Assert.All(games, game =>
            {
                Assert.True(game.Step!.InformationOnly);
                Assert.False(game.HasUpdateAction);
                Assert.DoesNotContain(buttons, button => ReferenceEquals(button.DataContext, game));
                Assert.Contains(game.Status, texts);
                Assert.Contains(game.Detail, texts);
            });
            Assert.Contains("Store:store-demo", fixture.Vm.SourceCards.Select(card => card.Key));
            Assert.Contains("Lanceurs : reste à installer — 1 reste à installer", fixture.Vm.SingleSourceSummary.Split('\n'));
            Assert.Contains("Source : Microprogramme (BIOS)", new UpdateItemViewModel(
                new UpdateItem { Source = UpdateSource.FirmwareInformation }, () => { }).Lines);
            Assert.Empty(fixture.Runner.Launches);
        }).WaitAsync(Deadline);

    [Fact]
    public Task Closed_or_offline_steam_offers_open_steam_action_launching_steam_downloads() =>
        ThemeContrastTests.OnSta(async () =>
        {
            var scenarios = new List<(string[] Texts, Button[] Buttons, string[] Messages, Fixture Fixture)>();
            foreach (var running in new[] { false, true })
            {
                var fixture = new Fixture(running, connected: false);
                var view = new UpdatesView(fixture.Vm);
                using var window = Show(view);
                await Scan(fixture.Vm, view);
                var texts = VisibleTexts(view);
                var buttons = VisibleButtons(view);
                var messages = new List<string>();
                view.VirgilMessageRequested += messages.Add;
                var button = buttons.FirstOrDefault(button => Equals(button.Content, "Ouvrir Steam"));
                if (button?.Command is IAsyncRelayCommand command)
                {
                    await command.ExecuteAsync(null).WaitAsync(Deadline);
                    fixture.Runner.FailLaunch = true;
                    await command.ExecuteAsync(null).WaitAsync(Deadline);
                }
                scenarios.Add((texts, buttons, messages.ToArray(), fixture));
            }

            await Write("steam-open-action.txt", scenarios.SelectMany(scenario =>
                scenario.Texts.Concat(scenario.Buttons.Select(button => button.Content?.ToString() ?? "")).Concat(scenario.Messages)));
            var counts = "1 jeu en téléchargement, 1 jeu en file d’attente, 1 jeu arrêté, 1 jeu à jour localement.";
            Assert.Contains("Steam est fermé : " + counts + " Ouvrez Steam pour vérifier « Téléchargements ».", scenarios[0].Texts);
            Assert.Contains("Steam est hors ligne ou sans session active : " + counts + " Connectez Steam puis vérifiez « Téléchargements ».", scenarios[1].Texts);
            foreach (var scenario in scenarios)
            {
                var card = Assert.Single(scenario.Fixture.Vm.SingleCards, card => card.Key == "Launcher:Steam");
                var property = typeof(UpdateTextCardViewModel).GetProperty("HasSteamAction");
                Assert.NotNull(property);
                Assert.True(Assert.IsType<bool>(property.GetValue(card)));
                var button = Assert.Single(scenario.Buttons, button => Equals(button.Content, "Ouvrir Steam"));
                Assert.True(button.IsEnabled);
                Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command);
                Assert.Equal(2, scenario.Fixture.Runner.Launches.Count);
                Assert.All(scenario.Fixture.Runner.Launches, launch =>
                {
                    Assert.Equal("steam://open/downloads", launch.FileName);
                    Assert.True(launch.UseShellExecute);
                    Assert.Empty(launch.Arguments);
                });
                Assert.Equal(new[] { "Steam ouvert.\nVirgil ne lance aucune installation.", "Ouverture de Steam impossible." }, scenario.Messages);
            }
        }).WaitAsync(Deadline);

    [Fact]
    public Task Steam_session_keeps_no_user_property_and_informational_games_keep_launcher_line_unchanged() =>
        ThemeContrastTests.OnSta(async () =>
        {
            Assert.DoesNotContain(typeof(SteamSession).GetProperties(), property => property.Name.Contains("User", StringComparison.Ordinal));
            var fixture = new Fixture(running: true, connected: true);
            fixture.Vm.SingleCards.Add(new("") { Step = new("Steam", "Steam", UpdateComponent.Launcher), Status = "à jour" });
            fixture.Vm.SingleCards.Add(new("")
            {
                Step = new("SteamGame:10", "Jeu Steam", UpdateComponent.Launcher) { InformationOnly = true },
                Status = "en erreur"
            });
            await fixture.Vm.InspectDriversCommand.ExecuteAsync(null).WaitAsync(Deadline);
            Assert.Contains("Lanceurs : à jour", fixture.Vm.SingleSourceSummary.Split('\n'));
        }).WaitAsync(Deadline);

    [Fact]
    public async Task H2_read_single_items_stays_isolated_and_windows_registry_rejects_writes()
    {
        await ThemeContrastTests.OnSta(async () =>
        {
            var fixture = new Fixture(running: false, connected: false);
            var vm = fixture.Vm;
            await vm.ScanUpdatesCommand.ExecuteAsync(null).WaitAsync(Deadline);
            await vm.InspectDriversCommand.ExecuteAsync(null).WaitAsync(Deadline);
            var preview = vm.RunAllCommand.ExecuteAsync(null);
            Assert.True(vm.IsValidationOpen);
            vm.SkipDecisionCommand.Execute(null);
            await preview.WaitAsync(Deadline);
            Assert.Equal(0, fixture.Backend.DriverVerifications);
            await fixture.Sequence.ReadSingleItemsAsync(default).WaitAsync(Deadline);
            Assert.Equal(1, fixture.Backend.DriverVerifications);
        }).WaitAsync(Deadline);

        const string script = """
            $ErrorActionPreference='Stop'
            $key=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',$false)
            try {
                $key.SetValue('VirgilReadOnlyProbe', '')
                exit 2
            } catch [System.UnauthorizedAccessException] {
                Write-Output 'UnauthorizedAccessException'
            } finally {
                $key.Dispose()
            }
            """;
        var result = await new ProcessRunner().RunAsync(new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script],
            Deadline), default).WaitAsync(Deadline);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("UnauthorizedAccessException", result.StandardOutput.Trim());
    }

    private static HwndSource Show(UpdatesView view) => new(new HwndSourceParameters("T6b")
    {
        Width = 1600, Height = 1100, WindowStyle = 0x10000000
    }) { RootVisual = view };

    private static async Task Scan(UpdatesViewModel vm, UpdatesView view)
    {
        await vm.ScanUpdatesCommand.ExecuteAsync(null).WaitAsync(Deadline);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
    }

    private static string[] VisibleTexts(DependencyObject view) =>
        Descendants<TextBlock>(view).Where(text => text.IsVisible).Select(text => text.Text).ToArray();

    private static Button[] VisibleButtons(DependencyObject view) =>
        Descendants<Button>(view).Where(button => button.IsVisible).ToArray();

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static Task Write(string name, IEnumerable<string> lines)
    {
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-t6b");
        Directory.CreateDirectory(directory);
        return File.WriteAllLinesAsync(Path.Combine(directory, name), lines, new UTF8Encoding(false)).WaitAsync(Deadline);
    }

    private sealed class Fixture
    {
        public Runner Runner { get; } = new();
        public Backend Backend { get; }
        public UpdateSequenceService Sequence { get; }
        public UpdatesViewModel Vm { get; }

        public Fixture(bool running, bool connected)
        {
            var confirmation = new RecordingConfirmation();
            var reboot = new EmptyPendingRebootProbe();
            Backend = new Backend(Runner, running, connected);
            Sequence = new(Backend, confirmation.Authority, reboot);
            Vm = new(new ScanService(), new WingetUpdateExecutionService(new WingetAvailabilityService(Runner), Runner, confirmation.Authority),
                new WindowsUpdateStatusService(reboot), Runner, new UpdatesSequenceViewModelTests.History(), confirmation, Sequence);
        }
    }

    private sealed class ScanService : IUpdateScanService
    {
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport
            {
                Winget = new() { IsAvailable = true },
                Items = request.IncludeApplicationUpdates
                    ? [new() { Id = "firmware-demo", Name = "BIOS local", Source = UpdateSource.FirmwareInformation }] : []
            });
    }

    private sealed class Backend : IUpdateSequenceBackend, IStoreUpdateBackend, ISteamUpdateBackend, ILauncherShutdownBackend,
        IDriverSequenceBackend, IDriverInventoryReader, IBiosInformationReader, IDriverPackageSource
    {
        private readonly bool _running;
        public int DriverVerifications { get; private set; }
        public ISteamClient SteamClient { get; }
        public IDriverInventoryReader DriverInventory => this;
        public IBiosInformationReader Bios => this;
        public IDriverPackageSource DriverPackages => this;

        Task<DriverInventoryReport> IDriverInventoryReader.ReadAsync(CancellationToken token) =>
            Task.FromResult(new DriverInventoryReport { WasAnalyzed = true });
        Task<BiosInformation> IBiosInformationReader.ReadAsync(CancellationToken token) =>
            Task.FromResult(new BiosInformation("OEM", "1.0"));
        public Task<IReadOnlyList<DriverPackageOffer>> ScanAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<DriverPackageOffer>>([]);
        public Task<UpdateSequenceStepResult> InstallAsync(UpdateSequenceStep step, CancellationToken token) =>
            throw new InvalidOperationException();
        public StoreUpdateService StoreUpdates { get; } = new(new StoreClient());

        public Backend(Runner runner, bool running, bool connected)
        {
            _running = running;
            SteamClient = new SteamWindowsClient(runner, new SteamAccess(connected), this, _ => true);
        }

        public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<string>>(["Steam"]);

        public Task<UpdateSourceVerification> VerifyAsync(UpdateComponent component, CancellationToken token)
        {
            if (component == UpdateComponent.Driver) DriverVerifications++;
            return Task.FromResult(new UpdateSourceVerification(component, "à jour", ""));
        }

        public Task<LauncherSnapshot> ReadAsync(string name, CancellationToken token) =>
            Task.FromResult(new LauncherSnapshot(true, _running ? [new(1, 0, @"C:\Steam\steam.exe")] : [], []));

        public Task CloseAsync(string executable, bool force, CancellationToken token) => throw new InvalidOperationException();
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) => throw new InvalidOperationException();
    }

    private sealed class SteamAccess(bool connected) : ISteamReadAccess
    {
        public bool ReadHasActiveUser() => connected;
        public string ReadExecutablePath() => @"C:\Steam\steam.exe";
        public bool FileExists(string path) => true;

        public string ReadText(string path) => Path.GetFileName(path) switch
        {
            "libraryfolders.vdf" => """ "libraryfolders" { "0" { "path" "C:\\Steam" } "1" { "path" "D:\\SteamLibrary" } } """,
            "appmanifest_10.acf" => """ "AppState" { "name" "Portal" "StateFlags" "4" "buildid" "1" "LastUpdated" "1763890200" } """,
            "appmanifest_20.acf" => """ "AppState" { "name" "Half-Life" "StateFlags" "1026" "buildid" "1" } """,
            "appmanifest_30.acf" => """ "AppState" { "name" "Team Fortress" "StateFlags" "1048576" "buildid" "1" } """,
            "appmanifest_40.acf" => """ "AppState" { "name" "Left 4 Dead" "StateFlags" "512" "buildid" "1" } """,
            _ => throw new IOException(path)
        };

        public IReadOnlyList<string> EnumerateManifests(string directory) =>
            directory.StartsWith("D:", StringComparison.Ordinal) ? [Path.Combine(directory, "appmanifest_10.acf")]
                : [Path.Combine(directory, "appmanifest_20.acf"), Path.Combine(directory, "appmanifest_30.acf"), Path.Combine(directory, "appmanifest_40.acf")];
    }

    private sealed class StoreClient : IStoreUpdateClient
    {
        public Task<StoreSearch> SearchAsync(CancellationToken token) =>
            Task.FromResult(new StoreSearch(true, "", [new("store-demo", "family", "Photos", "1.0")]));
        public Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token) => throw new InvalidOperationException();
        public Task<StoreStart> StartAsync(StorePackage package, CancellationToken token) => throw new InvalidOperationException();
    }

    private sealed class Runner : IProcessRunner
    {
        public List<ProcessLaunchRequest> Launches { get; } = [];
        public bool FailLaunch { get; set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token) => throw new InvalidOperationException();

        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token)
        {
            Launches.Add(request);
            return Task.FromResult(new ProcessLaunchResult(!FailLaunch, FailLaunch ? "Échec." : null));
        }
    }
}
