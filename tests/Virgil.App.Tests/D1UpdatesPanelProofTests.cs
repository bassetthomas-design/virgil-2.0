using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Virgil.App.Controls;
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
        var fixture = new UpdatesSequenceViewModelTests.Fixture();
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
        var panel = new OperationProgressPanel { DataContext = fixture.Vm.Operation, VerticalAlignment = VerticalAlignment.Top };
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
            using var file = File.Create(Path.Combine(directory, reboot ? "tout-mettre-a-jour-bloque-ou-reboot.png" : "tout-mettre-a-jour-bilan.png"));
            encoder.Save(file);
        }
        finally { window.Close(); }
    });
}
