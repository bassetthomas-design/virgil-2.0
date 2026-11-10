using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class D1bisAConfirmationTests
{
    [Fact]
    public Task Real_confirmation_lists_Store_outside_and_managed_application_without_empty_arrow() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new Fixture();
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        try
        {
            Assert.True(f.Vm.IsValidationOpen);
            Assert.True(f.Vm.IsSequencePreview);
            var applications = f.Vm.SequenceChoices.Where(c => c.Step.Application is not null).ToArray();
            Assert.Collection(applications,
                choice => Assert.Equal("WinGetUI : 5.0 → 6.0 (Applications)", choice.Title),
                choice => Assert.Equal("Photos : 1.0 (Microsoft Store)", choice.Title),
                choice => Assert.Equal("Éditeur local : 3.2 — non pris en charge automatiquement", choice.Title));
            var store = Assert.Single(applications, c => c.Step.Component == UpdateComponent.Store);
            var outside = Assert.Single(applications, c => c.Step.Component == UpdateComponent.OutsideWinget);
            Assert.DoesNotContain("→", store.Title);
            Assert.DoesNotContain("→", outside.Title);
            Assert.DoesNotContain("(Applications)", outside.Title);
            Assert.False(outside.CanSelect);
            Assert.False(outside.IsSelected);
            Assert.True(store.CanSelect);
            Assert.Equal("product-photos", store.Step.Id);
            Assert.Equal("vendor.tool", Assert.Single(applications, c => c.Step.Component == UpdateComponent.Winget).Step.Id);
            await CaptureConfirmation(f.Vm, applications.Select(c => c.Title).ToArray());
            Assert.Empty(f.Backend.Executed);
            Assert.Equal(0, f.Confirmation.Calls);
        }
        finally
        {
            f.Vm.SkipDecisionCommand.Execute(null);
            await pending;
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Application_name_containing_WinGet_survives_while_Virgil_source_labels_are_simple(bool finalAvailable)
    {
        var f = new Fixture(finalAvailable);
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Winget;
        Assert.Equal("WinGetUI : 5.0 → 6.0 (Applications)", Assert.Single(f.Vm.SequenceChoices, c => c.IsSelected && c.CanSelect).Title);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        var card = Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Winget:vendor.tool");
        Assert.Equal("WinGetUI", card.Title);
        Assert.DoesNotContain("ApplicationsUI", card.Title + card.Detail);
        if (finalAvailable) Assert.Contains("WinGetUI : installation réussie.", card.Detail);
        else Assert.Contains("Source des applications indisponible au contrôle final.", f.Vm.Operation.FinalSummary);
        var source = Assert.Single(f.Vm.Operation.FinalSummary.Split('\n'), line => line.StartsWith("Applications :", StringComparison.Ordinal));
        Assert.DoesNotContain("WinGet", source, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("WinGetUI", Assert.Single(f.History.Report!.ExecutedActions).Name);
        Assert.Equal(1, f.Confirmation.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_available_version_has_no_arrow(string available)
    {
        var choice = new UpdateSequenceChoiceViewModel(new("photos", "Photos", UpdateComponent.Store,
            new UpdateItem { Name = "Photos", InstalledVersion = "1.0", AvailableVersion = available, Source = UpdateSource.MicrosoftStore }), () => { });
        Assert.Equal("Photos : 1.0 (Microsoft Store)", choice.Title);
    }

    private static async Task CaptureConfirmation(UpdatesViewModel vm, IReadOnlyList<string> titles)
    {
        var view = new UpdatesView(vm);
        var host = new Border { Child = view, Background = (Brush)Application.Current.FindResource("App.BackgroundBrush") };
        var window = new Window { Content = host, Width = 1600, Height = 1100, ShowInTaskbar = false, Background = host.Background };
        try
        {
            window.Show(); window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var list = Assert.Single(Descendants<ItemsControl>(view), control => ReferenceEquals(control.ItemsSource, vm.SequenceChoices));
            Assert.True(list.IsVisible);
            foreach (var title in titles)
                Assert.True(Assert.Single(Descendants<CheckBox>(list), box => Equals(box.Content, title)).IsVisible);
            var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-d1bis-a");
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, "confirmation-mixed-sources.png"));
            encoder.Save(stream);
        }
        finally { window.Close(); }
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

    private sealed class Fixture
    {
        public Backend Backend { get; } = new();
        public RecordingConfirmation Confirmation { get; } = new();
        public UpdatesSequenceViewModelTests.History History { get; } = new();
        public UpdatesViewModel Vm { get; }
        public Fixture(bool finalAvailable = true)
        {
            var runner = new UpdatesSequenceViewModelTests.Runner();
            var service = new UpdateSequenceService(Backend, Confirmation.Authority, new EmptyPendingRebootProbe());
            Vm = new UpdatesViewModel(new Scan(finalAvailable), new WingetUpdateExecutionService(new WingetAvailabilityService(runner), runner, Confirmation.Authority),
                new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), runner, History, Confirmation, service);
        }
    }
    private sealed class Scan(bool finalAvailable) : IUpdateScanService
    {
        private int _calls;
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport
            {
                Winget = new WingetAvailability { IsAvailable = ++_calls == 1 || finalAvailable },
                Items = _calls == 1 ? [new UpdateItem { Id = "vendor.tool", Name = "WinGetUI", InstalledVersion = "5.0", AvailableVersion = "6.0", Source = UpdateSource.Winget }] : []
            });
    }
    private sealed class Backend : IUpdateSequenceBackend, IStoreUpdateBackend, IOutsideWingetBackend
    {
        public List<UpdateSequenceStep> Executed { get; } = [];
        public StoreUpdateService StoreUpdates { get; } = new(new StoreClient());
        public OutsideWingetInventory OutsideWinget { get; } = new(new Reader());
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token)
        {
            Executed.Add(step);
            return Task.FromResult(new UpdateSequenceStepResult(step, true, false, step.Title + " : installation réussie.") { RestorePointVerified = true });
        }
    }
    private sealed class StoreClient : IStoreUpdateClient
    {
        public Task<StoreSearch> SearchAsync(CancellationToken token) =>
            Task.FromResult(new StoreSearch(true, "", [new("product-photos", "family-photos", "Photos", "1.0")]));
        public Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token) => throw new InvalidOperationException("Aucune installation prévue.");
        public Task<StoreStart> StartAsync(StorePackage package, CancellationToken token) => throw new InvalidOperationException("Aucune installation prévue.");
    }
    private sealed class Reader : IOutsideWingetReader
    {
        public Task<OutsideWingetSnapshot> ReadAsync(CancellationToken token) =>
            Task.FromResult(new OutsideWingetSnapshot([new("Éditeur local", "3.2")], []));
    }
}
