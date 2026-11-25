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
public sealed class LotCorbeilleSelectionTests
{
    private const string CriticalText = "JE COMPRENDS ET JE CONFIRME : cleanup.selection : (aucune cible)";
    private const string Help = "La sélection globale et par catégorie exclut les dossiers personnels, les Téléchargements, Windows.old et les lignes non exécutables. « Tout sélectionner » coche aussi la Corbeille quand elle contient des éléments : son vidage est définitif et demande la confirmation renforcée.";
    private static CleanupZonePreview Cache => CleanupSelectionTests.Preview(CleanupClassification.Cleanable);

    [Fact]
    public async Task Select_all_checks_available_recycle_bin_only_in_preview()
    {
        var f = Fixture(Cache, Recycle(3, 1024, []), Zone(CleanupZoneId.OldDownloads));
        await f.Analyze().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "UserTemporaryFiles" }, Selected(f));
        f.Vm.SelectAllCommand.Execute(null);
        Assert.Equal(new[] { "RecycleBin", "UserTemporaryFiles" }, Selected(f));
        Assert.Empty(f.Actions.Order);
        Assert.Equal(0, f.Confirmation.Calls);
        f.Vm.DeselectAllCommand.Execute(null);
        Assert.Empty(Selected(f));
    }

    [Fact]
    public async Task Category_and_group_toggles_never_check_recycle_bin()
    {
        var f = Fixture(Cache, Recycle(3, 1024, []));
        await f.Analyze().WaitAsync(TimeSpan.FromSeconds(10));
        var recycle = Assert.Single(Rows(f), r => r.Preview.Definition.Id == CleanupZoneId.RecycleBin);
        foreach (var category in f.Vm.Categories)
        {
            category.Selection.IsSelected = true;
            Assert.False(recycle.IsSelected);
            category.Selection.IsSelected = false;
            Assert.False(recycle.IsSelected);
        }
        foreach (var group in f.Vm.Groups)
        {
            group.IsSelected = true;
            Assert.False(recycle.IsSelected);
        }
        f.Vm.SelectAllCommand.Execute(null);
        Assert.True(recycle.IsSelected);
        Assert.Single(f.Vm.Categories, c => c.Rows.Contains(recycle)).Selection.IsSelected = false;
        Assert.True(recycle.IsSelected);
        f.Vm.DeselectAllCommand.Execute(null);
        Assert.False(recycle.IsSelected);
    }

    [Theory]
    [InlineData("annuler")]
    [InlineData("passer")]
    [InlineData("vide")]
    [InlineData("faux")]
    [InlineData("exact")]
    public async Task Select_all_then_refusal_or_cancel_never_executes_recycle_bin(string decision)
    {
        var f = Fixture(Cache, Recycle(3, 1024, []));
        await f.Analyze().WaitAsync(TimeSpan.FromSeconds(10));
        f.Vm.SelectAllCommand.Execute(null);
        var pending = f.Run();
        try
        {
            Assert.True(f.Vm.IsValidationOpen);
            Assert.Equal("Corbeille : suppression irréversible via l'API Windows, confirmation renforcée obligatoire.\nDossier personnel : confirmation Critical obligatoire. Recopiez le texte demandé.", f.Vm.ValidationWarning);
            Assert.True(f.Vm.RequiresCriticalConfirmation);
            Assert.Equal(CriticalText, f.Vm.ExpectedCriticalConfirmation);
            Assert.Empty(f.Actions.Order);
            switch (decision)
            {
                case "annuler": f.Vm.CancelDecisionCommand.Execute(null); break;
                case "passer": f.Vm.SkipDecisionCommand.Execute(null); break;
                default:
                    f.Vm.CriticalConfirmationText = decision == "exact" ? CriticalText : decision == "faux" ? "oui" : "";
                    f.Vm.ExecuteDecisionCommand.Execute(null);
                    break;
            }
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
            var expected = decision switch
            {
                "exact" => new[] { "Execute:RecycleBin", "Execute:UserTemporaryFiles" },
                "passer" => new[] { "Skip:RecycleBin", "Skip:UserTemporaryFiles" },
                "vide" or "faux" => new[] { "Cancel:RecycleBin", "Execute:UserTemporaryFiles" },
                _ => new[] { "Cancel:RecycleBin", "Cancel:UserTemporaryFiles" }
            };
            Assert.Equal(expected, f.Actions.Order);
        }
        finally
        {
            if (f.Vm.IsValidationOpen) f.Vm.CancelDecisionCommand.Execute(null);
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Ineligible_rows_show_their_reason_and_empty_recycle_bin_stays_visible()
    {
        var technical = Zone(CleanupZoneId.TechnicalLogs);
        var internet = Zone(CleanupZoneId.InternetCache);
        var f = Fixture(Cache, Recycle(0, 0, []), Zone(CleanupZoneId.WindowsOld),
            technical with { Definition = technical.Definition with { IsExecutable = false } },
            internet with { Definition = internet.Definition with { Classification = CleanupClassification.ReviewOnly } });
        await f.Analyze().WaitAsync(TimeSpan.FromSeconds(10));
        f.Vm.SelectAllCommand.Execute(null);
        WriteRows(f);
        Assert.Equal(new[] { "InternetCache", "RecycleBin", "TechnicalLogs", "UserTemporaryFiles", "WindowsOld" },
            Rows(f).Select(r => r.Preview.Definition.Id.ToString()).Order(StringComparer.Ordinal).ToArray());
        var property = typeof(CleanupSelectionRow).GetProperty("IneligibleReason");
        Assert.NotNull(property);
        Assert.Equal(new[]
        {
            "InternetCache=Zone en lecture seule : à revoir manuellement.",
            "RecycleBin=Corbeille vide : rien à supprimer.",
            "TechnicalLogs=Zone en information seulement : aucune suppression possible.",
            "UserTemporaryFiles=",
            "WindowsOld=Windows.old : information seulement, suppression non proposée."
        }, Rows(f).Select(r => r.Preview.Definition.Id + "=" + property.GetValue(r)).Order(StringComparer.Ordinal).ToArray());
        f.Previews.Items = [Cache, Recycle(0, 0, ["Estimation de la corbeille indisponible."])];
        await f.Analyze().WaitAsync(TimeSpan.FromSeconds(10));
        f.Vm.SelectAllCommand.Execute(null);
        var recycle = Assert.Single(Rows(f), r => r.Preview.Definition.Id == CleanupZoneId.RecycleBin);
        Assert.Equal("Corbeille : estimation indisponible, vidage impossible depuis Virgil.", property.GetValue(recycle));
        Assert.False(recycle.IsSelected);
    }

    [Fact]
    public Task Rendered_recycle_row_is_checked_by_select_all_and_shows_reason_when_empty() => ThemeContrastTests.OnSta(async () =>
    {
        var f = Fixture(Cache, Recycle(3, 1024, []));
        await f.Analyze().WaitAsync(TimeSpan.FromSeconds(10));
        var view = new CleanupView(f.Vm);
        var window = Show(view);
        try
        {
            await Settle(window);
            Assert.Single(Descendants<Expander>(view), e => e.DataContext is CleanupCategoryItem &&
                e.Header is string header && header.StartsWith("Corbeille", StringComparison.Ordinal)).IsExpanded = true;
            f.Vm.SelectAllCommand.Execute(null);
            await Settle(window);
            var checkbox = Assert.Single(Descendants<CheckBox>(view), c => c.DataContext is CleanupSelectionRow row &&
                row.Preview.Definition.Id == CleanupZoneId.RecycleBin);
            Assert.True(checkbox.IsChecked);
            f.Previews.Items = [Cache, Recycle(0, 0, [])];
            await f.Analyze().WaitAsync(TimeSpan.FromSeconds(10));
            await Settle(window);
            Assert.Single(Descendants<Expander>(view), e => e.DataContext is CleanupCategoryItem &&
                e.Header is string header && header.StartsWith("Corbeille", StringComparison.Ordinal)).IsExpanded = true;
            await Settle(window);
            Assert.Contains(Descendants<TextBlock>(view), b => b.IsVisible && b.Text == "Corbeille vide : rien à supprimer.");
            Assert.Contains(Descendants<TextBlock>(view), b => b.IsVisible && b.Text == Help);
        }
        finally { window.Close(); }
    });

    private static CleanupZonePreview Zone(CleanupZoneId id) => Cache with { Definition = Cache.Definition with { Id = id } };
    private static CleanupZonePreview Recycle(int items, long bytes, string[] errors)
    {
        var def = Assert.Single(CleanupZoneCatalog.CreateDefault(), z => z.Id == CleanupZoneId.RecycleBin);
        return new PersonalFolderPolicy(new EmptyCleanupSource()).Apply(
            new CleanupZonePreview(def, DateTimeOffset.UtcNow, items, items, bytes, 0, [], errors));
    }
    private static CleanupViewModelTests.Fixture Fixture(params CleanupZonePreview[] items)
    {
        var f = new CleanupViewModelTests.Fixture();
        f.Previews.Items = items;
        return f;
    }
    private static CleanupSelectionRow[] Rows(CleanupViewModelTests.Fixture f) => f.Vm.Groups.SelectMany(g => g.Rows).ToArray();
    private static string[] Selected(CleanupViewModelTests.Fixture f) => Rows(f).Where(r => r.IsSelected)
        .Select(r => r.Preview.Definition.Id.ToString()).Order(StringComparer.Ordinal).ToArray();
    private static void WriteRows(CleanupViewModelTests.Fixture f)
    {
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-t3a");
        Directory.CreateDirectory(directory);
        var property = typeof(CleanupSelectionRow).GetProperty("IneligibleReason");
        File.WriteAllLines(Path.Combine(directory, "lignes.txt"), Rows(f).Select(r =>
            $"{r.Preview.Definition.Id}|{r.IsSelected}|{r.CanSelect}|{property?.GetValue(r) ?? ""}"), new UTF8Encoding(false));
    }
    private static Window Show(FrameworkElement view)
    {
        var window = new Window { Content = view, Width = 1600, Height = 900, ShowInTaskbar = false,
            Background = Brushes.White, Foreground = Brushes.Black };
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
