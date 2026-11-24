using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Virgil.App.Hosting;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Reports;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class LotMisesAJourTests
{
    private const string ManagedId = "9NBLGGH4NNS1";
    private const string StoreReason = "Les droits de ce compte ne permettent pas cette recherche.";
    private static readonly StorePackage Matched = new("product-a", "family-a", "Photos", "1.0");
    private static readonly StorePackage Orphan = new("orphan", "family-orphan", "", "");
    private static readonly UpdateItem Managed = new() { Id = ManagedId, Source = UpdateSource.MicrosoftStore };
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Store_unmatched_and_unreadable_manifest_keep_matched_updates_and_mark_partial()
    {
        var runner = new FakeRunner { Store = new(true, "", [Matched, Orphan]) };
        var service = new StoreUpdateService(new StoreWindowsClient(runner));
        var steps = await service.ScanAsync([], TestContextToken).WaitAsync(Deadline);
        var script = Assert.Single(runner.Runs).Arguments.Last();
        Assert.DoesNotContain("throw 'Version installée introuvable'", script);
        Assert.Matches(@"try\s*\{\s*\$manifest=Get-AppxPackageManifest", script);
        Assert.Equal(new[] { "product-a" }, steps.Select(step => step.Id));
        Assert.Equal("vérification partielle", Partial(service).VerifiedStatus);

        steps = await service.ScanAsync([Managed with { Id = Matched.ProductId }], TestContextToken).WaitAsync(Deadline);
        Assert.Empty(steps);
        Assert.Equal("vérification partielle", Partial(service).VerifiedStatus);

        runner.Store = new(false, StoreReason, []);
        await service.ScanAsync([], TestContextToken).WaitAsync(Deadline);
        var partial = Partial(service);
        Assert.Equal("vérification partielle", partial.VerifiedStatus);
        Assert.Equal(StoreReason, partial.Message);
        Assert.Empty(runner.Launches);
    }

    [Fact]
    public async Task Windows_update_read_only_scan_distinguishes_detected_current_and_unavailable_states()
    {
        var runner = new FakeRunner { Wua = new(0, """{"Count":2,"Titles":["KB1","KB2"]}""", "") };
        using var services = Services(runner);
        var scan = services.GetRequiredService<IUpdateScanService>();
        await scan.ScanAsync(UpdateScanRequest.QuickAvailability, null, TestContextToken).WaitAsync(Deadline);
        Assert.DoesNotContain(runner.Runs, IsWua);

        var detected = await ScanDeep(scan);
        Assert.Equal("2 mises à jour Windows Update détectées (ouverture manuelle ou « Tout mettre à jour »).", detected.WindowsUpdate.Status);
        var script = Assert.Single(runner.Runs, IsWua).Arguments.Last();
        Assert.Contains("IsInstalled=0 and IsHidden=0", script);
        Assert.DoesNotContain("CreateUpdateInstaller", script);
        Assert.DoesNotContain("Restart-Computer", script);

        runner.Wua = new(0, """{"Count":0,"Titles":[]}""", "");
        Assert.Equal("À jour selon Windows Update.", (await ScanDeep(scan)).WindowsUpdate.Status);

        runner.Wua = new(-1, "", "", TimedOut: true);
        var unavailable = await ScanDeep(scan);
        const string timeout = "Lecture Windows Update interrompue (délai dépassé) ; ouvrez Windows Update pour vérifier.";
        Assert.Equal(timeout, unavailable.WindowsUpdate.Status);
        Assert.Contains(timeout, unavailable.Errors);
        Assert.Equal("App.One", Assert.Single(unavailable.Items).Id);
        Assert.Empty(runner.Launches);
    }

    [Fact]
    public async Task Screen_scan_covers_all_sources_and_keeps_valid_sources_when_another_fails()
    {
        var runner = new FakeRunner
        {
            IncludeManaged = true,
            Wua = new(0, """{"Count":1,"Titles":["KB1"]}""", ""),
            Store = new(true, "", [new(ManagedId, "family-managed", "Courrier", "1.0"), Matched, Orphan])
        };
        using var services = Services(runner);
        var vm = services.GetRequiredService<UpdatesViewModel>();
        await vm.ScanUpdatesCommand.ExecuteAsync(null).WaitAsync(Deadline);

        Assert.Equal("2 mises à jour détectées via WinGet.", vm.WingetStatus);
        Assert.Equal("1 mise à jour Windows Update détectée (ouverture manuelle ou « Tout mettre à jour »).", vm.WindowsUpdateStatus);
        Assert.Equal(new[] { "Store:product-a", "Store:store-partial" }, vm.SourceCards.Select(card => card.Key));
        Assert.Equal("vérification partielle", Assert.Single(vm.SourceCards, card => card.Key == "Store:store-partial").Status);
        Assert.Contains("Microsoft Store : vérification partielle — 1 vérification partielle, 1 non pris en charge automatiquement",
            vm.SingleSourceSummary.Split('\n'));

        runner.WingetUnavailable = true;
        runner.Wua = new(1, "", "VIRGIL_WUA_001");
        await vm.ScanUpdatesCommand.ExecuteAsync(null).WaitAsync(Deadline);
        Assert.StartsWith("WinGet indisponible :", vm.WingetStatus);
        Assert.Equal("Recherche Windows Update incomplète ; ouvrez Windows Update pour vérifier.", vm.WindowsUpdateStatus);
        Assert.Contains(vm.SourceCards, card => card.Key == "Store:product-a");
        Assert.Empty(runner.Launches);
    }

    [Fact, Trait("Category", "Windows")]
    public Task Rendered_updates_screen_exposes_partial_store_card_and_opens_store_without_installing() =>
        ThemeContrastTests.OnStaWithin(Deadline, async deadline =>
        {
            var runner = new FakeRunner { Store = new(false, StoreReason, []) };
            using var services = Services(runner);
            var view = services.GetRequiredService<UpdatesView>();
            var vm = services.GetRequiredService<UpdatesViewModel>();
            using var window = new HwndSource(new HwndSourceParameters("T6a")
            {
                Width = 1600,
                Height = 1100,
                WindowStyle = 0x10000000
            });
            window.RootVisual = view;
            await WpfConditionWait.CompletionAsync(vm.ScanUpdatesCommand.ExecuteAsync(null), "scan des mises à jour", deadline);
            string[] VisibleTexts() => Descendants<TextBlock>(view)
                .Where(element => element.IsVisible).Select(element => element.Text).ToArray();
            await WpfConditionWait.LayoutAsync(view, VisibleTexts, deadline,
                "1 mise à jour détectée via WinGet.", "À jour selon Windows Update.", "vérification partielle", StoreReason);

            var texts = VisibleTexts();
            var buttons = Descendants<Button>(view).Where(element => element.IsVisible).ToArray();
            var evidence = new[] { "# textes" }.Concat(texts).Append("# boutons")
                .Concat(buttons.Select(button => button.Content?.ToString() ?? "")).ToArray();
            try
            {
                Assert.Contains("1 mise à jour détectée via WinGet.", texts);
                Assert.Contains("À jour selon Windows Update.", texts);
                Assert.Contains("vérification partielle", texts);
                Assert.Contains(StoreReason, texts);
                var storeButton = Assert.Single(buttons, button => Equals(button.Content, "Ouvrir le Microsoft Store"));
                Assert.True(storeButton.IsEnabled);
                await WpfConditionWait.CompletionAsync(
                    Assert.IsAssignableFrom<IAsyncRelayCommand>(storeButton.Command).ExecuteAsync(null),
                    "ouverture du Microsoft Store", deadline);
                var launch = Assert.Single(runner.Launches);
                Assert.Equal("ms-windows-store://downloadsandupdates", launch.FileName);
                Assert.True(launch.UseShellExecute);
                Assert.Empty(launch.Arguments);
            }
            finally
            {
                var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-t6a");
                Directory.CreateDirectory(directory);
                await WpfConditionWait.CompletionAsync(File.WriteAllLinesAsync(Path.Combine(directory, "mises-a-jour-sources.txt"),
                    evidence, new UTF8Encoding(false), TestContextToken), "écriture de la preuve Store", deadline);
            }
        });

    [Fact]
    public async Task Store_up_to_date_without_unmatched_items_shows_current_status_at_scan()
    {
        var runner = new FakeRunner { IncludeApp = false };
        using var services = Services(runner);
        var vm = services.GetRequiredService<UpdatesViewModel>();
        await vm.ScanUpdatesCommand.ExecuteAsync(null).WaitAsync(Deadline);

        Assert.Equal("À jour selon WinGet.", vm.WingetStatus);
        Assert.Equal("À jour selon Windows Update.", vm.WindowsUpdateStatus);
        Assert.Contains("Microsoft Store : à jour", vm.SingleSourceSummary.Split('\n'));
        var card = Assert.Single(vm.SourceCards);
        Assert.Equal("Store:store-current", card.Key);
        Assert.Equal("à jour", card.Status);
        var property = typeof(UpdateTextCardViewModel).GetProperty("HasStoreAction");
        Assert.NotNull(property);
        Assert.False(Assert.IsType<bool>(property.GetValue(card)));
        Assert.Empty(runner.Launches);
    }

    [Fact]
    public void Winget_upgrade_keeps_force_and_include_unknown_flags()
    {
        var capabilities = WingetAvailabilityService.GetCapabilities("1.8.1911");
        Assert.Contains("--include-unknown", WingetUpdateScanService.BuildScanArguments(capabilities));
        var arguments = WingetUpdateExecutionService.BuildUpgradeArguments("App.One", capabilities);
        Assert.Contains("--force", arguments);
        Assert.Contains("--include-unknown", arguments);
    }

    [Fact]
    public async Task Pending_reboot_and_store_dedup_in_sequence_remain_intact()
    {
        Assert.True(new WindowsUpdateStatusService(new RebootProbe(new(true, ["CBS"]))).ReadStatus().PendingRebootDetected);
        var confirmation = new RecordingConfirmation();
        var backend = new StoreBackend();
        var sequence = new UpdateSequenceService(backend, confirmation.Authority, new RebootProbe(new(false, [])));
        var steps = await sequence.ScanAsync([Managed], TestContextToken).WaitAsync(Deadline);
        Assert.DoesNotContain(steps, step => step.Component == UpdateComponent.Store);
        Assert.Equal(ManagedId, Assert.Single(steps, step => step.Component == UpdateComponent.Winget).Id);
    }

    private static CancellationToken TestContextToken => default;

    private static UpdateSequenceStepResult Partial(StoreUpdateService service)
    {
        var property = typeof(StoreUpdateService).GetProperty("Partial");
        Assert.NotNull(property);
        return Assert.IsType<UpdateSequenceStepResult>(property.GetValue(service));
    }

    private static Task<UpdateScanReport> ScanDeep(IUpdateScanService scan) =>
        scan.ScanAsync(UpdateScanRequest.DeepPreview with { IncludeDriverInventory = false }, null, TestContextToken).WaitAsync(Deadline);

    private static ServiceProvider Services(FakeRunner runner)
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        services.Replace(ServiceDescriptor.Singleton<IProcessRunner>(runner));
        services.Replace(ServiceDescriptor.Transient<WingetAvailabilityService>(_ => new(runner, [@"C:\tools\winget.exe"])));
        services.Replace(ServiceDescriptor.Transient<IPendingRebootProbe>(_ => new RebootProbe(new(false, []))));
        services.Replace(ServiceDescriptor.Singleton<IReportHistoryService>(new UpdatesSequenceViewModelTests.History()));
        return services.BuildServiceProvider();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static bool IsWua(ProcessRunRequest request) =>
        request.Arguments.Last().Contains("Microsoft.Update.Session", StringComparison.Ordinal);

    private sealed class RebootProbe(PendingRebootStatus status) : IPendingRebootProbe
    {
        public PendingRebootStatus Read() => status;
    }

    private sealed class StoreBackend : IUpdateSequenceBackend, IStoreUpdateBackend
    {
        public StoreUpdateService StoreUpdates { get; } = new(new InMemoryStore());

        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Aucune exécution attendue.");
    }

    private sealed class InMemoryStore : IStoreUpdateClient
    {
        public Task<StoreSearch> SearchAsync(CancellationToken token) =>
            Task.FromResult(new StoreSearch(true, "", [new(ManagedId, "family-managed", "Courrier", "1.0")]));

        public Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token) =>
            throw new InvalidOperationException("Aucune installation attendue.");

        public Task<StoreStart> StartAsync(StorePackage package, CancellationToken token) =>
            throw new InvalidOperationException("Aucune installation attendue.");
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Runs { get; } = [];
        public List<ProcessLaunchRequest> Launches { get; } = [];
        public StoreSearch Store { get; set; } = new(true, "", []);
        public ProcessRunResult Wua { get; set; } = new(0, """{"Count":0,"Titles":[]}""", "");
        public bool IncludeApp { get; init; } = true;
        public bool IncludeManaged { get; init; }
        public bool WingetUnavailable { get; set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Runs.Add(request);
            var argument = request.Arguments.Last();
            var result = request.Arguments[0] switch
            {
                "--version" => WingetUnavailable ? new(-1, "", "", LaunchError: "WinGet indisponible.") : new(0, "1.8.1911", ""),
                "upgrade" => new(0, WingetTable(), ""),
                "/enum-drivers" => new(0, "", ""),
                "-NoProfile" when argument.Contains("Get-WinGetPackage", StringComparison.Ordinal) =>
                    new(0, IncludeApp ? """[{"Id":"App.One","Name":"App One","InstalledVersion":"1.0","Source":"winget"}]""" : "[]", ""),
                "-NoProfile" when IsWua(request) => Wua,
                "-NoProfile" when argument.Contains("SearchForAllUpdatesAsync", StringComparison.Ordinal) =>
                    new(0, JsonSerializer.Serialize(Store), ""),
                _ => throw new InvalidOperationException($"Commande inattendue : {request.FileName} {argument}")
            };
            return Task.FromResult(result);
        }

        private string WingetTable()
        {
            var rows = new List<string>
            {
                "Name       Id            Version Available Source",
                "--------------------------------------------------",
                "App One    App.One       1.0     2.0       winget"
            };
            if (IncludeManaged) rows.Add("Courrier   9NBLGGH4NNS1  1.0     2.0       msstore");
            return string.Join('\n', rows);
        }

        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Launches.Add(request);
            return Task.FromResult(new ProcessLaunchResult(true, null));
        }
    }
}
