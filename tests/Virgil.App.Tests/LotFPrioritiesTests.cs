using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Virgil.Contracts;
using Virgil.Core.Status;
using Virgil.Core.Processes;
using Xunit;
using static Virgil.Tests.LotFSpeechTests;

namespace Virgil.Tests;

public sealed class LotFPrioritiesTests
{
    [Theory]
    [InlineData("disk-C:", "Le disque C: est presque plein.", "Ouvrir Nettoyage", "Nettoyage")]
    [InlineData("space.C:", "Le disque C: est presque plein.", "Ouvrir Nettoyage", "Nettoyage")]
    [InlineData("cleanup-potential-warning", "Des fichiers temporaires prennent de la place (7 Go).", "Ouvrir Nettoyage", "Nettoyage")]
    [InlineData("cleanup-potential-attention", "Des fichiers temporaires prennent de la place (7 Go).", "Ouvrir Nettoyage", "Nettoyage")]
    [InlineData("updates-preview", "Des mises à jour d'applications sont disponibles.", "Ouvrir Mises à jour", "Mises à jour")]
    [InlineData("updates-sensitive-preview", "Certaines mises à jour touchent des éléments sensibles : à regarder avant d'installer.", "Ouvrir Mises à jour", "Mises à jour")]
    [InlineData("windows.errors", "Windows a signalé des erreurs ces dernières 24 heures.", "Ouvrir Mises à jour", "Mises à jour")]
    [InlineData("device.C:", "Un périphérique ne fonctionne pas correctement.", "Ouvrir Mises à jour", "Mises à jour")]
    [InlineData("interventions-recommended", "Virgil propose une réparation à examiner.", "Ouvrir Réparation", "Réparation")]
    [InlineData("cpu-usage", "Le processeur est très chargé.", "Ouvrir Ressources", "Ressources")]
    [InlineData("resources-cpu-sustained", "Le processeur est très chargé.", "Ouvrir Ressources", "Ressources")]
    [InlineData("memory-usage", "La mémoire est presque pleine.", "Ouvrir Ressources", "Ressources")]
    [InlineData("resources-memory-high", "La mémoire est presque pleine.", "Ouvrir Ressources", "Ressources")]
    [InlineData("process-memory", "Des programmes consomment beaucoup de ressources.", "Ouvrir Ressources", "Ressources")]
    [InlineData("resources-heavy-processes", "Des programmes consomment beaucoup de ressources.", "Ouvrir Ressources", "Ressources")]
    [InlineData("applications-unknown", "Des applications sont mal identifiées : vérifiez-les avant toute désinstallation.", "Ouvrir Applications", "Applications")]
    [InlineData("network-active-interface", "Aucune connexion réseau active.", "Ouvrir Réseau", "Réseau")]
    [InlineData("cpu.hot", "Le processeur chauffe (90 °C ou plus).", "Ouvrir Diagnostic", "Diagnostic")]
    [InlineData("gpu.hot.C:", "La carte graphique chauffe (90 °C ou plus).", "Ouvrir Diagnostic", "Diagnostic")]
    [InlineData("disk.failure.C:", "Windows prévoit une panne de disque : sauvegardez vos fichiers maintenant.", "Ouvrir Sauvegarde Windows", "ms-settings:backup")]
    [InlineData("process.C:", "Un lanceur de jeux consomme beaucoup en arrière-plan.", "Ouvrir Diagnostic", "Diagnostic")]
    [InlineData("partial-error-C:", "Certaines mesures n'ont pas pu être lues.", "Ouvrir Diagnostic", "Diagnostic")]
    [InlineData("inconnu", "Titre conservé", "Ouvrir Diagnostic", "Diagnostic")]
    public async Task Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination(string id, string text, string button, string destination)
    {
        var store = new SystemStatusStore(); var runner = new Runner(); var vm = Make(store, runner);
        store.PublishScan(Report(new ScanFinding(id, "", "Titre conservé", "", "7 Go", ScanSeverity.Attention, null)));
        var point = Assert.Single(((IEnumerable)Read(vm, "Items")).Cast<object>());
        Assert.Equal(text, Read(point, "Text")); Assert.Equal(button, Read(point, "Button")); Assert.Equal(destination, Read(point, "Destination"));
        var navigations = new List<string>();
        var navigation = vm.GetType().GetEvent("NavigationRequested"); Assert.NotNull(navigation);
        navigation.AddEventHandler(vm, new Action<string>(navigations.Add));
        var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(Read(vm, "OpenCommand"));
        await command.ExecuteAsync(point);
        if (destination == "ms-settings:backup")
        {
            var launch = Assert.Single(runner.Launches); Assert.Equal(destination, launch.FileName);
            Assert.Empty(launch.Arguments); Assert.True(launch.UseShellExecute); Assert.Empty(navigations);
        }
        else { Assert.Empty(runner.Launches); Assert.Equal(new[] { destination }, navigations); }
        Assert.Equal(0, runner.Runs);
    }
    [Fact]
    public void Only_store_runner_and_clock_are_dependencies_and_empty_states_are_exact()
    {
        var type = Type("ViewModels.HomePrioritiesViewModel");
        Assert.Equal(new[] { typeof(ISystemStatusStore), typeof(IProcessRunner), typeof(TimeProvider) }, Assert.Single(type.GetConstructors()).GetParameters().Select(p => p.ParameterType));
        var store = new SystemStatusStore(); var vm = Make(store, new Runner());
        Assert.Equal("Aucune analyse récente : lancez un scan pour voir les priorités.", Read(vm, "StatusText"));
        Assert.Equal("", Read(vm, "MeasuredAt"));
        store.PublishScan(Report());
        Assert.Equal("Aucun point prioritaire au dernier contrôle.", Read(vm, "StatusText"));
        Assert.Equal("Mesuré à 02:00", Read(vm, "MeasuredAt"));
        Assert.Empty(((IEnumerable)Read(vm, "Items")).Cast<object>());
    }
    [Fact, Trait("Category", "Windows")]
    public Task Real_panel_renders_three_points_and_both_empty_states() => ThemeContrastTests.OnSta(() =>
    {
        var store = new SystemStatusStore(); var vm = Make(store, new Runner());
        var panel = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(Type("Controls.HomePrioritiesPanel")));
        panel.DataContext = vm; LotFRevealTests.Capture(panel, "priorites-sans-mesure");
        store.PublishScan(Report()); LotFRevealTests.Capture(panel, "priorites-aucun-point");
        store.PublishScan(Report(new ScanFinding("disk-C:", "", "Disque", "", "", ScanSeverity.Attention, null),
            new("memory-usage", "", "Mémoire", "", "", ScanSeverity.Warning, null),
            new("disk.failure.0", "", "Sauvegarde", "", "", ScanSeverity.Critical, null)));
        Assert.Equal(3, store.LastPriorities!.Value);
        LotFRevealTests.Capture(panel, "priorites-trois-points", 900, 560);
        return Task.CompletedTask;
    });
    [Fact]
    public void Main_window_routes_priorities_through_existing_navigation_handlers()
    {
        var text = File.ReadAllText(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src/Virgil.App/MainWindow.xaml.cs"));
        Assert.Contains("HomePriorities.NavigationRequested", text);
        foreach (var handler in new[] { "Cleanup_Click", "Diagnostic_Click", "Resources_Click", "Applications_Click", "Updates_Click", "Network_Click", "Interventions_Click" })
            Assert.True(text.Split(handler, StringSplitOptions.None).Length >= 3, handler);
    }
    private static object Make(SystemStatusStore store, Runner runner) => Activator.CreateInstance(Type("ViewModels.HomePrioritiesViewModel"), store, runner, new Clock())!;
    internal static SystemScanReport Report(params ScanFinding[] findings) => new(DateTimeOffset.UnixEpoch, ScanMode.Quick, TimeSpan.Zero,
        "", null!, null!, new(0, 0, 0, 0, ScanSeverity.Information, ""), [], [], null!, new(false, 0, 0, [], []), findings, [], []);
    private sealed class Runner : IProcessRunner
    {
        public List<ProcessLaunchRequest> Launches { get; } = new();
        public int Runs { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token)
        { Runs++; throw new InvalidOperationException("Aucun effet autorisé."); }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token)
        {
            Assert.Equal("ms-settings:backup", request.FileName); Assert.Empty(request.Arguments); Assert.True(request.UseShellExecute);
            Launches.Add(request); return Task.FromResult(new ProcessLaunchResult(true, null));
        }
    }
}
