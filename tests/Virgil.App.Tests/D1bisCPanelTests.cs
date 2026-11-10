using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
public sealed class D1bisCPanelTests
{
    private const string PackageId = "AMD.AMDSoftwareCloudEdition";
    private const string PackageName = "AMD Software: Cloud Edition";
    private const string Unsupported = "non pris en charge automatiquement";
    private const string FirstTitle = "NVIDIA - Display - 32.0.15.7216";
    private const string SecondTitle = "Intel - System - 10.1.1.38";
    private const string WuTitle = "Windows Update : pilotes (jusqu'à 3 passes)";
    private const string BiosTitle = "BIOS : information seulement, jamais installé par Virgil — 1.2.3";

    [Fact]
    public Task Confirmation_uses_one_plan_and_information_rows_cannot_be_selected() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new Fixture("partial");
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        try
        {
            await Capture(f.Vm, "confirmation");
            Assert.True(f.Vm.IsValidationOpen);
            Assert.Equal(0, f.Confirmation.Calls);
            Assert.Empty(f.Backend.InstalledPackages);
            Assert.Empty(f.Backend.DriverCalls);
            var wu = Assert.Single(f.Vm.SequenceChoices, c => c.Step.Id == "drivers");
            Assert.True(wu.IsSelected);
            Assert.True(wu.CanSelect);
            Assert.True(UpdateSequenceService.IsElevated(wu.Step.Component));
            Assert.True(Assert.Single(f.Vm.SequenceChoices, choice => choice.Step.Component == UpdateComponent.Restore).IsSelected);
            Assert.True(Assert.Single(f.Vm.SequenceChoices, choice => choice.Step.Component == UpdateComponent.PendingReboot).IsSelected);
            Assert.Equal(WuTitle, wu.Title);
            var package = Assert.Single(f.Vm.SequenceChoices, c => c.Step.Id == "WinGet:" + PackageId);
            Assert.True(package.CanSelect);
            Assert.True(package.IsSelected);
            Assert.Equal(PackageName, package.Title);
            var bios = Assert.Single(f.Vm.SequenceChoices, c => c.Step.Id == "BIOS");
            Assert.False(bios.CanSelect);
            Assert.False(bios.IsSelected);
            Assert.Equal(BiosTitle, bios.Title);
            Assert.Contains("BIOS : information seulement, jamais installé par Virgil", f.Vm.ValidationCommand);
            var provider = Assert.Single(f.Vm.SequenceChoices, c => c.Step.Id == "Provider:Autre fabricant");
            Assert.False(provider.CanSelect);
            Assert.False(provider.IsSelected);
            Assert.Equal("Autre fabricant", provider.Title);
            Assert.Equal("Autre fabricant — " + Unsupported, Assert.Single(f.Vm.ValidationCommand.Split('\n'), line => line.StartsWith("Autre fabricant", StringComparison.Ordinal)));
            Assert.Contains("point de restauration", f.Vm.ValidationDetails, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Aucun redémarrage automatique", f.Vm.ValidationSafety);
            var view = new UpdatesView(f.Vm);
            var window = new Window { Content = view, Width = 1600, Height = 1200, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.DoesNotContain(Descendants<CheckBox>(view), box =>
                    box.DataContext is UpdateSequenceChoiceViewModel choice && choice.Step.Id == "BIOS" && box.IsVisible);
                Assert.DoesNotContain(Descendants<CheckBox>(view), box =>
                    box.DataContext is UpdateSequenceChoiceViewModel choice && choice.Step.Id == "Provider:Autre fabricant" && box.IsVisible);
            }
            finally { window.Close(); }
        }
        finally
        {
            f.Vm.SkipDecisionCommand.Execute(null);
            await pending;
        }
        Assert.Empty(f.Backend.DriverCalls);
        Assert.Empty(f.Backend.InstalledPackages);
        Assert.Equal(0, f.Confirmation.Calls);
    });

    [Theory]
    [InlineData("partial")]
    [InlineData("none")]
    [InlineData("bios")]
    [InlineData("cancelled")]
    public Task Real_service_and_panel_keep_each_driver_status(string scenario) => ThemeContrastTests.OnSta(async () =>
    {
        var f = new Fixture(scenario);
        if (scenario == "cancelled") f.Backend.Cancel = f.Vm.CancelActiveOperation;
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        if (scenario == "bios")
        {
            try { Assert.True(f.Vm.IsValidationOpen); await Capture(f.Vm, "bios-information"); }
            catch { f.Vm.SkipDecisionCommand.Execute(null); await pending; throw; }
        }
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.CanSelect && choice.Step.Component == UpdateComponent.Driver;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.False(f.Vm.IsValidationOpen);
        if (scenario != "bios") await Capture(f.Vm, scenario switch { "partial" => "partial", "none" => "no-drivers", _ => "cancellation" });
        Assert.Equal(1, f.Confirmation.Calls);
        Assert.True(f.Vm.IsIdle);
        Assert.Equal(f.Vm.Operation.FinalSummary, f.History.Report!.SimpleView);
        var source = Assert.Single(f.Vm.Operation.FinalSummary.Split('\n'), line => line.StartsWith("Pilotes :", StringComparison.Ordinal));
        Assert.DoesNotContain("0 ", source);
        var bios = Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Driver:BIOS");
        Assert.Equal(Unsupported, bios.Status);
        Assert.Equal(BiosTitle, bios.Title);
        Assert.Contains("1.2.3", bios.Title + bios.Detail);
        Assert.Contains("Virgil n'installe jamais le BIOS", bios.Detail);
        Assert.DoesNotContain(f.Backend.InstalledPackages, id => id.Contains("Firmware", StringComparison.OrdinalIgnoreCase));
        if (scenario is "none" or "bios")
        {
            var empty = Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Driver:drivers");
            Assert.Equal("à jour", empty.Status);
            Assert.Equal(WuTitle, empty.Title);
            Assert.Equal("Pilotes : à jour", source);
            Assert.DoesNotContain(f.Vm.Operation.Steps, c => c.Key.StartsWith("Driver:WU:", StringComparison.Ordinal));
            Assert.DoesNotContain(f.Vm.Operation.Steps, c => c.Key.StartsWith("Driver:WinGet:", StringComparison.Ordinal));
            Assert.Empty(f.Backend.InstalledPackages);
        }
        else
        {
            var first = Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Driver:WU:first");
            var second = Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Driver:WU:second");
            Assert.Equal(FirstTitle, first.Title);
            Assert.Equal(SecondTitle, second.Title);
            Assert.Equal("mis à jour", first.Status);
            Assert.Equal(scenario == "cancelled" ? "non lancée" : "reste à installer", second.Status);
            var package = Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Driver:WinGet:" + PackageId);
            Assert.Equal(PackageName, package.Title);
            Assert.Equal(scenario == "cancelled" ? "non lancée" : "mis à jour", package.Status);
            Assert.Equal(Unsupported, Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Driver:Provider:Autre fabricant").Status);
            Assert.Equal("Autre fabricant", Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Driver:Provider:Autre fabricant").Title);
            Assert.DoesNotContain(f.Vm.Operation.Steps, c => c.Key == "Driver:drivers");
            Assert.DoesNotContain("Tout est à jour", f.Vm.Operation.FinalSummary);
            Assert.DoesNotContain("Pilotes : à jour", source);
            if (scenario == "partial")
            {
                Assert.Equal(new[] { PackageId }, f.Backend.InstalledPackages);
                Assert.Contains("2 mis à jour", source);
                Assert.Contains("1 reste à installer", source);
                Assert.Contains(Unsupported, source);
                Assert.Equal("Pilotes : reste à installer — 2 mis à jour, 1 reste à installer, 2 non pris en charge automatiquement", source);
            }
            else
            {
                Assert.Empty(f.Backend.InstalledPackages);
                Assert.Equal(new[] { "first" }, f.Backend.DriverCalls);
                Assert.Contains("1 mis à jour", source);
                Assert.Contains("2 non lancées", source);
                Assert.Equal("Pilotes : non lancée — 1 mis à jour, 2 non lancées, 2 non pris en charge automatiquement", source);
            }
        }
        Assert.All(f.Vm.Operation.Steps.Where(c => c.Key.StartsWith("Driver:", StringComparison.Ordinal)), card =>
        {
            Assert.DoesNotContain("WinGet", card.Title + card.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pnputil", card.Title + card.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COM", card.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(PackageId, card.Title + card.Detail, StringComparison.Ordinal);
            if (card.Status is not ("mis à jour" or "à jour"))
            {
                Assert.DoesNotContain("Terminé", card.Detail, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("mis à jour", card.Detail, StringComparison.OrdinalIgnoreCase);
            }
        });
        Assert.DoesNotContain(f.Vm.Operation.Steps, c => c.Title == "Point de restauration et préparation Microsoft Update");
    });

    [Theory]
    [InlineData(1, "1 reste à installer")]
    [InlineData(2, "2 restent à installer")]
    public Task Driver_source_uses_correct_plurals_without_zero_counters(int remaining, string expected) => ThemeContrastTests.OnSta(async () =>
    {
        var f = new Fixture("partial");
        f.Backend.Remaining = remaining;
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.CanSelect && choice.Step.Component == UpdateComponent.Driver;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        var source = Assert.Single(f.Vm.Operation.FinalSummary.Split('\n'), line => line.StartsWith("Pilotes :", StringComparison.Ordinal));
        Assert.Contains(expected, source);
        Assert.DoesNotContain("0 ", source);
        Assert.DoesNotContain("Pilotes : à jour", source);
        Assert.Equal(remaining, f.Vm.Operation.Steps.Count(c => c.Key.StartsWith("Driver:WU:", StringComparison.Ordinal) && c.Status == "reste à installer"));
    });

    [Theory]
    [InlineData("Intel.IntelDriverAndSupportAssistant", false)]
    [InlineData("Intel.IntelDriverAndSupportAssistant", true)]
    [InlineData("AMD.AMDSoftwareCloudEdition", false)]
    public Task Interactive_and_unapproved_packages_are_information_only(string id, bool silent) => ThemeContrastTests.OnSta(async () =>
    {
        var f = new Fixture("partial");
        f.Backend.OfferedId = id; f.Backend.Silent = silent;
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.CanSelect && choice.Step.Component == UpdateComponent.Driver;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Empty(f.Backend.InstalledPackages);
        var card = Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Driver:WinGet:" + id);
        Assert.Equal(Unsupported, card.Status);
        Assert.Contains("n'installe pas les pilotes tout seul", card.Detail);
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static async Task Capture(UpdatesViewModel vm, string name)
    {
        var view = new UpdatesView(vm);
        var host = new Border { Child = view, Background = (Brush)Application.Current.FindResource("App.BackgroundBrush") };
        var window = new Window { Content = host, Width = 1600, Height = 1200, ShowInTaskbar = false, Background = host.Background };
        try
        {
            window.Show(); window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            if (vm.IsValidationOpen)
            {
                var bios = Assert.Single(vm.SequenceChoices, choice => choice.Step.Id == "BIOS");
                AssertInformationRow(host, bios, BiosTitle);
                foreach (var provider in vm.SequenceChoices.Where(choice => choice.Step.Id.StartsWith("Provider:", StringComparison.Ordinal)))
                    AssertInformationRow(host, provider, provider.Step.Id[9..]);
                Assert.All(vm.SequenceChoices.Where(choice => choice.Step.Component == UpdateComponent.Driver && choice.Step.IsInformational), choice =>
                {
                    Assert.False(choice.CanSelect); Assert.False(choice.IsSelected);
                    Assert.DoesNotContain(Descendants<CheckBox>(host), box => ReferenceEquals(box.DataContext, choice) && box.IsVisible);
                });
            }
            var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-d1bis-c");
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(stream);
        }
        finally { window.Close(); }
    }

    private static void AssertInformationRow(FrameworkElement host, UpdateSequenceChoiceViewModel choice, string title)
    {
        Assert.Equal(title, choice.Title);
        Assert.True(choice.Step.IsInformational);
        Assert.False(choice.CanSelect); Assert.False(choice.IsSelected);
        var label = Assert.Single(Descendants<TextBlock>(host), text => ReferenceEquals(text.DataContext, choice) && text.Text == title);
        var status = Assert.Single(Descendants<TextBlock>(host), text => ReferenceEquals(text.DataContext, choice) && text.Text == Unsupported);
        foreach (var text in new[] { label, status })
        {
            Assert.True(text.IsVisible);
            Assert.True(text.ActualWidth > 0 && text.ActualHeight > 0);
            var bounds = new Rect(host.RenderSize); bounds.Inflate(0.5, 0.5);
            Assert.True(bounds.Contains(text.TransformToAncestor(host).TransformBounds(new Rect(text.RenderSize))), "Ligne hors de la surface capturée.");
            for (var parent = VisualTreeHelper.GetParent(text); parent is not null && parent != host; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollContentPresenter viewport)
                {
                    var visible = new Rect(viewport.RenderSize); visible.Inflate(0.5, 0.5);
                    Assert.True(visible.Contains(text.TransformToAncestor(viewport).TransformBounds(new Rect(text.RenderSize))), "Ligne hors de la zone visible de confirmation.");
                }
        }
    }

    private sealed class Fixture
    {
        public RecordingConfirmation Confirmation { get; } = new();
        public Backend Backend { get; }
        public UpdatesSequenceViewModelTests.History History { get; } = new();
        public UpdatesViewModel Vm { get; }
        public Fixture(string scenario)
        {
            Backend = new Backend(scenario);
            var runner = new UpdatesSequenceViewModelTests.Runner();
            var service = new UpdateSequenceService(Backend, Confirmation.Authority, new EmptyPendingRebootProbe());
            Vm = new UpdatesViewModel(new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(runner), runner, Confirmation.Authority),
                new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), runner, History, Confirmation, service);
        }
    }
    private sealed class Scan : IUpdateScanService
    {
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new WingetAvailability { IsAvailable = true } });
    }
    private sealed class Backend(string scenario) : IUpdateSequenceBackend, IDriverSequenceBackend, IDriverInventoryReader, IBiosInformationReader, IDriverPackageSource
    {
        public IDriverInventoryReader DriverInventory => this;
        public IBiosInformationReader Bios => this;
        public IDriverPackageSource DriverPackages => this;
        public string OfferedId { get; set; } = PackageId;
        public bool Silent { get; set; } = true;
        public int Remaining { get; set; } = 1;
        public Action? Cancel { get; set; }
        public List<string> InstalledPackages { get; } = [];
        public List<string> DriverCalls { get; } = [];
        private bool Empty => scenario is "none" or "bios";
        Task<DriverInventoryReport> IDriverInventoryReader.ReadAsync(CancellationToken token) => Task.FromResult(new DriverInventoryReport {
            WasAnalyzed = true, Drivers = Empty ? [] : [new() { Provider = "Autre fabricant", PublishedName = "oem1.inf" }] });
        Task<BiosInformation> IBiosInformationReader.ReadAsync(CancellationToken token) => Task.FromResult(new BiosInformation("OEM", "1.2.3"));
        public Task<IReadOnlyList<DriverPackageOffer>> ScanAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<DriverPackageOffer>>(Empty ? [] :
            [new(new UpdateItem { Id = OfferedId, Name = OfferedId == PackageId ? PackageName : "Intel Driver & Support Assistant", AvailableVersion = "23.Q3", Source = UpdateSource.Winget }, Silent,
                "l'outil du fabricant n'installe pas les pilotes tout seul")]);
        public Task<UpdateSequenceStepResult> InstallAsync(UpdateSequenceStep step, CancellationToken token)
        {
            Assert.False(token.IsCancellationRequested);
            Assert.Equal(PackageId, step.Application!.Id);
            InstalledPackages.Add(step.Application.Id);
            return Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Paquet officiel installé.") { VerifiedStatus = "mis à jour" });
        }
        public Task<UpdateSourceVerification> VerifyAsync(UpdateComponent component, CancellationToken token) =>
            Task.FromResult(new UpdateSourceVerification(component, "à jour", "Contrôle global vide ; les résultats individuels restent prioritaires."));
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) =>
            Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Étape exécutée."));
        public Task<IReadOnlyList<UpdateSequenceStepResult>> ExecuteGroupAsync(IReadOnlyList<UpdateSequenceStep> steps, CancellationToken token)
        {
            if (steps.All(s => s.Component is UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate))
                return Task.FromResult<IReadOnlyList<UpdateSequenceStepResult>>([new(new("restore", "Point de restauration", UpdateComponent.Restore), true, false, "Point vérifié.") { RestorePointVerified = true }]);
            var results = new List<UpdateSequenceStepResult>();
            foreach (var step in steps)
            {
                if (step.Id != "drivers") { results.Add(new(step, true, false, "Étape exécutée.")); continue; }
                if (Empty) { results.Add(new(step, true, false, "Aucun pilote proposé.") { VerifiedStatus = "à jour" }); continue; }
                DriverCalls.Add("first");
                results.Add(new(new("WU:first", FirstTitle, UpdateComponent.Driver), true, false, "Pilote installé.") { VerifiedStatus = "mis à jour" });
                Cancel?.Invoke();
                if (Cancel is null) DriverCalls.Add("second");
                results.Add(new(new("WU:second", SecondTitle, UpdateComponent.Driver), Cancel is null, false,
                    Cancel is null ? "Ce pilote reste proposé." : "Non lancée : annulation demandée.")
                    { VerifiedStatus = Cancel is null ? "reste à installer" : "non lancée", WasSkipped = Cancel is not null });
                if (Remaining == 2 && Cancel is null)
                    results.Add(new(new("WU:third", "Intel - System - 10.1.1.39", UpdateComponent.Driver), true, false,
                        "Ce pilote reste proposé.") { VerifiedStatus = "reste à installer" });
            }
            return Task.FromResult<IReadOnlyList<UpdateSequenceStepResult>>(results);
        }
    }
}
