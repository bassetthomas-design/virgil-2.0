using System.Collections;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotNettoyageInfosTests
{
    private const string EmptySelection = "Sélection : 0 zone(s) · 0 fichiers · 0 o";
    private const string DefaultSelection = "Sélection : 1 zone(s) · 2 fichiers · 2 Ko";
    private const string FullSelection =
        "Sélection : 3 zone(s) · 7 fichiers · 5 Ko · Corbeille incluse (suppression définitive)";

    [Fact]
    public Task Rendered_selection_summary_follows_every_check() => ThemeContrastTests.OnSta(async () =>
    {
        var fixture = CreateFixture();
        var view = new CleanupView(fixture.Vm);
        var window = Show(view);
        try
        {
            await Settle(window);
            var beforeAnalysis = VisibleTexts(view);

            await fixture.Analyze().WaitAsync(TimeSpan.FromSeconds(10));
            await Settle(window);
            Assert.Contains(DefaultSelection, VisibleTexts(view));
            Assert.Contains(EmptySelection, beforeAnalysis);

            fixture.Vm.SelectAllCommand.Execute(null);
            await Settle(window);
            Assert.Contains(FullSelection, VisibleTexts(view));

            var category = Assert.Single(Descendants<Expander>(view), e => e.DataContext is CleanupCategoryItem item &&
                item.Rows.Any(row => row.Preview.Definition.Id == CleanupZoneId.RecycleBin));
            category.IsExpanded = true;
            await Settle(window);
            var recycle = Assert.Single(Descendants<CheckBox>(category), c => c.DataContext is CleanupSelectionRow row &&
                row.Preview.Definition.Id == CleanupZoneId.RecycleBin);
            recycle.IsChecked = false;
            await Settle(window);
            Assert.Contains("Sélection : 2 zone(s) · 4 fichiers · 4 Ko", VisibleTexts(view));
            recycle.IsChecked = true;
            await Settle(window);
            Assert.Contains(FullSelection, VisibleTexts(view));

            fixture.Vm.DeselectAllCommand.Execute(null);
            await Settle(window);
            Assert.Contains(EmptySelection, VisibleTexts(view));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Rendered_additional_information_is_split_into_collapsed_sections_with_totals() =>
        ThemeContrastTests.OnSta(async () =>
    {
        var fixture = CreateFixture();
        await fixture.Analyze().WaitAsync(TimeSpan.FromSeconds(10));
        var view = new CleanupView(fixture.Vm);
        var window = Show(view);
        try
        {
            await Settle(window);
            var additional = Assert.Single(Descendants<Expander>(view),
                e => Equals(e.Header, "Informations complémentaires (lecture seule)"));
            additional.IsExpanded = true;
            await Settle(window);

            var sections = Descendants<Expander>(additional).ToArray();
            var headers = sections.Select(e => e.Header as string ?? string.Empty).ToArray();
            WriteSections(headers, sections.Select(e => e.Content is ListBox list ? list.Items.Count : 0));

            Assert.Equal(new[]
            {
                "Nettoyage sûr — 1 zone(s) · 2 Ko sélectionnables",
                "Nettoyage avancé — 3 zone(s) · 3 Ko sélectionnables",
                "Nettoyage technique bloqué",
                "À revoir manuellement — 0 élément(s)",
                "Protégé"
            }, headers);
            Assert.All(sections, section => Assert.False(section.IsExpanded));

            var property = typeof(CleanupViewModel).GetProperty("AdditionalSections");
            Assert.NotNull(property);
            var models = Assert.IsAssignableFrom<IEnumerable>(property.GetValue(fixture.Vm)).Cast<object>().ToArray();
            var counts = models.Select(section =>
            {
                var items = section.GetType().GetProperty("Items");
                Assert.NotNull(items);
                return Assert.IsAssignableFrom<IEnumerable>(items.GetValue(section)).Cast<object>().Count();
            }).ToArray();
            Assert.Equal(new[] { 1, 3, 1, 1, 1 }, counts);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public async Task Entries_are_unchanged_by_sections()
    {
        var fixture = CreateFixture();
        await fixture.Analyze().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[]
        {
            "CleanupSectionTitleItem",
            "CleanupZoneItemViewModel",
            "CleanupSectionTitleItem",
            "CleanupZoneItemViewModel",
            "CleanupZoneItemViewModel",
            "CleanupZoneItemViewModel",
            "CleanupTextCardItem",
            "CleanupTextCardItem",
            "CleanupTextCardItem"
        }, fixture.Vm.Entries.Select(entry => entry.GetType().Name).ToArray());
    }

    private static CleanupViewModelTests.Fixture CreateFixture()
    {
        var fixture = new CleanupViewModelTests.Fixture();
        var safe = new CleanupZonePreview(
            new CleanupZoneDefinition(CleanupZoneId.UserTemporaryFiles, "Temp", "Description", "Zone test",
                TimeSpan.FromDays(2), CleanupRiskLevel.Low, "Attention", "Effet", "Non touché", 0)
            {
                Classification = CleanupClassification.Cleanable
            }, DateTimeOffset.Now, 3, 2, 2048, 1, [], []);
        var advanced = safe with
        {
            Definition = safe.Definition with
            {
                Id = CleanupZoneId.BrowserChromeCache,
                Classification = CleanupClassification.AdvancedCleanable,
                DisplayOrder = 1
            }
        };
        var recycleDefinition = Assert.Single(CleanupZoneCatalog.CreateDefault(), z => z.Id == CleanupZoneId.RecycleBin);
        var recycle = new PersonalFolderPolicy(new EmptyCleanupSource()).Apply(
            new CleanupZonePreview(recycleDefinition, DateTimeOffset.UtcNow, 3, 3, 1024, 0, [], []));
        var information = safe with
        {
            Definition = safe.Definition with
            {
                Id = CleanupZoneId.PrefetchInformation,
                Classification = CleanupClassification.InformationOnly,
                IsExecutable = false,
                DisplayOrder = 2
            },
            EligibleFileCount = 0,
            EligibleBytes = 0
        };
        fixture.Previews.Items = [safe, advanced, recycle, information];
        return fixture;
    }

    private static void WriteSections(string[] headers, IEnumerable<int> counts)
    {
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-t3c");
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "sections.txt"),
            headers.Zip(counts, (header, count) => $"{header}|{count}"), new UTF8Encoding(false));
    }

    private static Window Show(FrameworkElement view)
    {
        var window = new Window
        {
            Content = view, Width = 1600, Height = 900, ShowInTaskbar = false,
            Background = Brushes.White, Foreground = Brushes.Black
        };
        window.Show();
        return window;
    }

    private static async Task Settle(Window window)
    {
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static string[] VisibleTexts(DependencyObject root) =>
        Descendants<TextBlock>(root).Where(block => block.IsVisible).Select(block => block.Text).ToArray();

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
