using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Actions;
using Virgil.Core.Diagnostic;
using Xunit;

namespace Virgil.Tests;

public sealed class LotDiagnosticTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 16, 24, 0, TimeSpan.Zero);

    [Fact]
    public void Start_healthy_and_partial_readings_say_what_was_measured()
    {
        var (vm, source) = CreateViewModel();
        Assert.Equal("Constats disponibles après le premier relevé.", vm.Top3Caption);
        Assert.Equal("Note non calculée", vm.Health);
        Assert.Equal("Aucun relevé terminé pour le moment.", Read<string>(vm, "LastReading"));
        Assert.Equal(string.Empty, Read<string>(vm, "PartialMeasure"));

        vm.ApplyFindings(DiagnosticAnalyzer.Analyze(Healthy(T0), Signals()));
        Assert.Equal("Aucun problème détecté dans les signaux disponibles.", vm.Top3Caption);
        Assert.Equal("Note de santé : 100/100", vm.Health);
        Assert.Equal("Dernier relevé à 18 h 24", Read<string>(vm, "LastReading"));
        Assert.Empty(Read<IEnumerable<DiagnosticFindingRow>>(vm, "TopFindings"));
        Assert.Equal(string.Empty, Read<string>(vm, "PartialMeasure"));

        vm.ApplyFindings(Partial(T0.AddMinutes(10)));
        Assert.Equal("Note de santé : 100/100 — partielle (1 lecture en échec)", vm.Health);
        Assert.Equal(new[]
        {
            "Non mesuré sur ce PC — Température GPU : aucun capteur exposé.",
            "Lecture en échec — Erreurs Windows : Journal System indisponible."
        }, vm.Unavailable);
        Assert.Equal("Dernier relevé à 18 h 34", Read<string>(vm, "LastReading"));
        Assert.Equal("Mesure partielle : 2 signaux indisponibles (liste dans les détails techniques).",
            Read<string>(vm, "PartialMeasure"));

        vm.ApplyFindings(OneMissing(T0.AddMinutes(20)));
        Assert.Equal("Note de santé : 100/100", vm.Health);
        Assert.Equal("Dernier relevé à 18 h 44", Read<string>(vm, "LastReading"));
        Assert.Equal("Mesure partielle : 1 signal indisponible (liste dans les détails techniques).",
            Read<string>(vm, "PartialMeasure"));
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public void Loaded_reading_puts_three_findings_with_their_action_first()
    {
        var (vm, source) = CreateViewModel();
        vm.ApplyFindings(Loaded(T0));
        Assert.Equal("À vérifier en priorité", vm.Top3Caption);
        Assert.Equal("Note de santé : 50/100", vm.Health);
        Assert.Equal("Dernier relevé à 18 h 24", Read<string>(vm, "LastReading"));
        var top = Read<IEnumerable<DiagnosticFindingRow>>(vm, "TopFindings").ToArray();
        Assert.Equal(new[] { "Processeur : ça chauffe", "Carte : périphérique en échec", "C: : un peu à l'étroit" },
            top.Select(row => row.Title));
        Assert.Equal(new[]
        {
            "Action proposée : vérifiez la ventilation et réduisez la charge.",
            "Choisissez une proposition ci-dessous, puis validez son aperçu.",
            "Choisissez une proposition ci-dessous, puis validez son aperçu."
        }, top.Select(row => row.Recommendation));
        Assert.Equal<string[][]>([[], ["Mettre à jour…"], ["Nettoyer…"]], ActionTitles(top));
        for (var index = 0; index < top.Length; index++)
            Assert.Same(vm.Findings[index], top[index]);
        Assert.False(vm.IsValidationOpen);
        Assert.Equal(0, source.Reads);
    }

    [Fact, Trait("Category", "Windows")]
    public Task Rendered_screen_explains_use_shows_result_first_and_folds_details() => ThemeContrastTests.OnStaWithin(TimeSpan.FromSeconds(10), async deadline =>
    {
        var (vm, source) = CreateViewModel();
        var view = new DiagnosticView(vm);
        var window = Show(view);
        try
        {
            await WpfConditionWait.LayoutAsync(window, () => VisibleTexts(view), deadline,
                "Mesurer ce qui ralentit le PC et savoir quoi vérifier.",
                "Aucun relevé terminé pour le moment.", "Constats disponibles après le premier relevé.");
            var texts = VisibleTexts(view);
            var buttons = VisibleButtons(view);
            WriteEvidence(1, texts, buttons);
            Assert.Contains("Mesurer ce qui ralentit le PC et savoir quoi vérifier.", texts);
            Assert.Contains("Aucun relevé terminé pour le moment.", texts);
            Assert.Contains("Constats disponibles après le premier relevé.", texts);
            Assert.DoesNotContain("Aucun problème détecté dans les signaux disponibles.", texts);
            Assert.Contains("Actualiser le diagnostic", buttons);
            Assert.DoesNotContain("Actualiser", buttons);

            vm.ApplyFindings(Loaded(T0));
            await WpfConditionWait.LayoutAsync(window, () => VisibleTexts(view), deadline,
                "Dernier relevé à 18 h 24", "Note de santé : 50/100", "À vérifier en priorité",
                "Processeur : ça chauffe", "Action proposée : vérifiez la ventilation et réduisez la charge.", "Détails techniques");
            texts = VisibleTexts(view);
            buttons = VisibleButtons(view);
            WriteEvidence(2, texts, buttons);
            Assert.Contains("Dernier relevé à 18 h 24", texts);
            Assert.Contains("Note de santé : 50/100", texts);
            Assert.Contains("À vérifier en priorité", texts);
            Assert.Contains("Processeur : ça chauffe", texts);
            Assert.Contains("Action proposée : vérifiez la ventilation et réduisez la charge.", texts);
            Assert.Contains("Détails techniques", texts);
            Assert.DoesNotContain("Windows a quelques réclamations", texts);
            Assert.Contains("Mettre à jour…", buttons);
            Assert.Contains("Nettoyer…", buttons);

            var expanders = Descendants<Expander>(view).ToArray();
            Assert.Equal(new[]
            {
                "Tous les constats", "Démarrage", "Signaux indisponibles — non mesurés ou lectures en échec",
                "Processeur", "Mémoire", "Disques", "Réseau", "Carte graphique"
            }, expanders.Select(expander => (string)expander.Header));
            Assert.Equal(new[] { false, false, false, false, false, false, false, false },
                expanders.Select(expander => expander.IsExpanded));
            Assert.Single(Descendants<DataGrid>(view));
            Assert.InRange(source.Reads, 0, 1);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Process_grid_stays_outside_folded_details()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src/Virgil.App/Views/DiagnosticView.xaml"));
        var grid = Assert.Single(document.Descendants(), element => element.Name.LocalName == "DataGrid");
        Assert.DoesNotContain(grid.Ancestors(), element => element.Name.LocalName == "Expander");
        Assert.Equal("True", (string?)grid.Attribute("IsReadOnly"));
        Assert.Equal("Processus en lecture seule", (string?)grid.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void Complete_findings_list_keeps_analyzer_order_and_actions()
    {
        var (vm, source) = CreateViewModel();
        vm.ApplyFindings(Loaded(T0));
        Assert.Equal(new[]
        {
            "Processeur : ça chauffe", "Carte : périphérique en échec",
            "C: : un peu à l'étroit", "Windows a quelques réclamations"
        }, vm.Findings.Select(row => row.Title));
        Assert.Equal<string[][]>([[], ["Mettre à jour…"], ["Nettoyer…"], ["Mettre à jour…"]], ActionTitles(vm.Findings));
        Assert.Equal(3, vm.Top3.Count);
        Assert.Equal(0, source.Reads);
    }

    private static DiagnosticValue<T> V<T>(T value) => DiagnosticValue<T>.Available(value);

    private static DiagnosticSnapshot Healthy(DateTimeOffset at) => new(at,
        new(V(10d), [], V(3000d), V(3000d), V(40d)), new(V(16d), V(8d), V(1d), V(1d), V(8d), V(8d)),
        [new("0", "Disque physique", V(0d), V(0d), V(1d), V(1e12), V(1e12), V("Aucune défaillance prédite par Windows (contrôle limité)")),
         new("C:", "Volume", V(0d), V(0d), V(1d), V(100e9), V(50e9), V("Contrôle limité"))], [],
        [new("GPU", V(1d), V(1d), V(1d), V(40d))], []);

    private static DiagnosticSignals Signals() => new(V(new WindowsErrorSummary(0, [])),
        V<IReadOnlyList<DeviceProblem>>([]), V<IReadOnlyList<StartupEntry>>([]), V<IReadOnlyList<ProcessResourceInfo>>([]));

    private static DiagnosticFindings Partial(DateTimeOffset at) =>
        DiagnosticAnalyzer.Analyze(Healthy(at) with { Gpu = [] },
            Signals() with { Errors = DiagnosticSignals.Unavailable.Errors });

    private static DiagnosticFindings OneMissing(DateTimeOffset at) =>
        DiagnosticAnalyzer.Analyze(Healthy(at) with { Gpu = [] }, Signals());

    private static DiagnosticFindings Loaded(DateTimeOffset at)
    {
        var h = Healthy(at);
        return DiagnosticAnalyzer.Analyze(h with
        {
            Cpu = h.Cpu with { TemperatureCelsius = V(95d) },
            Disks = [h.Disks[0], h.Disks[1] with { FreeBytes = V(1d) }]
        }, Signals() with
        {
            Errors = V(new WindowsErrorSummary(12, ["Kernel-Power (12)"])),
            Devices = V<IReadOnlyList<DeviceProblem>>([new("Carte", 1024, 28)])
        });
    }

    private static (DiagnosticViewModel ViewModel, NeverSource Source) CreateViewModel()
    {
        var clock = new LotFSpeechTests.Clock();
        var source = new NeverSource();
        var module = new DiagnosticModule(new DiagnosticSampler(source, clock), gate: new ConfirmationService(clock));
        return (new DiagnosticViewModel(module, clock), source);
    }

    private static T Read<T>(object target, string name)
    {
        var property = target.GetType().GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property.GetValue(target));
    }

    private static string[][] ActionTitles(IEnumerable<DiagnosticFindingRow> rows) =>
        rows.Select(row => row.Actions.Select(action => action.Title).ToArray()).ToArray();

    private static string[] VisibleTexts(DependencyObject view) =>
        Descendants<TextBlock>(view).Where(block => block.IsVisible && block.ActualWidth > 0 && !string.IsNullOrWhiteSpace(block.Text))
            .Select(block => block.Text).ToArray();

    private static string[] VisibleButtons(DependencyObject view) =>
        Descendants<Button>(view).Where(button => button.IsVisible).Select(button => button.Content).OfType<string>().ToArray();

    private static void WriteEvidence(int stage, string[] texts, string[] buttons)
    {
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-t5");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "diagnostic.txt");
        using var writer = new StreamWriter(path, append: stage > 1, encoding: new UTF8Encoding(false));
        writer.WriteLine($"# étape {stage}");
        foreach (var text in texts.Concat(buttons))
            writer.WriteLine(text);
    }

    private static Window Show(FrameworkElement view)
    {
        var window = new Window { Content = view, Width = 1600, Height = 900, ShowInTaskbar = false,
            Background = Brushes.White, Foreground = Brushes.Black };
        window.Show();
        window.UpdateLayout();
        return window;
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

    private sealed class NeverSource : IDiagnosticSource
    {
        public int Reads { get; private set; }

        public async Task<DiagnosticReading> ReadAsync(CancellationToken token)
        {
            Reads++;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }
    }
}
