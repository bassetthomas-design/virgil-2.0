using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Virgil.App.Controls;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class D1bisAPanelTests
{
    [Theory]
    [InlineData("updated")]
    [InlineData("unsupported")]
    [InlineData("mixed")]
    [InlineData("cancelled")]
    [InlineData("outside")]
    public Task Real_service_and_view_model_prove_each_Store_outcome(string scenario) => ThemeContrastTests.OnSta(async () =>
    {
        var client = new Client(scenario);
        var backend = new Backend(client, scenario);
        var confirmation = new RecordingConfirmation();
        var history = new UpdatesSequenceViewModelTests.History();
        var runner = new UpdatesSequenceViewModelTests.Runner();
        var service = new UpdateSequenceService(backend, confirmation.Authority, new EmptyPendingRebootProbe());
        var vm = new UpdatesViewModel(new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(runner), runner, confirmation.Authority),
            new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), runner, history, confirmation, service);
        client.AfterA = scenario == "cancelled" ? vm.CancelActiveOperation : null;
        var pending = vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Store;
        vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(1, confirmation.Calls);
        var source = Assert.Single(vm.Operation.FinalSummary.Split('\n'), line => line.StartsWith("Microsoft Store :", StringComparison.Ordinal));
        if (scenario == "unsupported")
        {
            Assert.Contains("non pris en charge automatiquement", source);
            Assert.Contains("droits", source);
            Assert.Empty(client.Started);
        }
        else
        {
            var a = Assert.Single(vm.Operation.Steps, card => card.Key == "Store:product-a");
            Assert.Equal("mis à jour", a.Status);
            Assert.Contains("1.0", a.Detail);
            Assert.Contains("2.0", a.Detail);
            Assert.True(client.Reads >= 2);
            if (scenario is "mixed" or "cancelled")
            {
                var b = Assert.Single(vm.Operation.Steps, card => card.Key == "Store:product-b");
                Assert.Equal(scenario == "mixed" ? "en erreur" : "reste à installer", b.Status);
                Assert.DoesNotContain("Terminé", b.Detail, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("reste à installer", source);
                if (scenario == "mixed") Assert.Contains("1 échec", source);
                else Assert.Equal(new[] { "product-a" }, client.Started);
            }
            else Assert.Contains("mis à jour", source);
        }
        if (scenario == "outside")
        {
            Assert.Contains("Non pris en charge automatiquement", vm.Operation.FinalSummary);
            Assert.Contains("Éditeur local", vm.Operation.FinalSummary);
            Assert.Contains("3.2", vm.Operation.FinalSummary);
            Assert.Contains("pas de source de mise à jour connue", vm.Operation.FinalSummary);
            Assert.DoesNotContain("Application connue", vm.Operation.FinalSummary);
            Assert.DoesNotContain("Tout est à jour", vm.Operation.FinalSummary);
        }
        foreach (var card in vm.Operation.Steps)
        {
            Assert.DoesNotContain("Identité", card.Detail);
            Assert.DoesNotContain("product-", card.Detail);
            Assert.DoesNotContain("winget", card.Title + card.Detail, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("à terminer dans le Microsoft Store", source);
        Assert.DoesNotContain("winget", vm.Operation.FinalSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(vm.Operation.FinalSummary, history.Report!.SimpleView);
        if (scenario != "cancelled") await Capture(vm.Operation, scenario);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Remaining_application_does_not_hide_failure_or_safety_block(bool blocked)
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new D1WingetIdentityTests.IdentityScan(false));
        f.Backend.Handler = (step, _) => Task.FromResult(new UpdateSequenceStepResult(step, step.Id == "app-a", false,
            step.Id == "app-a" ? "Installation réussie." : blocked ? "Installation bloquée par une garde de sécurité." : "Échec d'installation."));
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Winget;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        var source = Assert.Single(f.Vm.Operation.FinalSummary.Split('\n'), line => line.StartsWith("Applications :", StringComparison.Ordinal));
        Assert.Contains(blocked ? "reste à installer ; bloquée" : "reste à installer ; 1 échec", source);
        Assert.Equal(blocked ? "bloquée" : "en erreur", Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Winget:app-b").Status);
    }

    private static async Task Capture(LongOperationViewModel operation, string name)
    {
        var panel = new OperationProgressPanel { DataContext = operation };
        var host = new Border { Child = panel, Background = (Brush)Application.Current.FindResource("App.BackgroundBrush") };
        var window = new Window { Content = host, Width = 1600, Height = 1000, ShowInTaskbar = false, Background = host.Background };
        try
        {
            window.Show(); window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Assert.True(panel.IsVisible);
            var path = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-d1bis-a");
            Directory.CreateDirectory(path);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(path, name + ".png")); encoder.Save(stream);
        }
        finally { window.Close(); }
    }
    private sealed class Scan : IUpdateScanService
    {
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new WingetAvailability { IsAvailable = true } });
    }
    private sealed class Client(string scenario) : IStoreUpdateClient
    {
        public List<string> Started { get; } = [];
        private readonly HashSet<string> _updated = [];
        public int Reads { get; private set; }
        public Action? AfterA { get; set; }
        public Task<StoreSearch> SearchAsync(CancellationToken token)
        {
            StorePackage[] all = scenario is "mixed" or "cancelled"
                ? [new("product-a", "family-a", "Photos", "1.0"), new("product-b", "family-b", "Courrier", "1.0")]
                : [new("product-a", "family-a", "Photos", "1.0")];
            return Task.FromResult(scenario == "unsupported" ? new StoreSearch(false, "Les droits de ce compte ne permettent pas cette mise à jour.", [])
                : new StoreSearch(true, "", all.Where(p => !_updated.Contains(p.ProductId)).ToArray()));
        }
        public Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token)
        {
            Reads++;
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>
                { ["family-a"] = _updated.Contains("product-a") ? "2.0" : "1.0", ["family-b"] = "1.0" });
        }
        public Task<StoreStart> StartAsync(StorePackage package, CancellationToken token)
        {
            Started.Add(package.ProductId);
            if (package.ProductId == "product-b") return Task.FromResult(new StoreStart(false, "Échec de l'installation."));
            _updated.Add(package.ProductId); AfterA?.Invoke();
            return Task.FromResult(new StoreStart(true, ""));
        }
    }
    private sealed class Backend(Client client, string scenario) : IUpdateSequenceBackend, IStoreUpdateBackend, IOutsideWingetBackend
    {
        public StoreUpdateService StoreUpdates { get; } = new(client);
        public OutsideWingetInventory OutsideWinget { get; } = new(new Reader(scenario));
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) =>
            Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Protection vérifiée.") { RestorePointVerified = true });
    }
    private sealed class Reader(string scenario) : IOutsideWingetReader
    {
        public Task<OutsideWingetSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(scenario == "outside"
            ? new OutsideWingetSnapshot([new("Éditeur local", "3.2"), new("Application connue", "1")], ["Application connue"])
            : new OutsideWingetSnapshot([], []));
    }
}
