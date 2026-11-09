using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Virgil.App.Controls;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Contracts.Applications;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LongOperationLotCWpfTests
{
    [Fact]
    public Task Updates_final_panel_renders_two_steps_and_pending_reboot() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(wrap: service => new UpdatesSequenceViewModelTests.RepeatingSequence(service));
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(2, f.Vm.Operation.Steps.Count);
        Assert.Equal("Source bloquée", f.Vm.Operation.Steps[1].Detail);
        Assert.True(f.Vm.Operation.IsVisible);
        Assert.Contains("Redémarrage nécessaire : oui", f.Vm.Operation.FinalSummary);
        await Capture(new OperationProgressPanel { DataContext = f.Vm.Operation }, "mises-a-jour-avancement.png");
    });

    [Fact]
    public Task Cleanup_multi_zone_final_panel_preserves_quarantine_and_untouched_summary() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new CleanupViewModelTests.Fixture();
        var first = CleanupSelectionTests.Preview(CleanupClassification.Cleanable);
        f.Previews.Items = [first, first with { Definition = first.Definition with
            { Id = CleanupZoneId.DirectXShaderCache, DisplayName = "Cache des shaders", DisplayOrder = 2 } }];
        await f.Analyze();
        Assert.True(f.Vm.Operation.IsVisible);
        Assert.Equal(2, f.Vm.Operation.Steps.Count);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Actions.Handler = async (zone, _) =>
        {
            if (zone.Definition.Id == first.Definition.Id) { entered.SetResult(); await finish.Task; }
            return new CleanupStepResult(zone.Definition, CleanupStepStatus.Completed, 0, 0, 0, 0, TimeSpan.Zero, [])
                { QuarantinedFiles = 1, QuarantinedBytes = 1024 };
        };
        var pending = f.Run();
        Assert.Equal(2, f.Vm.Operation.Steps.Count);
        Assert.All(f.Vm.Operation.Steps, step => Assert.Equal("Non lancée", step.Status));
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(2, f.Vm.Operation.Steps.Count);
            Assert.Contains("4/10 fichiers", f.Vm.Operation.Steps[0].Detail);
            Assert.Equal("Non lancée", f.Vm.Operation.Steps[1].Status);
        }
        finally { finish.SetResult(); }
        await pending;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(f.Previews.Items.Select(zone => zone.Definition.DisplayName), f.Vm.Operation.Steps.Select(step => step.Title));
        Assert.All(f.Vm.Operation.Steps, step => Assert.Equal("Terminée", step.Status));
        Assert.True(f.Vm.Operation.IsVisible);
        Assert.Contains("2 fichiers en quarantaine", f.Vm.Operation.FinalSummary);
        Assert.Contains("Fichiers hors sélection et racines conservés", f.Vm.Operation.FinalSummary);
        Assert.NotEmpty(f.Vm.Operation.Commentary);
        await Capture(new OperationProgressPanel { DataContext = f.Vm.Operation }, "nettoyage-avancement.png");
    });

    [Fact]
    public Task Application_details_use_simple_french_without_executing() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new ApplicationsViewModelTests.Fixture();
        f.Inventory.Report = new() { Applications = [ApplicationsViewModelTests.Safe() with
            { Publisher = "", Source = ApplicationInventorySource.Winget, Status = ApplicationStatus.UninstallAvailable, Architecture = ApplicationArchitecture.Unknown }] };
        await f.Analyze();
        f.OpenDetails();
        var view = new ApplicationsView(f.Vm);
        var body = Assert.IsType<TextBlock>(view.FindName("DetailsBodyText"));
        await Capture(view, "applications-details-libellés.png", () =>
        {
            Assert.True(body.IsVisible);
            Assert.Contains("Désinstallation possible", body.Text);
            Assert.Contains("Inconnue", body.Text);
            Assert.Contains("non renseigné", body.Text);
            Assert.DoesNotContain("UninstallAvailable", body.Text);
            Assert.DoesNotContain("Unknown", body.Text);
            Assert.DoesNotContain("N/A", body.Text);
        });
        Assert.Empty(f.Runner.Requests);
        Assert.Empty(f.Dialog.Calls);
    });

    private static async Task Capture(FrameworkElement view, string name, Action? assertions = null)
    {
        var host = new Border { Child = view, Background = (Brush)Application.Current.FindResource("App.BackgroundBrush") };
        if (view is OperationProgressPanel) view.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Content = host, Width = 1600, Height = 1000, ShowInTaskbar = false,
            Background = host.Background };
        try
        {
            window.Show();
            window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            assertions?.Invoke();
            Assert.True(view.IsVisible);
            var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-c");
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, name));
            encoder.Save(file);
        }
        finally { window.Close(); }
    }
}
