using System.Collections;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using Virgil.App.Controls;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Diagnostic;
using Virgil.Core.Processes;
using Virgil.Core.Status;
using Xunit;
using static Virgil.Tests.LotFSpeechTests;

namespace Virgil.Tests;

public sealed class LotPrioritesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 16, 24, 0, TimeSpan.Zero);
    private static readonly DiskScanInfo C = new("C:\\", "Système", 100, 50, 50, 50, ScanSeverity.Healthy, "Normal", true);
    private const string DiskText = "Le disque C: est presque plein.";
    private const string WindowsText = "Windows a signalé des erreurs ces dernières 24 heures.";
    private const string DiskResolved = "Espace du disque C: — retour à la normale lors du scan de 18 h 44.";

    [Fact]
    public void Each_item_shows_source_measure_time_and_recheck_state()
    {
        var store = new SystemStatusStore();
        using var runner = new Runner();
        using var vm = Make(store, runner);
        PublishDiskAndWindows(store);

        Assert.Equal(new[] { DiskText, WindowsText }, vm.Items.Select(i => i.Text));
        Assert.Equal(new[] { "Ouvrir Nettoyage", "Ouvrir Mises à jour" }, vm.Items.Select(i => i.Button));
        Assert.Equal(new[] { "Scan · mesuré à 18 h 24", "Diagnostic · mesuré à 18 h 34" }, Details(vm));
        Assert.Equal("Mesuré à 18:34", vm.MeasuredAt);

        store.PublishScan(Scan(T0.AddMinutes(20), []));
        Assert.Equal(new[] { DiskText, WindowsText }, vm.Items.Select(i => i.Text));
        Assert.Equal(new[] { "Scan · mesuré à 18 h 24 · à revérifier", "Diagnostic · mesuré à 18 h 34" }, Details(vm));
        Assert.Equal("Mesuré à 18:44", vm.MeasuredAt);
        Assert.Empty(Resolved(vm));
    }

    [Fact]
    public void Resolved_points_show_return_to_normal_with_source_and_time()
    {
        var store = new SystemStatusStore();
        using var runner = new Runner();
        using var vm = Make(store, runner);
        PublishDiskAndWindows(store);
        store.PublishScan(Scan(T0.AddMinutes(20), [C]));

        Assert.Equal(new[] { WindowsText }, vm.Items.Select(i => i.Text));
        Assert.Equal(string.Empty, vm.StatusText);
        Assert.Equal(new[] { DiskResolved }, Resolved(vm));

        store.PublishDiagnostic(Diag(T0.AddMinutes(30)));
        Assert.Empty(vm.Items);
        Assert.Equal("Aucun point prioritaire au dernier contrôle.", vm.StatusText);
        Assert.Equal(new[] { DiskResolved, "Erreurs Windows — retour à la normale lors du diagnostic de 18 h 54." }, Resolved(vm));
    }

    [Fact]
    public void Tile_counts_the_displayed_list_and_separates_analysis_to_run_from_no_priority()
    {
        var store1 = new SystemStatusStore();
        using var runner = new Runner();
        using var vm1 = Make(store1, runner);
        PublishDiskAndWindows(store1);

        Assert.Equal(2, store1.LastPriorities!.Value);
        AssertTile(vm1, "2", "2 POINTS À VÉRIFIER · Mesuré à 18:34");
        Assert.Equal(2, vm1.Items.Count);

        var store2 = new SystemStatusStore();
        using var vm2 = Make(store2, runner);
        AssertTile(vm2, "--", "ANALYSE À LANCER");
        store2.PublishScan(Scan(T0, []));
        AssertTile(vm2, "0", "AUCUNE PRIORITÉ DÉTECTÉE · Mesuré à 18:24");
        store2.PublishScan(Scan(T0.AddMinutes(20), [C], F("memory-usage", ScanSeverity.Warning)));
        AssertTile(vm2, "1", "1 POINT À VÉRIFIER · Mesuré à 18:44");

        var destinations = new List<string>();
        vm2.NavigationRequested += destinations.Add;
        Assert.IsAssignableFrom<IRelayCommand>(Read(vm2, "ShowAllCommand")).Execute(null);
        Assert.Equal(new[] { "Priorités" }, destinations);
    }

    [Fact, Trait("Category", "Windows")]
    public Task Rendered_panel_and_tile_show_source_time_return_to_normal_and_open_the_list() =>
        ThemeContrastTests.OnSta(async () =>
        {
            var store = new SystemStatusStore();
            using var runner = new Runner();
            using var vm = Make(store, runner);
            var panel = new HomePrioritiesPanel { DataContext = vm };
            var window = Show(panel);
            try
            {
                PublishDiskAndWindows(store);
                await Settle(window);
                var texts = VisibleTexts(panel);
                WriteEvidence(1, texts);
                Assert.Contains(DiskText, texts);
                Assert.Contains("Scan · mesuré à 18 h 24", texts);
                Assert.Contains("Diagnostic · mesuré à 18 h 34", texts);

                store.PublishScan(Scan(T0.AddMinutes(20), [C]));
                await Settle(window);
                texts = VisibleTexts(panel);
                WriteEvidence(2, texts);
                Assert.Contains(DiskResolved, texts);
                Assert.DoesNotContain(DiskText, texts);

                var tile = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(Type("Controls.HomePriorityTile")));
                tile.DataContext = vm;
                var tileWindow = Show(tile);
                try
                {
                    await Settle(tileWindow);
                    texts = VisibleTexts(tile);
                    WriteEvidence(3, texts);
                    Assert.Equal(new[] { "PRIORITÉS", "1", "1 POINT À VÉRIFIER · Mesuré à 18:44" }, texts);
                    var button = Assert.Single(Descendants<Button>(tile));
                    Assert.Equal("Voir les priorités", AutomationProperties.GetName(button));
                    var destinations = new List<string>();
                    vm.NavigationRequested += destinations.Add;
                    button.Command.Execute(button.CommandParameter);
                    Assert.Equal(new[] { "Priorités" }, destinations);
                }
                finally { tileWindow.Close(); }
            }
            finally { window.Close(); }
        }).WaitAsync(TimeSpan.FromSeconds(10));

    [Fact]
    public void Main_window_hosts_the_tile_and_routes_it_to_the_home_list()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "src", "Virgil.App", "MainWindow.xaml"));
        XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var card = Assert.Single(xaml.Descendants(), e => (string?)e.Attribute(name) == "PriorityMetricCard");
        var children = card.Elements().Where(e => !e.Name.LocalName.Contains('.')).ToArray();
        Assert.Equal(new[] { "HomePriorityTile" }, children.Select(e => e.Name.LocalName));
        Assert.Equal("PriorityTile", (string?)children[0].Attribute(name));
        var names = xaml.Descendants().Select(e => (string?)e.Attribute(name));
        Assert.DoesNotContain("PriorityValueText", names);
        Assert.DoesNotContain("PriorityDetailText", names);

        var code = File.ReadAllText(Path.Combine(root, "src", "Virgil.App", "MainWindow.xaml.cs"));
        Assert.Contains("PriorityTile.DataContext = HomePriorities;", code);
        Assert.Contains("case \"Priorités\":", code);
        Assert.Contains("PrioritiesPanel.BringIntoView()", code);
        Assert.DoesNotContain("UpdatePriorities(", code);
        Assert.DoesNotContain("LastPriorities", code);
    }

    [Fact, Trait("Category", "Windows")]
    public Task Rendered_panel_distinguishes_analysis_to_run_from_no_priority() =>
        ThemeContrastTests.OnSta(async () =>
        {
            var store = new SystemStatusStore();
            using var runner = new Runner();
            using var vm = Make(store, runner);
            var panel = new HomePrioritiesPanel { DataContext = vm };
            var window = Show(panel);
            try
            {
                await Settle(window);
                Assert.Contains("Aucune analyse récente : lancez un scan pour voir les priorités.", VisibleTexts(panel));
                store.PublishScan(Scan(T0, []));
                await Settle(window);
                var texts = VisibleTexts(panel);
                Assert.Contains("Aucun point prioritaire au dernier contrôle.", texts);
                Assert.Contains("Mesuré à 18:24", texts);
            }
            finally { window.Close(); }
        }).WaitAsync(TimeSpan.FromSeconds(10));

    private static HomePrioritiesViewModel Make(SystemStatusStore store, Runner runner) => new(store, runner, new Clock());

    private static void PublishDiskAndWindows(SystemStatusStore store)
    {
        store.PublishScan(Scan(T0, [C], F("disk-C:", ScanSeverity.Warning)));
        store.PublishDiagnostic(Diag(T0.AddMinutes(10), new DiagnosticIssue("windows.errors", "windows.errors", "Description", 2, 10, "test")));
    }

    private static SystemScanReport Scan(DateTimeOffset at, IReadOnlyList<DiskScanInfo> disks, params ScanFinding[] findings) =>
        new(at, ScanMode.Quick, TimeSpan.Zero, "", null!, new("CPU", 8, 10, ScanSeverity.Healthy, "Normal"),
            new(0, 0, 0, 0, ScanSeverity.Information, ""), disks, [], null!, new(false, 0, 0, [], []), findings, [], []);

    private static ScanFinding F(string id, ScanSeverity severity) => new(id, "", id, "", "", severity, null);
    private static DiagnosticValue<T> V<T>(T value) => DiagnosticValue<T>.Available(value);

    private static DiagnosticFindings Diag(DateTimeOffset at, params DiagnosticIssue[] issues) =>
        new(new DiagnosticSnapshot(at, new(V(10d), [], V(3000d), V(3000d), V(40d)), new(V(16d), V(8d), V(1d), V(1d), V(8d), V(8d)), [], [], [], []),
            new(V(new WindowsErrorSummary(0, [])), V<IReadOnlyList<DeviceProblem>>([]), V<IReadOnlyList<StartupEntry>>([]),
                V<IReadOnlyList<ProcessResourceInfo>>([])), issues, new HealthScore(90, false), []);

    private static IEnumerable<string> Details(HomePrioritiesViewModel vm) => vm.Items.Select(i => (string)Read(i, "Detail"));
    private static IEnumerable<string> Resolved(HomePrioritiesViewModel vm) => ((IEnumerable)Read(vm, "Resolved")).Cast<string>();

    private static void AssertTile(HomePrioritiesViewModel vm, string value, string detail)
    {
        Assert.Equal(value, (string)Read(vm, "TileValue"));
        Assert.Equal(detail, (string)Read(vm, "TileDetail"));
    }

    private static string[] VisibleTexts(DependencyObject view) => Descendants<TextBlock>(view)
        .Where(t => t.IsVisible && t.ActualWidth > 0 && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text).ToArray();

    private static void WriteEvidence(int step, string[] texts)
    {
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-t4");
        Directory.CreateDirectory(directory);
        using var writer = new StreamWriter(Path.Combine(directory, "priorites.txt"), append: step != 1, encoding: new UTF8Encoding(false));
        writer.WriteLine($"# étape {step}");
        foreach (var content in texts) writer.WriteLine(content);
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

    private sealed class Runner : IProcessRunner, IDisposable
    {
        private int _runs;
        private int _launches;

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token)
        {
            _runs++;
            throw new InvalidOperationException("Aucun effet autorisé.");
        }

        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token)
        {
            _launches++;
            return Task.FromResult(new ProcessLaunchResult(true, null));
        }

        public void Dispose()
        {
            Assert.Equal(0, _runs);
            Assert.Equal(0, _launches);
        }
    }
}
