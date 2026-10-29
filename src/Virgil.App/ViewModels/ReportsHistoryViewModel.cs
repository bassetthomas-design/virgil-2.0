using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Virgil.App.Animation;
using Virgil.App.Services;
using Virgil.Core.Reports;
using Virgil.Contracts;

namespace Virgil.App.ViewModels;

public partial class ReportsHistoryViewModel : ObservableObject
{
    private readonly IReportHistoryService _historyService;
    private readonly IReportExportService _exportService;
    private readonly IFileSaveDialog _fileSaveDialog;
    private IReadOnlyList<ReportEntry> _reports = Array.Empty<ReportEntry>();
    private ReportEntry? _selectedReport;
    private bool _selectedReportIsPersistent;

    public ReportsHistoryViewModel(IReportHistoryService historyService, IReportExportService exportService,
        IFileSaveDialog fileSaveDialog)
    {
        _historyService = historyService;
        _exportService = exportService;
        _fileSaveDialog = fileSaveDialog;
    }

    public event Action<string>? VirgilMessageRequested;
    public event Action<VirgilCoreState, string>? VirgilStateRequested;
    public event EventHandler? ReturnHomeRequested;
    public event EventHandler? ReportPersisted;

    public ObservableCollection<ReportHistoryItemViewModel> Items { get; } = new();

    [ObservableProperty] private string statusText = "SCANS, ACTIONS ET EXPORTS LOCAUX";

    [ObservableProperty] private string historyCountText = "0 rapport";

    [ObservableProperty] private string latestSummaryText = "Aucun rapport local disponible.";

    [ObservableProperty] private string historyNotice = "Aucun historique local. Les futurs rapports resteront uniquement sur cet appareil.";

    [ObservableProperty] private string detailsTitle = "RAPPORT";

    [ObservableProperty] private string simpleDetails = string.Empty;

    [ObservableProperty] private string proposedActions = string.Empty;

    [ObservableProperty] private string executedActions = string.Empty;

    [ObservableProperty] private string skippedActions = string.Empty;

    [ObservableProperty] private string errors = string.Empty;

    [ObservableProperty] private string technicalDetails = string.Empty;

    [ObservableProperty] private string toggleTechnicalDetailsLabel = "VOIR DÉTAILS TECHNIQUES";

    [ObservableProperty] private bool hasReports;

    [ObservableProperty] private bool isDetailsOpen;

    [ObservableProperty] private bool isTechnicalDetailsVisible;

    [ObservableProperty] private bool canSaveTransient;

    [ObservableProperty] private bool isExportEnabled;

