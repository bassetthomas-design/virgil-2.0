using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Core.Actions;
using Virgil.Core.Interventions;
using Virgil.Core.Reports;
using Xunit;

namespace Virgil.Tests;

public sealed class LotReparationBilanTests
{
    private const string PartialOutput = """
        VIRGIL|SFC|OK|Aucune anomalie détectée
        Étape : sfc.exe /verifyonly
        Code sortie : 0.
        La vérification est à 100% terminée.
        VIRGIL|DISM|FINDING|Corruption détectée
        Étape : dism.exe /Online /Cleanup-Image /CheckHealth /English
        Code sortie : 0.
        The component store is repairable.
        VIRGIL|CHKDSK|UNKNOWN|Contrôle non terminé : Accès refusé.
        Étape : chkdsk.exe C: /scan
        Code sortie : 3.

        Étape : virgil-services
        Code sortie : 0.
        VIRGIL|wuauserv|INFO|Running (démarrage à la demande possible)
        VIRGIL|Spooler|OK|Spooler : Running
        VIRGIL|PrintQueue|UNKNOWN|File d'impression indisponible
        VIRGIL|AudioEndpoint|UNKNOWN|Points de sortie audio indisponibles
        VIRGIL|DefaultAudioEndpoint|OK|Sortie audio par défaut disponible
        Vérification incomplète : certains contrôles sont indisponibles. Consultez les détails.
        """;
    private const string PartialSummary = """
        Vérification partielle : 3 contrôle(s) en échec, 1 constat(s).
        Fichiers système (SFC) : terminé, aucune anomalie.
        Image Windows (DISM) : terminé, constat : Corruption détectée. Action proposée : Réparer l'image Windows avec DISM.
        Disque système (CHKDSK) : échec, Contrôle non terminé : Accès refusé.
        Service Windows Update : terminé, aucune anomalie.
        Spouleur d'impression : terminé, aucune anomalie.
        File d'impression : échec, File d'impression indisponible.
        Sorties audio : échec, Points de sortie audio indisponibles.
        Sortie audio par défaut : terminé, aucune anomalie.
        Journal brut : « Détails techniques » et rapport.
        """;
    private const string HealthyOutput = "VIRGIL|SFC|OK|Aucune anomalie détectée\nVIRGIL|DISM|OK|Aucune anomalie détectée\nVIRGIL|CHKDSK|OK|Aucune anomalie détectée\nVIRGIL|Reboot|OK|Redémarrage Windows en attente\nTout est sain.";
    private const string HealthySummary = "Vérification terminée : aucune anomalie.\nFichiers système (SFC) : terminé, aucune anomalie.\nImage Windows (DISM) : terminé, aucune anomalie.\nDisque système (CHKDSK) : terminé, aucune anomalie.\nRedémarrage de Windows : terminé, aucune anomalie.\nJournal brut : « Détails techniques » et rapport.";
    private const string S1 = "Étape 1 sur 4 : Fichiers système (SFC, vérification)";
    private const string S2 = "Étape 2 sur 4 : Image Windows (DISM, contrôle rapide)";

    public static IEnumerable<object[]> Summaries => new[]
    {
        new object[] { InterventionStatus.PartialFailure, PartialOutput.Replace("\r\n", "\n"), PartialOutput.Replace("\r\n", "\n"), PartialSummary.Replace("\r\n", "\n") },
        new object[] { InterventionStatus.Completed, HealthyOutput, null!, HealthySummary },
        new object[] { InterventionStatus.Cancelled, "VIRGIL|SFC|OK|Aucune anomalie détectée\nÉtape : sfc.exe /verifyonly\nCode sortie : 0.\nParcours annulé entre deux commandes. Commandes suivantes non lancées.", null!, "Vérification annulée : les contrôles suivants n'ont pas été lancés.\nFichiers système (SFC) : terminé, aucune anomalie.\nJournal brut : « Détails techniques » et rapport." },
        new object[] { InterventionStatus.Failed, "Droits administrateur requis.", "Droits administrateur requis.", "Vérification en échec : Droits administrateur requis.\nJournal brut : « Détails techniques » et rapport." }
    };

