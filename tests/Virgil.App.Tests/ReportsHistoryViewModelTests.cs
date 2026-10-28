using Virgil.App.Animation;
using Virgil.App.Services;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Reports;
using Xunit;

namespace Virgil.Tests;

public sealed class ReportsHistoryViewModelTests
{
    [Fact]
    public async Task RefreshPopulatesCardsCountAndLatestSummary()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.RefreshAsync();
        var item = Assert.Single(fixture.ViewModel.Items);
        Assert.Same(fixture.Report, item.Report);
        Assert.Equal("2026-10-18 12:30 - SCAN RAPIDE - SUCCES", item.Header);
        Assert.Equal("Diagnostic", item.Title);
        Assert.Equal("Tout va bien", item.Summary);
        Assert.Equal("Statut : Terminé", item.StatusLine);
        Assert.Equal("1 rapport(s) local(aux), limite 30.", fixture.ViewModel.HistoryCountText);
        Assert.Equal("2026-10-18 12:30 - SCAN RAPIDE - Terminé\nTout va bien", fixture.ViewModel.LatestSummaryText);
        Assert.True(fixture.ViewModel.HasReports);
        Assert.True(fixture.ViewModel.IsExportEnabled);
        Assert.Empty(fixture.ViewModel.HistoryNotice);
    }

    [Fact]
    public async Task EmptyHistoryShowsNoticeAndDisablesExport()
    {
        var fixture = new Fixture();
        fixture.History.LoadResult = new();
        await fixture.ViewModel.RefreshAsync();
        Assert.Empty(fixture.ViewModel.Items);
        Assert.False(fixture.ViewModel.HasReports);
        Assert.False(fixture.ViewModel.IsExportEnabled);
        Assert.Equal("Aucun rapport local disponible.", fixture.ViewModel.LatestSummaryText);
        Assert.Equal("Aucun historique local. Les futurs rapports resteront uniquement sur cet appareil.", fixture.ViewModel.HistoryNotice);
    }

    [Fact]
    public async Task LoadErrorsShowWarningAndFirstFourErrors()
    {
        var fixture = new Fixture();
        fixture.History.LoadResult = fixture.History.LoadResult with { Errors = new[] { "Un", "Deux", "Trois", "Quatre", "Cinq" } };
        await fixture.ViewModel.RefreshAsync();
        Assert.Equal("Historique charge avec avertissements.", fixture.ViewModel.StatusText);
        Assert.Equal("Un\nDeux\nTrois\nQuatre", fixture.ViewModel.HistoryNotice);
    }

    [Fact]
    public async Task OpenLatestWithoutReportRequestsMessage()
    {
        var fixture = new Fixture();
        fixture.History.LoadResult = new();
        await fixture.ViewModel.OpenLatestAsync();
        Assert.Equal("Aucun rapport local disponible.", fixture.Message);
        Assert.False(fixture.ViewModel.IsDetailsOpen);
    }

    [Fact]
    public async Task CancelledExportDoesNotCallExporter()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.RefreshAsync();
        await fixture.ViewModel.ExportSelectedCommand.ExecuteAsync(null);
        Assert.Equal(0, fixture.Export.Calls);
        Assert.Equal("Export annulé.\nAucun fichier cree.", fixture.Message);
        Assert.Equal(("Exporter le rapport Virgil", "Rapport texte (*.txt)|*.txt", ".txt", "Virgil-Rapport-20261018-123000.txt"), fixture.Dialog.Arguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedExportUsesVisibleTechnicalDetailsAndOverwrite(bool technical)
    {
        var fixture = new Fixture();
        fixture.Dialog.Path = "rapport.txt";
        fixture.ViewModel.ShowTransientReport(fixture.Report);
        if (technical) fixture.ViewModel.ToggleTechnicalDetailsCommand.Execute(null);
        await fixture.ViewModel.ExportCurrentCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Export.Calls);
        Assert.Same(fixture.Report, fixture.Export.Report);
        Assert.Equal("rapport.txt", fixture.Export.Path);
        Assert.True(fixture.Export.Overwrite);
        Assert.Equal(technical, fixture.Export.Technical);
        Assert.Equal("Rapport exporte manuellement en TXT.\nAucun envoi en ligne.", fixture.Message);
    }

    [Fact]
    public async Task ExportSelectedFallsBackToFirstReport()
    {
        var fixture = new Fixture();
        fixture.Dialog.Path = "rapport.txt";
        await fixture.ViewModel.RefreshAsync();
        await fixture.ViewModel.ExportSelectedCommand.ExecuteAsync(null);
        Assert.Same(fixture.Report, fixture.Export.Report);
    }

    [Fact]
    public async Task ItemCommandsOpenAndExportTheirOwnReport()
    {
        var fixture = new Fixture();
        fixture.Dialog.Path = "rapport.txt";
        var second = fixture.Report with { Title = "Second" };
        fixture.History.LoadResult = new() { Index = new() { Reports = new[] { fixture.Report, second }, TotalCount = 2 } };
        await fixture.ViewModel.RefreshAsync();
        var item = fixture.ViewModel.Items[1];
        item.OpenCommand.Execute(null);
        Assert.Equal("SCAN RAPIDE - Second", fixture.ViewModel.DetailsTitle);
        Assert.False(fixture.ViewModel.CanSaveTransient);
        await item.ExportCommand.ExecuteAsync(null);
        Assert.Same(second, fixture.Export.Report);
    }

    [Fact]
    public void TransientReportOpensDetailsAndCanBeSaved()
    {
        var fixture = new Fixture();
        (VirgilCoreState State, string Label)? requested = null;
        fixture.ViewModel.VirgilStateRequested += (state, label) => requested = (state, label);
        fixture.ViewModel.ShowTransientReport(fixture.Report);
        Assert.True(fixture.ViewModel.IsDetailsOpen);
        Assert.True(fixture.ViewModel.CanSaveTransient);
        Assert.True(fixture.ViewModel.IsExportEnabled);
        Assert.Equal(VirgilCoreState.Success, fixture.ViewModel.MiniCoreState);
        Assert.Equal((VirgilCoreState.Communicating, "RAPPORT"), requested);
        Assert.False(fixture.ViewModel.IsTechnicalDetailsVisible);
        Assert.Equal("Aucune", fixture.ViewModel.Errors);
        Assert.Equal("Aucun detail technique supplementaire.", fixture.ViewModel.TechnicalDetails);
    }

    [Fact]
    public async Task SaveTransientSuccessRaisesEventAndRefreshesOnce()
    {
        var fixture = new Fixture();
        fixture.History.SaveResult = new() { Success = true, Report = fixture.Report };
        fixture.ViewModel.ShowTransientReport(fixture.Report);
        await fixture.ViewModel.SaveTransientCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Persisted);
        Assert.Equal(1, fixture.History.LoadCalls);
        Assert.Same(fixture.Report, fixture.History.SavedReport);
        Assert.False(fixture.ViewModel.CanSaveTransient);
        Assert.Single(fixture.ViewModel.Items);
        Assert.Equal("Rapport enregistre localement.\nAucun envoi en ligne.", fixture.Message);
        await fixture.ViewModel.SaveTransientCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.History.SaveCalls);
    }

    [Fact]
    public async Task SaveTransientFailureUsesReadableErrorWithoutEventOrRefresh()
    {
        var fixture = new Fixture();
        fixture.History.SaveResult = new() { ReadableError = "Ecriture refusée." };
        fixture.ViewModel.ShowTransientReport(fixture.Report);
        await fixture.ViewModel.SaveTransientCommand.ExecuteAsync(null);
        Assert.Equal("Ecriture refusée.", fixture.Message);
        Assert.Equal(0, fixture.Persisted);
        Assert.Equal(0, fixture.History.LoadCalls);
        Assert.True(fixture.ViewModel.CanSaveTransient);
    }

    [Fact]
    public void DetailsPreserveActionFormattingAndTechnicalContent()
    {
        var fixture = new Fixture();
        var action = new ReportAction { Name = "Action", Status = ReportActionStatus.Failed, Result = "Résultat", ReadableError = "Erreur", TechnicalDetails = "Trace" };
        fixture.ViewModel.ShowTransientReport(fixture.Report with
        {
            Severity = ReportSeverity.Error, TechnicalDetails = "Diagnostic", RestartRequired = true,
            ProposedActions = new[] { action }, ExecutedActions = new[] { action }, SkippedActions = new[] { action }, Errors = new[] { "Erreur" }
        });
        Assert.Equal("- Action [Failed] : Résultat - Erreur", fixture.ViewModel.ProposedActions);
        Assert.Equal(fixture.ViewModel.ProposedActions, fixture.ViewModel.ExecutedActions);
        Assert.Equal(fixture.ViewModel.ProposedActions, fixture.ViewModel.SkippedActions);
        Assert.Equal("- Erreur", fixture.ViewModel.Errors);
        Assert.Equal("Diagnostic\nAction : Trace\nAction : Trace\nAction : Trace", fixture.ViewModel.TechnicalDetails);
        Assert.Contains("Redémarrage requis : oui", fixture.ViewModel.SimpleDetails);
        Assert.Equal(VirgilCoreState.Warning, fixture.ViewModel.MiniCoreState);
        fixture.ViewModel.ToggleTechnicalDetailsCommand.Execute(null);
        Assert.Equal("MASQUER DÉTAILS TECHNIQUES", fixture.ViewModel.ToggleTechnicalDetailsLabel);
        fixture.ViewModel.ToggleTechnicalDetailsCommand.Execute(null);
        Assert.Equal("VOIR DÉTAILS TECHNIQUES", fixture.ViewModel.ToggleTechnicalDetailsLabel);
        fixture.ViewModel.CloseDetailsCommand.Execute(null);
        Assert.False(fixture.ViewModel.IsDetailsOpen);
        Assert.Equal(VirgilCoreState.Idle, fixture.ViewModel.MiniCoreState);
    }

    [Fact]
    public void ReturnHomeRaisesEvent()
    {
        var fixture = new Fixture();
        var calls = 0;
        fixture.ViewModel.ReturnHomeRequested += (_, _) => calls++;
        fixture.ViewModel.ReturnHomeCommand.Execute(null);
        Assert.Equal(1, calls);
    }

    private sealed class Fixture
    {
        public ReportEntry Report { get; } = new() { Date = new DateTimeOffset(2026, 10, 18, 12, 30, 0, TimeSpan.Zero), Kind = ReportKind.QuickScan, Severity = ReportSeverity.Success, Title = "Diagnostic", Summary = "Tout va bien", Status = "Terminé" };
        public FakeHistory History { get; } = new();
        public FakeExport Export { get; } = new();
        public FakeDialog Dialog { get; } = new();
        public ReportsHistoryViewModel ViewModel { get; }
        public string? Message { get; private set; }
        public int Persisted { get; private set; }
        public Fixture()
        {
            History.LoadResult = new() { Index = new() { Reports = new[] { Report }, TotalCount = 1 } };
            ViewModel = new(History, Export, Dialog);
            ViewModel.VirgilMessageRequested += message => Message = message;
            ViewModel.ReportPersisted += (_, _) => Persisted++;
        }
    }

    private sealed class FakeDialog : IFileSaveDialog
    {
        public string? Path { get; set; }
        public (string, string, string, string) Arguments { get; private set; }
        public string? PickSavePath(string title, string filter, string defaultExt, string fileName)
        {
            Arguments = (title, filter, defaultExt, fileName);
            return Path;
        }
    }

    private sealed class FakeHistory : IReportHistoryService
    {
        public ReportHistoryLoadResult LoadResult { get; set; } = new();
        public ReportSaveResult SaveResult { get; set; } = new();
        public ReportEntry? SavedReport { get; private set; }
        public int LoadCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) { LoadCalls++; return Task.FromResult(LoadResult); }
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken) { SaveCalls++; SavedReport = report; return Task.FromResult(SaveResult); }
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeExport : IReportExportService
    {
        public int Calls { get; private set; }
        public ReportEntry? Report { get; private set; }
        public string? Path { get; private set; }
        public bool Technical { get; private set; }
        public bool Overwrite { get; private set; }
        public string BuildText(ReportEntry report, bool includeTechnicalDetails) => throw new NotSupportedException();
        public Task<ReportExportResult> ExportAsync(ReportEntry report, string destinationPath, bool includeTechnicalDetails, bool overwriteConfirmed, CancellationToken cancellationToken)
        {
            Calls++; Report = report; Path = destinationPath; Technical = includeTechnicalDetails; Overwrite = overwriteConfirmed;
            return Task.FromResult(new ReportExportResult { Success = true });
        }
    }
}