    [ObservableProperty] private VirgilCoreState miniCoreState = VirgilCoreState.Idle;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        StatusText = "Lecture de l historique local.";
        var result = await _historyService.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        _reports = result.Index.Reports;
        RenderHistory(result);
        StatusText = result.Errors.Count == 0
            ? "SCANS, ACTIONS ET EXPORTS LOCAUX"
            : "Historique charge avec avertissements.";
    }

    [RelayCommand]
    public async Task OpenLatestAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
        var latest = _reports.FirstOrDefault();
        if (latest is null)
        {
            VirgilMessageRequested?.Invoke("Aucun rapport local disponible.");
            return;
        }

        OpenReport(latest, isPersistent: true);
    }

    public void ShowTransientReport(ReportEntry report)
    {
        OpenReport(report, isPersistent: false);
    }

    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        _selectedReport ??= _reports.FirstOrDefault();
        await ExportReportAsync(_selectedReport).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ExportCurrentAsync() => ExportReportAsync(_selectedReport);

    [RelayCommand]
    private void ReturnHome() => ReturnHomeRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleTechnicalDetails()
    {
        IsTechnicalDetailsVisible = !IsTechnicalDetailsVisible;
        ToggleTechnicalDetailsLabel = IsTechnicalDetailsVisible
            ? "MASQUER DÉTAILS TECHNIQUES" : "VOIR DÉTAILS TECHNIQUES";
    }

    [RelayCommand]
    private void CloseDetails()
    {
        IsDetailsOpen = false;
        MiniCoreState = VirgilCoreState.Idle;
    }

    private void RenderHistory(ReportHistoryLoadResult result)
    {
        Items.Clear();
        HistoryCountText = $"{result.Index.TotalCount} rapport(s) local(aux), limite {result.Index.AppliedLimit}.";
        var latest = result.Index.Reports.FirstOrDefault();
        LatestSummaryText = latest is null
            ? "Aucun rapport local disponible."
            : $"{latest.Date:yyyy-MM-dd HH:mm} - {KindLabel(latest.Kind)} - {latest.Status}\n{latest.Summary}";
        HasReports = latest is not null;
        IsExportEnabled = latest is not null;
        foreach (var report in result.Index.Reports)
        {
            Items.Add(new ReportHistoryItemViewModel(report,
                $"{report.Date:yyyy-MM-dd HH:mm} - {KindLabel(report.Kind)} - {SeverityLabel(report.Severity)}",
                () => OpenReport(report, isPersistent: true), () => ExportReportAsync(report)));
        }

        HistoryNotice = !HasReports
            ? "Aucun historique local. Les futurs rapports resteront uniquement sur cet appareil."
            : string.Join("\n", result.Errors.Take(4));
    }

    [RelayCommand]
    private async Task SaveTransientAsync()
    {
        if (_selectedReport is null || _selectedReportIsPersistent)
        {
            return;
        }

        var result = await _historyService.SaveAsync(_selectedReport, CancellationToken.None).ConfigureAwait(true);
        if (!result.Success)
        {
            VirgilMessageRequested?.Invoke(result.ReadableError ?? "Historique local indisponible.");
            return;
        }

        _selectedReport = result.Report ?? _selectedReport;
        _selectedReportIsPersistent = true;
        ReportPersisted?.Invoke(this, EventArgs.Empty);
        CanSaveTransient = false;
        VirgilMessageRequested?.Invoke("Rapport enregistré localement.\nAucun envoi en ligne.");
        await RefreshAsync().ConfigureAwait(true);
    }

    private void OpenReport(ReportEntry report, bool isPersistent)
    {
        _selectedReport = report;
        _selectedReportIsPersistent = isPersistent;
        IsTechnicalDetailsVisible = false;
        DetailsTitle = $"{KindLabel(report.Kind)} - {report.Title}";
        SimpleDetails = string.Join("\n", new[]
        {
            $"Date : {report.Date:yyyy-MM-dd HH:mm:ss}",
            $"État : {report.Status}",
            $"Résumé : {report.Summary}",
            $"Redémarrage requis : {(report.RestartRequired ? "oui" : "non")}",
            string.IsNullOrWhiteSpace(report.SimpleView) ? report.Summary : report.SimpleView
        });
        ProposedActions = FormatActions(report.ProposedActions);
        ExecutedActions = FormatActions(report.ExecutedActions);
        SkippedActions = FormatActions(report.SkippedActions);
        Errors = report.Errors.Count == 0 ? "Aucune" : string.Join("\n", report.Errors.Select(error => "- " + error));
        TechnicalDetails = BuildTechnicalDetails(report);
        ToggleTechnicalDetailsLabel = "VOIR DÉTAILS TECHNIQUES";
        CanSaveTransient = !isPersistent;
        IsExportEnabled = true;
        IsDetailsOpen = true;
        MiniCoreState = report.Severity >= ReportSeverity.Error
            ? VirgilCoreState.Warning
            : VirgilCoreState.Success;
        VirgilStateRequested?.Invoke(VirgilCoreState.Communicating, "RAPPORT");
    }

    private async Task ExportReportAsync(ReportEntry? report)
    {
        if (report is null)
        {
            VirgilMessageRequested?.Invoke("Aucun rapport sélectionné pour l export.");
            return;
        }

        var path = _fileSaveDialog.PickSavePath(
            "Exporter le rapport Virgil", "Rapport texte (*.txt)|*.txt", ".txt",
            $"Virgil-Rapport-{report.Date:yyyyMMdd-HHmmss}.txt");
        if (path is null)
        {
            VirgilMessageRequested?.Invoke("Export annulé.\nAucun fichier créé.");
            return;
        }

        var result = await _exportService.ExportAsync(
            report,
            path,
            includeTechnicalDetails: IsTechnicalDetailsVisible,
            overwriteConfirmed: true,
            CancellationToken.None).ConfigureAwait(true);
        VirgilMessageRequested?.Invoke(result.Success
            ? "Rapport exporté manuellement en TXT.\nAucun envoi en ligne."
            : result.ReadableError ?? "Export impossible.");
    }

    private static string FormatActions(IReadOnlyList<ReportAction> actions)
    {
        return actions.Count == 0
            ? "Aucune"
            : string.Join("\n", actions.Select(action =>
                $"- {action.Name} [{action.Status}] : {action.Result}" +
                (string.IsNullOrWhiteSpace(action.ReadableError) ? string.Empty : $" - {action.ReadableError}")));
    }

    private static string BuildTechnicalDetails(ReportEntry report)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(report.TechnicalDetails))
        {
            values.Add(report.TechnicalDetails);
        }

        values.AddRange(report.ProposedActions
            .Concat(report.ExecutedActions)
            .Concat(report.SkippedActions)
            .Where(action => !string.IsNullOrWhiteSpace(action.TechnicalDetails))
            .Select(action => $"{action.Name} : {action.TechnicalDetails}"));
        return values.Count == 0
            ? "Aucun détail technique supplémentaire."
            : string.Join("\n", values);
    }

    private static string KindLabel(ReportKind kind)
    {
        return kind switch
        {
            ReportKind.QuickScan => "SCAN RAPIDE",
            ReportKind.DeepScan => "ANALYSE APPROFONDIE",
            ReportKind.Cleanup => "NETTOYAGE",
            ReportKind.Updates => "MISES À JOUR",
            ReportKind.Interventions => "INTERVENTIONS",
            ReportKind.Resources => "RESSOURCES",
            ReportKind.ApplicationManagement => "APPLICATIONS",
            _ => kind.ToString().ToUpperInvariant()
        };
    }

    private static string SeverityLabel(ReportSeverity severity)
    {
        return severity switch
        {
            ReportSeverity.Success => "SUCCÈS",
            ReportSeverity.Warning => "ATTENTION",
            ReportSeverity.Error => "ERREUR",
            ReportSeverity.Critical => "CRITIQUE",
            _ => "INFO"
        };
    }
}