    [Theory]
    [MemberData(nameof(Summaries))]
    public async Task Verify_shows_french_summary_per_check(InterventionStatus status, string output, string? error, string expected)
    {
        var f = new Fixture { Actions = { Result = Result(status, output, error) } };
        await f.Verify();
        Assert.Equal(expected, f.Vm.StatusText);
        Assert.Equal(expected.Split('\n')[0], f.Messages.Last());
        var property = typeof(InterventionsViewModel).GetProperty("TechnicalDetailsText");
        Assert.NotNull(property);
        Assert.Equal(output, property.GetValue(f.Vm));
    }

    [Fact]
    public async Task Progress_line_updates_and_feed_gets_one_message_per_step()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineContext());
        try
        {
            var f = new Fixture();
            var observed = new List<string>();
            f.Actions.OnProgress = progress =>
            {
                foreach (var (step, percent, seconds) in new (string, int?, int)[]
                { (S1, null, 0), (S1, 5, 5), (S1, 5, 16), (S1, 10, 20), (S2, null, 21), (S2, 40, 30) })
                {
                    progress?.Report(new InterventionProgress { Step = step, Percent = percent, Elapsed = TimeSpan.FromSeconds(seconds) });
                    observed.Add(f.Vm.StatusText);
                }
            };
            await f.Verify();
            Assert.Equal(new[] { S1, S2 }, f.Messages.Where(message => message.StartsWith("Étape", StringComparison.Ordinal)));
            Assert.Equal(S1 + " — 10 % · Temps écoulé : 00:00:20", observed[3]);
            Assert.Equal(S2 + " — en cours… · Temps écoulé : 00:00:21", observed[4]);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void Technical_details_are_collapsed_in_view()
    {
        var xml = XDocument.Load(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src/Virgil.App/Views/InterventionsView.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var nodes = xml.Descendants().ToList();
        var expander = Assert.Single(nodes, node => (string?)node.Attribute(x + "Name") == "InterventionsTechnicalDetails");
        Assert.Equal("Expander", expander.Name.LocalName);
        Assert.Equal("Détails techniques", (string?)expander.Attribute("Header"));
        Assert.Equal("False", (string?)expander.Attribute("IsExpanded"));
        var field = Assert.Single(expander.Descendants(), node => node.Name.LocalName == "TextBox");
        Assert.Equal("True", (string?)field.Attribute("IsReadOnly"));
        Assert.Equal("{Binding TechnicalDetailsText, Mode=OneWay}", (string?)field.Attribute("Text"));
        var status = Assert.Single(nodes, node => (string?)node.Attribute(x + "Name") == "InterventionsStatusText");
        Assert.True(nodes.IndexOf(expander) > nodes.IndexOf(status));
    }

    [Fact]
    [Trait("Category", "Windows")]
    public Task Loaded_view_captures_collapsed_and_expanded_summary() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new Fixture { Actions = { Result = Result(InterventionStatus.PartialFailure, PartialOutput, PartialOutput) } };
        await f.Verify();
        f.Vm.CloseReportCommand.Execute(null);
        var view = new InterventionsView(f.Vm) { Width = 1600, Height = 2400 };
        using var presentation = new HwndSource(new HwndSourceParameters("Preuve WPF T2b")
        { Width = 1600, Height = 2400, PositionX = -20000, PositionY = -20000, WindowStyle = unchecked((int)0x90000000) });
        try
        {
            presentation.RootVisual = view;
            void Layout() { view.Measure(new Size(1600, 2400)); view.Arrange(new Rect(0, 0, 1600, 2400)); view.UpdateLayout(); }
            Layout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var expander = view.FindName("InterventionsTechnicalDetails") as Expander;
            if (expander is not null) expander.IsExpanded = false;
            Layout(); Save(view, "bilan-replie");
            if (expander is not null) expander.IsExpanded = true;
            Layout(); Save(view, "bilan-deplie");
            var status = Assert.IsType<TextBlock>(view.FindName("InterventionsStatusText"));
            Assert.Equal(PartialSummary.Replace("\r\n", "\n"), status.Text);
            Assert.NotNull(expander);
            Assert.Equal("Détails techniques", expander.Header);
            var field = Assert.IsType<TextBox>(expander.Content);
            Assert.Equal(PartialOutput.Replace("\r\n", "\n"), field.Text);
            Assert.True(field.IsReadOnly);
            Assert.Equal("Détails techniques de la vérification", AutomationProperties.GetName(field));
            Assert.True(view.IsLoaded);
        }
        finally { presentation.RootVisual = null; }
    });

    private static void Save(FrameworkElement view, string name)
    {
        var bitmap = new RenderTargetBitmap(1600, 2400, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts/ui-smoke/lot-t2b"); Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }

    private static InterventionExecutionResult Result(InterventionStatus status, string output, string? error) => new()
    {
        Action = new InterventionCatalog().Get(InterventionId.VerifyHealth), Status = status,
        ExitCode = status == InterventionStatus.PartialFailure ? -1 : 0, SummaryOutput = output.Replace("\r\n", "\n"), ReadableError = error
    };

    private sealed class InlineContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    private sealed class Fixture
    {
        public FakeDiagnostics Diagnostics { get; } = new();
        public RecordingConfirmation Confirmation { get; } = new();
        public FakeActions Actions { get; } = new();
        public FakeHistory History { get; } = new();
        public InterventionsViewModel Vm { get; }
        public List<string> Messages { get; } = new();
        public Fixture()
        {
            Actions.Authority = Confirmation.Authority;
            Vm = new(Diagnostics, Actions, new InterventionReportBuilder(), History, Confirmation);
            Vm.VirgilMessageRequested += Messages.Add;
        }
        public async Task Verify()
        {
            var pending = Vm.VerifyHealthCommand.ExecuteAsync(null);
            Vm.ExecuteDecisionCommand.Execute(null);
            await pending;
        }
    }

    private sealed class FakeDiagnostics : IInterventionDiagnosticService
    {
        public Task<InterventionDiagnostic> DiagnoseAsync(InterventionId id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<InterventionDiagnostic>> DiagnoseAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InterventionDiagnostic>>(Array.Empty<InterventionDiagnostic>());
    }

    private sealed class FakeActions : IInterventionExecutionService
    {
        public ConfirmationService Authority { get; set; } = null!;
        public InterventionExecutionResult Result { get; set; } = LotReparationBilanTests.Result(InterventionStatus.Completed, HealthyOutput, null);
        public Action<IProgress<InterventionProgress>?>? OnProgress { get; set; }
        public Task<InterventionExecutionResult> ExecuteAsync(InterventionDiagnostic diagnostic, ConfirmedPlan? plan, CancellationToken cancellationToken)
            => ExecuteAsync(diagnostic, plan, null, cancellationToken);
        public Task<InterventionExecutionResult> ExecuteAsync(InterventionDiagnostic diagnostic, ConfirmedPlan? plan, IProgress<InterventionProgress>? progress, CancellationToken cancellationToken)
        {
            Assert.NotNull(plan);
            Assert.True(Authority.TryConsume(plan, InterventionActionPreviewFactory.Create(diagnostic.Definition)).IsAccepted);
            OnProgress?.Invoke(progress);
            return Task.FromResult(Result);
        }
        public InterventionExecutionResult Skip(InterventionDiagnostic diagnostic) => new() { Action = diagnostic.Definition, Status = InterventionStatus.Skipped };
        public InterventionExecutionResult Cancel(InterventionDiagnostic diagnostic) => new() { Action = diagnostic.Definition, Status = InterventionStatus.Cancelled };
        public InterventionSessionReport CreateReport(DateTimeOffset startedAt, IReadOnlyList<InterventionDiagnostic> proposedActions, IReadOnlyList<InterventionExecutionResult> results, IReadOnlyList<string> errors)
            => new() { StartedAt = startedAt, ProposedActions = proposedActions, Results = results, Errors = errors };
    }

    private sealed class FakeHistory : IReportHistoryService
    {
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken) => Task.FromResult(new ReportSaveResult { Success = true });
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
