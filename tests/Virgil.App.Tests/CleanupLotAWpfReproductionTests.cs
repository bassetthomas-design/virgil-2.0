using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class CleanupLotAWpfReproductionTests(ITestOutputHelper output)
{
    [Fact]
    public Task Rendered_download_checkbox_stays_unchecked_across_scan_bulk_selection_and_return() => ThemeContrastTests.OnSta(async () =>
    {
        var fixture = Fixture();
        var view = new CleanupView(fixture.Vm);
        var window = Show(view);
        try
        {
            for (var scan = 0; scan < 2; scan++)
            {
                await fixture.Analyze();
                await Settle(window);
                var category = Downloads(view);
                category.IsExpanded = true;
                await Settle(window);
                AssertDownloadUnchecked(view);
                fixture.Vm.SelectAllCommand.Execute(null);
                await Settle(window);
                AssertDownloadUnchecked(view);
                var model = Assert.IsType<CleanupCategoryItem>(category.DataContext);
                model.Selection.IsSelected = true;
                await Settle(window);
                AssertDownloadUnchecked(view);
                model.Selection.IsSelected = false;
                await Settle(window);
                AssertDownloadUnchecked(view);
                fixture.Vm.ReturnHomeCommand.Execute(null);
                window.Content = null;
                window.Content = view;
                await Settle(window);
                AssertDownloadUnchecked(view);
                // Choix explicite simulé pour vérifier que le rescan ne le restaure pas.
                Assert.Single(model.Rows).IsSelected = true;
            }
            output.WriteLine("Téléchargements : case WPF et modèle décochés après scan, sélection globale, catégorie et retour.");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Downloads_category_without_eligible_rows_has_no_enabled_bulk_checkbox() => ThemeContrastTests.OnSta(async () =>
    {
        var fixture = Fixture();
        await fixture.Analyze();
        var view = new CleanupView(fixture.Vm);
        var window = Show(view);
        try
        {
            await Settle(window);
            var category = Downloads(view);
            category.IsExpanded = true;
            await Settle(window);
            var bulk = Descendants<CheckBox>(category).Where(c => c.DataContext is CleanupCategoryItem).ToArray();
            Assert.DoesNotContain(bulk, c => c.IsVisible && c.IsEnabled);
            Assert.Contains(Descendants<TextBlock>(category), b => b.IsVisible &&
                b.Text == "Choix ligne par ligne : fichiers personnels");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Expanded_download_row_text_has_at_least_4_5_contrast() => ThemeContrastTests.OnSta(async () =>
    {
        var fixture = Fixture();
        await fixture.Analyze();
        var view = new CleanupView(fixture.Vm);
        var window = Show(view);
        try
        {
            await Settle(window);
            var category = Downloads(view);
            category.IsExpanded = true;
            await Settle(window);
            var details = Assert.Single(Descendants<Expander>(category), e => Equals(e.Header, "Détails techniques"));
            details.IsExpanded = true;
            await Settle(window);
            var blocks = Descendants<TextBlock>(category).Where(b => b.IsVisible && b.IsEnabled &&
                b.ActualHeight > 0 && !string.IsNullOrWhiteSpace(b.Text)).ToArray();
            Assert.Contains(blocks, b => b.Text == "Téléchargements anciens ou volumineux");
            var failures = new List<string>();
            foreach (var block in blocks)
            {
                var fg = Assert.IsType<SolidColorBrush>(block.Foreground).Color;
                var bg = Assert.IsType<SolidColorBrush>(window.Background).Color;
                for (DependencyObject? node = block; node is not null; node = VisualTreeHelper.GetParent(node))
                {
                    var brush = node switch { Border b => b.Background, Panel p => p.Background,
                        Control c => c.Background, TextBlock t => t.Background, _ => null };
                    if (brush is SolidColorBrush solid && solid.Color.A == 255 && solid.Opacity == 1)
                    { bg = solid.Color; break; }
                }
                var opacity = block.Foreground.Opacity * fg.A / 255d;
                for (DependencyObject? node = block; node is not null; node = VisualTreeHelper.GetParent(node))
                    if (node is UIElement element) opacity *= element.Opacity;
                byte Blend(byte f, byte b) => (byte)Math.Round(f * opacity + b * (1 - opacity));
                fg = Color.FromRgb(Blend(fg.R, bg.R), Blend(fg.G, bg.G), Blend(fg.B, bg.B));
                var ratio = ContrastChecker.Ratio(fg, bg);
                var measurement = $"« {block.Text} » : {fg} / {bg} = {ratio:F2}:1";
                output.WriteLine(measurement);
                if (ratio < 4.5) failures.Add(measurement);
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }
        finally { window.Close(); }
    });

    private static CleanupViewModelTests.Fixture Fixture()
    {
        var fixture = new CleanupViewModelTests.Fixture();
        fixture.Previews.Items = [CleanupLotAReproductionTests.CaptureDownload(),
            CleanupSelectionTests.Preview(CleanupClassification.Cleanable)];
        return fixture;
    }

    private static Expander Downloads(DependencyObject view) => Assert.Single(Descendants<Expander>(view),
        e => e.DataContext is CleanupCategoryItem category && category.Rows.Any(r => r.Preview.Definition.Id == CleanupZoneId.OldDownloads)
            && e.Header is string header && header.StartsWith("Téléchargements", StringComparison.Ordinal));

    private static void AssertDownloadUnchecked(DependencyObject view)
    {
        var checkbox = Assert.Single(Descendants<CheckBox>(view), c => c.DataContext is CleanupSelectionRow row &&
            row.Preview.Definition.Id == CleanupZoneId.OldDownloads);
        Assert.True(checkbox.IsVisible);
        Assert.True(checkbox.IsEnabled);
        Assert.False(checkbox.IsChecked);
        Assert.False(Assert.IsType<CleanupSelectionRow>(checkbox.DataContext).IsSelected);
    }

    private static Window Show(FrameworkElement view)
    {
        var window = new Window { Content = view, Width = 1600, Height = 900, ShowInTaskbar = false,
            Background = (Brush)Application.Current.FindResource("App.BackgroundBrush"),
            Foreground = (Brush)Application.Current.FindResource("App.TextPrimaryBrush") };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static async Task Settle(Window window)
    {
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
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
}
