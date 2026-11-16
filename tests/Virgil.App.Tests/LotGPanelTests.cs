using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using static Virgil.Tests.LotGLinkTests;
using static Virgil.Tests.LotGViewModelTests;

namespace Virgil.Tests;

public sealed class LotGPanelTests
{
    [Fact, Trait("Category", "Windows")]
    public Task Real_control_with_theme_has_exact_labels_copyable_failure_and_no_startup_effect() => ThemeContrastTests.OnSta(async () =>
    {
        var runner = new Runner { Result = () => Task.FromResult(new Virgil.Core.Processes.ProcessLaunchResult(false, null)) };
        var vm = Create(runner);
        var type = typeof(Virgil.App.ViewModels.LongOperationViewModel).Assembly.GetType("Virgil.App.Controls.BugReportPanel"); Assert.NotNull(type);
        var panel = Assert.IsAssignableFrom<UserControl>(Activator.CreateInstance(type, vm));
        Screen(vm, () => "Mises à jour"); Assert.Empty(runner.Launches);
        var border = new Border { Width = 1000, Height = 420, Background = (Brush)Application.Current.FindResource("App.BackgroundBrush"), Child = panel };
        void Layout() { border.Measure(new Size(1000, 420)); border.Arrange(new Rect(0, 0, 1000, 420)); border.UpdateLayout(); }
        Layout();
        var button = Assert.Single(Descendants<Button>(panel));
        Assert.Equal("Signaler un problème", button.Content); Assert.Equal("Signaler un problème", AutomationProperties.GetName(button));
        Assert.Equal("BugReportButton", AutomationProperties.GetAutomationId(button)); Assert.Equal(Application.Current.FindResource("VirgilSecondaryButton"), button.Style);
        var help = Assert.Single(Descendants<TextBlock>(panel), b => b.Text == "Ouvre une page GitHub préremplie dans votre navigateur. Rien n'est envoyé sans vous.");
        Assert.Equal("Ouvre une page GitHub préremplie dans votre navigateur. Rien n'est envoyé sans vous.", help.Text);
        Assert.Equal(Application.Current.FindResource("VirgilHudSecondaryText"), help.Style);
        var path = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts/ui-smoke/lot-g"); Directory.CreateDirectory(path);
        Capture(border, Path.Combine(path, "bouton-repos.png"));
        await Command(vm).ExecuteAsync(null); Layout(); Assert.Single(runner.Launches);
        var error = Assert.Single(Descendants<TextBlock>(panel), b => b.Text == Failure); Assert.Equal(Failure, error.Text); Assert.Equal(Visibility.Visible, error.Visibility);
        var field = Assert.Single(Descendants<TextBox>(panel)); Assert.True(field.IsReadOnly); Assert.Equal(TextWrapping.Wrap, field.TextWrapping);
        Assert.Equal("Lien du signalement", AutomationProperties.GetName(field)); Assert.Equal("BugReportUrlText", AutomationProperties.GetAutomationId(field));
        Assert.Equal(Build(Context()), field.Text); Assert.Equal(Visibility.Visible, field.Visibility);
        foreach (var control in new Control[] { field }) Assert.True(ContrastChecker.Ratio(((SolidColorBrush)control.Foreground).Color, ((SolidColorBrush)control.Background).Color) >= 4.5);
        Assert.True(ContrastChecker.Ratio(((SolidColorBrush)error.Foreground).Color, ((SolidColorBrush)border.Background).Color) >= 4.5);
        Assert.True(field.ActualWidth > 0); Assert.True(field.ActualHeight >= field.ExtentHeight);
        Assert.True(field.TranslatePoint(new Point(field.ActualWidth, field.ActualHeight), border).Y <= border.Height);
        Capture(border, Path.Combine(path, "echec-lien.png"));
        Assert.False(File.ReadAllBytes(Path.Combine(path, "bouton-repos.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(path, "echec-lien.png"))));
        File.WriteAllText(Path.Combine(path, "exemple-lien.txt"), field.Text + "\n\n" + DecodedBody(field.Text)); border.Child = null;
    });
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T typed) yield return typed;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Capture(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap(1000, 420, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
