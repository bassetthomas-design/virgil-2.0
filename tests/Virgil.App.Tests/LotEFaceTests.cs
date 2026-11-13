using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Virgil.App.Animation;
using Virgil.App.Controls;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotEFaceTests
{
    [Theory]
    [InlineData(VirgilCoreState.Idle, "Rest", "#FF3FE0C5", "Virgil au repos")]
    [InlineData(VirgilCoreState.Scanning, "Work", "#FF4AA8FF", "Virgil au travail")]
    [InlineData(VirgilCoreState.Executing, "Work", "#FF4AA8FF", "Virgil au travail")]
    [InlineData(VirgilCoreState.Warning, "Attention", "#FFFFC23D", "Virgil : attention")]
    [InlineData(VirgilCoreState.SensitiveAction, "Attention", "#FFFFC23D", "Virgil : attention")]
    [InlineData(VirgilCoreState.Error, "Error", "#FFFF4D4D", "Virgil : erreur")]
    [InlineData(VirgilCoreState.Success, "Success", "#FF5EE07A", "Virgil : réussi")]
    [InlineData(VirgilCoreState.Communicating, "Rest", "#FF3FE0C5", "Virgil au repos")]
    public Task Each_state_has_its_vector_eyes_and_exact_accessible_name(VirgilCoreState state, string shape, string color, string name) =>
        ThemeContrastTests.OnSta(() =>
        {
            var face = new VirgilCoreControl { Width = 256, Height = 256, AnimationDetailLevel = VirgilCoreAnimationDetailLevel.Compact };
            face.SetState(state);
            Assert.Equal(name, AutomationProperties.GetName(face));
            var eyes = Assert.IsAssignableFrom<System.Windows.Shapes.Shape>(face.FindName("Eyes"));
            Assert.Equal(shape, eyes.Tag);
            var brush = shape == "Success" ? eyes.Stroke : eyes.Fill;
            Assert.Equal(color, Assert.IsType<SolidColorBrush>(brush).Color.ToString());
            Assert.IsAssignableFrom<Geometry>(eyes is System.Windows.Shapes.Path path ? path.Data : null);
            Assert.True(ContrastChecker.Ratio(((SolidColorBrush)brush).Color, Color.FromRgb(43, 52, 64)) >= 3);
            Capture(face, "visage-" + state);
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public Task Disabled_Windows_animations_or_compact_stops_every_eye_animation(bool enabled, bool compact) => ThemeContrastTests.OnSta(() =>
    {
        var face = new VirgilCoreControl { Width = 256, Height = 256,
            AnimationDetailLevel = compact ? VirgilCoreAnimationDetailLevel.Compact : VirgilCoreAnimationDetailLevel.Full };
        // The same render entry point is called by the Windows preference listener.
        var render = typeof(VirgilCoreControl).GetMethod("RefreshFace", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(render);
        foreach (var state in Enum.GetValues<VirgilCoreState>())
        {
            face.SetState(state);
            render.Invoke(face, new object[] { enabled });
            face.NotifyCommunication();
            render.Invoke(face, new object[] { enabled });
            var eyes = Assert.IsAssignableFrom<FrameworkElement>(face.FindName("Eyes"));
            Assert.False(eyes.HasAnimatedProperties);
            Assert.False(eyes.RenderTransform.HasAnimatedProperties);
            Assert.Equal(1d, eyes.Opacity);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void Face_does_not_consume_rotation_profiles_or_rotating_elements()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/Virgil.App/Controls/VirgilCoreControl.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/Virgil.App/Controls/VirgilCoreControl.xaml.cs"));
        Assert.DoesNotContain("RotateTransform", xaml);
        Assert.DoesNotContain("VirgilMotionProfiles", code);
        Assert.DoesNotContain("RotateTransform", code);
        Assert.DoesNotContain("<Image", xaml);
    }

    internal static void Capture(FrameworkElement element, string name, int width = 256, int height = 256)
    {
        var border = new Border { Width = width, Height = height, Child = element,
            Background = (Brush)Application.Current.FindResource("App.BackgroundBrush") };
        border.Measure(new Size(width, height)); border.Arrange(new Rect(0, 0, width, height)); border.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(border);
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-e");
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
        border.Child = null;
    }
}
