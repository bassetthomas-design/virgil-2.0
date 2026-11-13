using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Xunit;
using static Virgil.Tests.LotFSpeechTests;

namespace Virgil.Tests;

public sealed class LotFRevealTests
{
    [Fact]
    public void Manual_clock_reveals_words_every_40ms_with_at_most_40_steps_and_completes_previous()
    {
        var clock = new ManualClock();
        var model = Activator.CreateInstance(Type("Speech.SpeechReveal"), clock)!;
        var full = string.Join(" ", Enumerable.Range(1, 123).Select(n => "mot" + n));
        Call(model, "Start", full, true);
        Assert.Equal(full, Read(model, "FullText")); Assert.Equal("", Read(model, "Text"));
        clock.Advance(39); Call(model, "Tick"); Assert.Equal("", Read(model, "Text"));
        clock.Advance(1); Call(model, "Tick"); var part = (string)Read(model, "Text");
        Assert.StartsWith("mot1 mot2 mot3", part); Assert.NotEqual(full, part);
        clock.Advance(19 * 40); Call(model, "Tick");
        Assert.NotEqual(full, Read(model, "Text"));
        clock.Advance(20 * 40); Call(model, "Tick"); Assert.Equal(full, Read(model, "Text"));
        Call(model, "Start", "Un autre texte", true); clock.Advance(40); Call(model, "Tick");
        Call(model, "Start", "Nouveau texte", true);
        Assert.Equal("Un autre texte", Read(model, "PreviousText"));
        Call(model, "Complete"); Assert.Equal("Nouveau texte", Read(model, "Text"));
        Call(model, "Start", "Sans animation", false); Assert.Equal("Sans animation", Read(model, "Text"));
    }
    [Fact, Trait("Category", "Windows")]
    public Task Ordinary_TextBlock_has_full_accessible_name_immediately_and_click_finishes() => ThemeContrastTests.OnSta(() =>
    {
        var type = Type("Speech.SpeechText");
        var setter = type.GetMethod("SetText"); Assert.NotNull(setter);
        var clockSetter = type.GetMethod("SetClock"); Assert.NotNull(clockSetter);
        var clock = new ManualClock(); var block = new TextBlock();
        clockSetter.Invoke(null, [block, clock]);
        var full = "[VIRGIL]\nUn texte affiché mot par mot sans bloquer.";
        setter.Invoke(null, [block, full]);
        Assert.Equal(full, AutomationProperties.GetName(block));
        clock.Advance(200);
        var reveal = type.GetMethod("GetReveal"); Assert.NotNull(reveal);
        var model = reveal.Invoke(null, [block]); Assert.NotNull(model);
        Call(model, "Tick");
        Capture(block, "texte-partiel", 760, 150);
        block.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        Assert.Equal(full, block.Text); Capture(block, "texte-apres-clic", 760, 150);
        return Task.CompletedTask;
    });
    [Fact]
    public void Chat_preserves_the_old_message_and_adds_welcome_after_it()
    {
        var text = File.ReadAllText(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src/Virgil.App/MainWindow.xaml.cs"));
        var old = text.IndexOf("AppendVirgilMessage(\"Système prêt.\\nAucune analyse récente.\\nEn attente d'instruction.\");", StringComparison.Ordinal);
        Assert.True(old > 0);
        Assert.True(text.IndexOf("Pick(\"Welcome\")", old, StringComparison.Ordinal) > old);
        Assert.Contains("SpeechText.SetText", text); Assert.Contains("\"[VIRGIL]\\n\" + message", text);
        Assert.Contains("ShowSarcasticGlance", text);
    }
    internal sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(int milliseconds) => ticks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }
    internal static void Capture(FrameworkElement element, string name, int width = 760, int height = 480)
    {
        var border = new Border { Width = width, Height = height, Child = element,
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("App.BackgroundBrush") };
        border.Measure(new Size(width, height)); border.Arrange(new Rect(0, 0, width, height)); border.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(border);
        var path = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-f"); Directory.CreateDirectory(path);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(path, name + ".png")); encoder.Save(stream); border.Child = null;
    }
}
