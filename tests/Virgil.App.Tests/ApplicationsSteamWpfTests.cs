using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Virgil.App.Views;
using Virgil.Contracts.Applications;
using Virgil.Core.Applications;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class ApplicationsSteamWpfTests
{
    [Fact]
    public Task SteamDetailsRenderOfficialMethodAndUriWithoutLaunching() => ThemeContrastTests.OnSta(async () =>
    {
        var fixture = new ApplicationsViewModelTests.Fixture();
        fixture.Inventory.Report = new() { Applications = [new ApplicationRiskClassifier().Classify(
            ApplicationRegistryReader.ConvertEntry(new()
            {
                KeyName = "Steam App 2221490", DisplayName = "Jeu Steam de démonstration",
                Publisher = "Studio de test", UninstallString = "steam://uninstall/2221490"
            }))] };
        await fixture.Analyze();
        fixture.OpenDetails();
        var view = new ApplicationsView(fixture.Vm);
        var window = new Window
        {
            Content = view, Width = 1600, Height = 1000, ShowInTaskbar = false,
            Background = (Brush)Application.Current.FindResource("App.BackgroundBrush")
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var details = Assert.IsType<TextBlock>(view.FindName("DetailsBodyText"));
            var command = Assert.IsType<TextBlock>(view.FindName("DetailsCommandText"));
            Assert.True(details.IsVisible);
            Assert.Contains("Méthode : Steam officiel", details.Text);
            Assert.True(command.IsVisible);
            Assert.Equal("steam://uninstall/2221490", command.Text);
            Assert.True(fixture.Vm.IsLaunchUninstallEnabled);
            Assert.Empty(fixture.Runner.Requests);
            Assert.Empty(fixture.Dialog.Calls);
            var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-b");
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),
                (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, "applications-steam-details.png"));
            encoder.Save(file);
        }
        finally { window.Close(); }
    });
}
