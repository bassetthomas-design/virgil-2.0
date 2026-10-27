using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Virgil.App.Views;
using Xunit;
using Xunit.Abstractions;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class ThemeContrastTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<Dispatcher>> Sta = new(StartSta);

    [Fact]
    public Task Theme_text_brushes_meet_wcag_aa_on_theme_backgrounds() => OnSta(() =>
    {
        var theme = Application.Current.Resources;
        // App.TextDisabledBrush : texte désactivé, exempté par WCAG 1.4.3.
        var texts = new[] { "App.TextPrimaryBrush", "App.TextSecondaryBrush", "App.AccentBrush", "App.AccentLightBrush",
            "App.AccentSoftBrush", "App.AlertBrush", "App.SuccessBrush", "TextPrimaryBrush", "TextSecondaryBrush" };
        var backgrounds = new[] { "App.BackgroundBrush", "App.SecondaryBackgroundBrush", "App.PanelBrush", "App.CardBrush" };
        var failures = new List<string>();
        foreach (var text in texts)
        foreach (var background in backgrounds)
        {
            var fg = ((SolidColorBrush)theme[text]).Color;
            var bg = ((SolidColorBrush)theme[background]).Color;
            var ratio = ContrastChecker.Ratio(fg, bg);
            if (ratio < 4.5) failures.Add($"{text} {fg} / {background} {bg} : {ratio:F2}:1");
        }
        Assert.Equal(21, ContrastChecker.Ratio(Colors.Black, Colors.White), 8);
        Assert.Equal(1, ContrastChecker.Ratio(Colors.White, Colors.White), 8);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Cleanup_view_text_meets_wcag_aa() => OnSta(async () =>
    {
        var vm = await CleanupViewModelTests.ContrastFixture();
        var view = new CleanupView(vm);
        var window = Show(view);
        try
        {
            var headers = Descendants<Expander>(view).Where(e => e.DataContext is Virgil.App.ViewModels.CleanupCategoryItem).ToArray();
            Assert.Equal(4, headers.Length);
            var restes = Assert.Single(headers, h => h.Header.ToString()!.StartsWith("Restes de logiciels désinstallés", StringComparison.Ordinal));
            Assert.Contains("(20)", restes.Header.ToString());
            restes.IsExpanded = true;
            window.UpdateLayout();
            var details = Descendants<Expander>(restes).First(e => Equals(e.Header, "Détails techniques"));
            details.IsExpanded = true;
            window.UpdateLayout();
            var blocks = Descendants<TextBlock>(view).Where(IsVisibleText).ToArray();
            Assert.Contains(blocks, b => b.Text.StartsWith("Restes de logiciels désinstallés", StringComparison.Ordinal));
            Assert.Contains(blocks, b => b.Text == "Détails techniques");
            Assert.Contains(blocks, b => b.Text.Contains("Sélectionner la catégorie", StringComparison.Ordinal));
            Assert.Contains(blocks, b => b.Text.Contains("Ancien outil", StringComparison.Ordinal));
            var failures = blocks.Select(Measure).Where(m => m.Ratio < 4.5).ToArray();
            Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures.Select(m => m.Message)));
            var header = Measure(Assert.Single(blocks, b => b.Text.StartsWith("Restes de logiciels désinstallés", StringComparison.Ordinal)));
            output.WriteLine($"En-tête catégorie après : {header.Ratio.ToString("F2", CultureInfo.GetCultureInfo("fr-FR"))}:1 ({header.Message})");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Checker_flags_the_alpha2_cleanup_header_pattern() => OnSta(() =>
    {
        // Ancien gabarit : ListBox natif, Expander avec style local sans BasedOn.
        var fixture = (ListBox)XamlReader.Parse("""
            <ListBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     Background="Transparent" BorderThickness="0" HorizontalContentAlignment="Stretch">
              <Expander Header="Restes de logiciels désinstallés — 2,1 Go (20)">
                <Expander.Style><Style TargetType="Expander"><Setter Property="IsExpanded" Value="False"/></Style></Expander.Style>
              </Expander>
            </ListBox>
            """);
        var window = Show(fixture);
        try
        {
            var header = Assert.Single(Descendants<TextBlock>(fixture), b => IsVisibleText(b) && b.Text.StartsWith("Restes", StringComparison.Ordinal));
            var before = Measure(header);
            Assert.True(before.Ratio < 4.5, before.Message);
            var expander = Assert.Single(Descendants<Expander>(fixture));
            expander.Style = new Style(typeof(Expander), (Style)Application.Current.FindResource(typeof(Expander)));
            window.UpdateLayout();
            var after = Measure(header);
            Assert.True(after.Ratio >= 4.5, after.Message);
            output.WriteLine($"En-tête catégorie : {before.Ratio.ToString("F2", CultureInfo.GetCultureInfo("fr-FR"))}:1 avant → {after.Ratio.ToString("F2", CultureInfo.GetCultureInfo("fr-FR"))}:1 après");
            output.WriteLine($"Avant : {before.Message}; après : {after.Message}");
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static Window Show(FrameworkElement content)
    {
        var window = new Window { Content = content, Width = 1280, Height = 720,
            Background = (Brush)Application.Current.FindResource("App.BackgroundBrush"), ShowInTaskbar = false };
        window.Show();
        content.Measure(new Size(1280, 720));
        content.Arrange(new Rect(0, 0, 1280, 720));
        window.UpdateLayout();
        return window;
    }

    private static bool IsVisibleText(TextBlock b) => b.IsVisible && b.ActualWidth > 0 && b.ActualHeight > 0 && !string.IsNullOrWhiteSpace(b.Text);

    private static (double Ratio, string Message) Measure(TextBlock block)
    {
        Assert.IsType<SolidColorBrush>(block.Foreground);
        var foreground = ((SolidColorBrush)block.Foreground).Color;
        var background = ((SolidColorBrush)Application.Current.FindResource("App.BackgroundBrush")).Color;
        for (DependencyObject? node = block; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            Brush? brush = node switch { Border b => b.Background, Panel p => p.Background, Control c => c.Background, TextBlock t => t.Background, _ => null };
            if (brush is SolidColorBrush solid && solid.Color.A == 255 && solid.Opacity == 1)
            { background = solid.Color; break; }
        }
        // Composition sRGB du foreground effectif, y compris son canal alpha et l'opacité héritée.
        var opacity = ((Brush)block.Foreground).Opacity * foreground.A / 255d;
        for (DependencyObject? node = block; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement element) opacity *= element.Opacity;
        byte Blend(byte fg, byte bg) => (byte)Math.Round(fg * opacity + bg * (1 - opacity));
        foreground = Color.FromRgb(Blend(foreground.R, background.R), Blend(foreground.G, background.G), Blend(foreground.B, background.B));
        var ratio = ContrastChecker.Ratio(foreground, background);
        return (ratio, $"« {block.Text} » : {foreground} / {background} = {ratio:F2}:1");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static Task<Dispatcher> StartSta()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("/Virgil.App;component/Themes/TacticalHudTheme.xaml", UriKind.Relative) });
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "Contraste WPF STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    private static async Task OnSta(Func<Task> action)
    {
        var dispatcher = await Sta.Value;
        await await dispatcher.InvokeAsync(action);
    }
}
