using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Virgil.App.Animation;
using Virgil.App.Controls;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotHWpfTests
{
    [Fact]
    public Task View_name_survives_state_changes_while_status_and_unnamed_face_follow_profile() => ThemeContrastTests.OnSta(() =>
    {
        var xml = XDocument.Load(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src/Virgil.App/Views/UpdatesView.xaml"));
        var name = (string?)xml.Descendants().First(e => e.Name.LocalName == "VirgilCoreControl").Attribute("AutomationProperties.Name");
        Assert.Equal("Mini noyau Virgil mises à jour", name);
        var face = new VirgilCoreControl { AnimationDetailLevel = VirgilCoreAnimationDetailLevel.Compact };
        AutomationProperties.SetName(face, name);
        face.SetState(VirgilCoreState.Warning);
        Assert.Equal("Mini noyau Virgil mises à jour", AutomationProperties.GetName(face));
        Assert.Equal("Virgil : attention", AutomationProperties.GetItemStatus(face));
        face.SetState(VirgilCoreState.Error);
        Assert.Equal("Mini noyau Virgil mises à jour", AutomationProperties.GetName(face));
        Assert.Equal("Virgil : erreur", AutomationProperties.GetItemStatus(face));
        var unnamed = new VirgilCoreControl { AnimationDetailLevel = VirgilCoreAnimationDetailLevel.Compact };
        unnamed.SetState(VirgilCoreState.Error); Assert.Equal("Virgil : erreur", AutomationProperties.GetName(unnamed));
        unnamed.SetState(VirgilCoreState.Success); Assert.Equal("Virgil : réussi", AutomationProperties.GetName(unnamed));
        return Task.CompletedTask;
    });
    [Fact]
    public Task Loaded_visible_error_and_warning_reject_glance() => ThemeContrastTests.OnSta(async () =>
    {
        var face = new VirgilCoreControl { Width = 256, Height = 256, AnimationDetailLevel = VirgilCoreAnimationDetailLevel.Full };
        var window = new Window { Content = face, Width = 300, Height = 300, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(face.IsLoaded); Assert.True(face.IsVisible);
            var show = typeof(VirgilCoreControl).GetMethod("ShowSarcasticGlance", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool)], null);
            Assert.NotNull(show); var eyes = (FrameworkElement)face.FindName("Eyes");
            face.SetState(VirgilCoreState.Error); show.Invoke(face, [true]); Assert.Equal("Error", eyes.Tag);
            face.SetState(VirgilCoreState.Warning); show.Invoke(face, [true]); Assert.Equal("Attention", eyes.Tag);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task Needs_review_real_panel_is_orange_with_theme_and_captured() => ThemeContrastTests.OnSta(() =>
    {
        var operation = new LongOperationViewModel(() => { }); operation.Start("Traitement", immediately: true);
        operation.Complete("À vérifier.", finalState: OperationFinalState.NeedsReview);
        var panel = new OperationProgressPanel { DataContext = operation };
        Capture(panel, "barre-a-verifier", 900, 360);
        Assert.Equal("#FFFF8C1A", Assert.IsType<SolidColorBrush>(Assert.Single(Descendants(panel).OfType<ProgressBar>()).Foreground).Color.ToString());
        return Task.CompletedTask;
    });
    internal static async Task CaptureApplication(UpdatesViewModel vm)
    {
        var view = new UpdatesView(vm) { Width = 1600, Height = 2400 };
        using var presentation = new HwndSource(new HwndSourceParameters("Preuve WPF H")
        {
            Width = 1600, Height = 2400, PositionX = -20000, PositionY = -20000,
            WindowStyle = unchecked((int)0x90000000)
        });
        try
        {
            presentation.RootVisual = view;
            view.Measure(new Size(1600, 2400)); view.Arrange(new Rect(0, 0, 1600, 2400)); view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            view.UpdateLayout();
            var lines = Descendants(view).OfType<TextBlock>().Where(t => t.IsVisible && t.DataContext is string value
                && Assert.Single(vm.Entries.OfType<UpdateItemViewModel>()).Lines.Contains(value)).ToArray();
            Assert.Equal(5, lines.Length);
            Assert.Equal("Source : Applications", Assert.Single(lines, t => t.Text.StartsWith("Source :", StringComparison.Ordinal)).Text);
            foreach (var text in lines)
            {
                var bounds = new Rect(view.RenderSize); bounds.Inflate(0.5, 0.5);
                Assert.True(bounds.Contains(text.TransformToAncestor(view).TransformBounds(new Rect(text.RenderSize))), "Texte H hors cadre.");
                for (var parent = VisualTreeHelper.GetParent(text); parent is not null && parent != view; parent = VisualTreeHelper.GetParent(parent))
                    if (parent is ScrollContentPresenter viewport)
                    {
                        var visible = new Rect(viewport.RenderSize); visible.Inflate(0.5, 0.5);
                        Assert.True(visible.Contains(text.TransformToAncestor(viewport).TransformBounds(new Rect(text.RenderSize))), "Texte H hors de la zone visible.");
                    }
            }
            Save(view, "carte-application", 1600, 2400);
        }
        finally { presentation.RootVisual = null; }
    }
    private static void Capture(FrameworkElement element, string name, int width, int height)
    {
        var border = new Border { Width = width, Height = height, Child = element,
            Background = (Brush)Application.Current.FindResource("App.BackgroundBrush") };
        try
        {
            border.Measure(new Size(width, height)); border.Arrange(new Rect(0, 0, width, height)); border.UpdateLayout();
            Save(border, name, width, height);
        }
        finally { border.Child = null; }
    }
    private static void Save(FrameworkElement element, string name, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-h"); Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var node in Descendants(child)) yield return node;
        }
    }
}
