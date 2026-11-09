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
public sealed class D1UpdatesPanelProofTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Real_panel_renders_final_verdict_with_fake_sources(bool reboot) => ThemeContrastTests.OnSta(async () =>
    {
        var fixture = new UpdatesSequenceViewModelTests.Fixture(scan: new Scan());
        fixture.Backend.Reboot = reboot;
        var pending = fixture.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in fixture.Vm.SequenceChoices)
            choice.IsSelected = choice.Step.Component is UpdateComponent.WingetFinal or UpdateComponent.Store;
        fixture.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Contains("Store", fixture.Vm.Operation.FinalSummary);
        Assert.DoesNotContain("tout est à jour", fixture.Vm.Operation.FinalSummary, StringComparison.OrdinalIgnoreCase);
        Assert.True(fixture.Vm.Operation.IsVisible);
        Assert.Equal(100, fixture.Vm.Operation.Percent);
        Assert.True(fixture.Vm.Operation.HasDistinctCommentary);
        if (!reboot) Assert.Contains("à terminer dans le Microsoft Store", fixture.Vm.StatusText);
        Assert.Equal("à vérifier", Assert.Single(fixture.Vm.Operation.Steps, card => card.Key == "Store:store").Status);
        foreach (var card in fixture.Vm.Operation.Steps.Where(card => card.Status is not "à jour" and not "mis à jour" and not "Terminée"))
            Assert.DoesNotContain("Terminé", card.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("(", Assert.Single(fixture.Vm.Operation.FinalSummary.Split('\n'), line => line.StartsWith("Redémarrage nécessaire", StringComparison.Ordinal)));
        await Capture(fixture.Vm.Operation, reboot ? "tout-mettre-a-jour-bloque-ou-reboot.png" : "tout-mettre-a-jour-bilan.png");
    });

    [Fact]
    public Task Winget_identity_panel_keeps_success_failure_and_not_started_separate() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new D1WingetIdentityTests.IdentityScan(true));
        f.Backend.Handler = (step, _) =>
        {
            if (step.Id == "app-b") f.Vm.CancelActiveOperation();
            return Task.FromResult(new UpdateSequenceStepResult(step, step.Id == "app-a", false,
                step.Id == "app-a" ? "Installation réussie pour app-a." : "Échec d'installation pour app-b."));
        };
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Winget;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal("mis à jour", Assert.Single(f.Vm.Operation.Steps, card => card.Key == "Winget:app-a").Status);
        Assert.Equal("en erreur", Assert.Single(f.Vm.Operation.Steps, card => card.Key == "Winget:app-b").Status);
        Assert.Equal("reste à installer", Assert.Single(f.Vm.Operation.Steps, card => card.Key == "Winget:app-c").Status);
        Assert.NotEqual(100, f.Vm.Operation.Percent);
        foreach (var summary in new[] { f.Vm.Operation.FinalSummary, f.History.Report!.SimpleView })
        {
            var source = Assert.Single(summary.Split('\n'), line => line.StartsWith("WinGet / applications", StringComparison.Ordinal));
            Assert.Contains(": reste à installer — ", source);
            Assert.DoesNotContain("non lancée", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("app-b", source);
        }
        await Capture(f.Vm.Operation, "tout-mettre-a-jour-identites-winget.png");
    });

    [Fact]
    public Task Information_only_panel_never_claims_source_current() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new D1InformationOnlyTests.InformationScan("same-id"));
        f.Backend.Reboot = false;
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Winget;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Contains("reste à vérifier", f.Vm.Operation.FinalSummary);
        Assert.DoesNotContain("Tout est à jour", f.Vm.Operation.FinalSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("reste à vérifier", Assert.Single(f.Vm.Operation.Steps, card => card.Key == "Winget:app-a").Status);
        await Capture(f.Vm.Operation, "tout-mettre-a-jour-information-only.png");
    });

    private static async Task Capture(LongOperationViewModel operation, string name)
    {
        var panel = new OperationProgressPanel { DataContext = operation, VerticalAlignment = VerticalAlignment.Top };
        var host = new Border { Child = panel, Background = (Brush)Application.Current.FindResource("App.BackgroundBrush") };
        var window = new Window { Content = host, Width = 1600, Height = 1000, ShowInTaskbar = false, Background = host.Background };
        try
        {
            window.Show(); window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Assert.True(panel.IsVisible);
            var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-d1");
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, name));
            encoder.Save(file);
        }
        finally { window.Close(); }
    }

    private sealed class Scan : IUpdateScanService
    {
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new WingetAvailability { IsAvailable = true } });
    }
}
