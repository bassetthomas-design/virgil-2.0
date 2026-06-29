using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Virgil.App.Controls;
using Virgil.Core.Reports;
using Virgil.Domain;

namespace Virgil.App.Views;

public partial class ReportsHistoryView : UserControl
{
    private IReportHistoryService _historyService;
    private IReportExportService _exportService;
    private IReadOnlyList<ReportEntry> _reports = Array.Empty<ReportEntry>();
    private ReportEntry? _selectedReport;
    private bool _selectedReportIsPersistent;
    private bool _technicalDetailsVisible;

    public ReportsHistoryView()
    {
        InitializeComponent();
        _historyService = new ReportHistoryService();
        _exportService = new ReportExportService();
        RenderEmptyHistory();
    }

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public event EventHandler? ReportPersisted;

    public void Configure(IReportHistoryService historyService, IReportExportService exportService)
    {
        _historyService = historyService;
        _exportService = exportService;
    }

    public async Task RefreshAsync()
    {
        ReportsStatusText.Text = "Lecture de l historique local.";
        var result = await _historyService.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        _reports = result.Index.Reports;
        RenderHistory(result);
        ReportsStatusText.Text = result.Errors.Count == 0
            ? "SCANS, ACTIONS ET EXPORTS LOCAUX"
            : "Historique charge avec avertissements.";
    }

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

    public bool TryCloseOverlay()
    {
        if (ReportDetailsOverlay.Visibility != Visibility.Visible)
        {
            return false;
        }

        HideReport();
        return true;
    }

    public void FocusPrimaryButton()
    {
        LastPersistentReportButton.Focus();
    }

    private async void LastReport_Click(object sender, RoutedEventArgs e)
    {
        await OpenLatestAsync();
    }

    private async void RefreshHistory_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        HistoryHeading.BringIntoView();
    }

    private async void ExportSelectedReport_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedReport is null)
        {
            _selectedReport = _reports.FirstOrDefault();
        }

        await ExportReportAsync(_selectedReport);
    }

    private void ReturnHome_Click(object sender, RoutedEventArgs e)
    {
        ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleTechnicalDetails_Click(object sender, RoutedEventArgs e)
    {
        _technicalDetailsVisible = !_technicalDetailsVisible;
        TechnicalDetailsPanel.Visibility = _technicalDetailsVisible ? Visibility.Visible : Visibility.Collapsed;
        ToggleTechnicalDetailsButton.Content = _technicalDetailsVisible
            ? "MASQUER DETAILS TECHNIQUES"
            : "VOIR DETAILS TECHNIQUES";
    }

    private async void SaveTransientReport_Click(object sender, RoutedEventArgs e)
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
        SaveTransientReportButton.Visibility = Visibility.Collapsed;
        VirgilMessageRequested?.Invoke("Rapport enregistre localement.\nAucun envoi en ligne.");
        await RefreshAsync().ConfigureAwait(true);
    }

    private async void ExportCurrentReport_Click(object sender, RoutedEventArgs e)
    {
        await ExportReportAsync(_selectedReport);
    }

    private void CloseReportDetails_Click(object sender, RoutedEventArgs e)
    {
        HideReport();
    }

    private void RenderHistory(ReportHistoryLoadResult result)
    {
        HistoryPanel.Children.Clear();
        HistoryCountText.Text = $"{result.Index.TotalCount} rapport(s) local(aux), limite {result.Index.AppliedLimit}.";
        var latest = result.Index.Reports.FirstOrDefault();
        LatestReportSummaryText.Text = latest is null
            ? "Aucun rapport local disponible."
            : $"{latest.Date:yyyy-MM-dd HH:mm} - {KindLabel(latest.Kind)} - {latest.Status}\n{latest.Summary}";
        LastPersistentReportButton.IsEnabled = latest is not null;
        ExportSelectedReportButton.IsEnabled = latest is not null;

        if (result.Index.Reports.Count == 0)
        {
            RenderEmptyHistory();
            return;
        }

        foreach (var report in result.Index.Reports)
        {
            HistoryPanel.Children.Add(CreateHistoryCard(report));
        }

        if (result.Errors.Count > 0)
        {
            HistoryPanel.Children.Add(CreateTextCard(string.Join("\n", result.Errors.Take(4))));
        }
    }

    private UIElement CreateHistoryCard(ReportEntry report)
    {
        var card = new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var stack = new StackPanel();
        stack.Children.Add(CreateText(
            $"{report.Date:yyyy-MM-dd HH:mm} - {KindLabel(report.Kind)} - {SeverityLabel(report.Severity)}",
            primary: true));
        stack.Children.Add(CreateText(report.Title));
        stack.Children.Add(CreateText(report.Summary));
        stack.Children.Add(CreateText($"Statut : {report.Status}"));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(CreateButton("OUVRIR", () =>
        {
            OpenReport(report, isPersistent: true);
            return Task.CompletedTask;
        }, primary: true));
        actions.Children.Add(CreateButton("EXPORTER", () => ExportReportAsync(report)));
        stack.Children.Add(actions);
        card.Child = stack;
        return card;
    }

    private void OpenReport(ReportEntry report, bool isPersistent)
    {
        _selectedReport = report;
        _selectedReportIsPersistent = isPersistent;
        _technicalDetailsVisible = false;
        DetailsTitleText.Text = $"{KindLabel(report.Kind)} - {report.Title}";
        SimpleDetailsText.Text = string.Join("\n", new[]
        {
            $"Date : {report.Date:yyyy-MM-dd HH:mm:ss}",
            $"Etat : {report.Status}",
            $"Resume : {report.Summary}",
            $"Redemarrage requis : {(report.RestartRequired ? "oui" : "non")}",
            string.IsNullOrWhiteSpace(report.SimpleView) ? report.Summary : report.SimpleView
        });
        ProposedActionsText.Text = FormatActions(report.ProposedActions);
        ExecutedActionsText.Text = FormatActions(report.ExecutedActions);
        SkippedActionsText.Text = FormatActions(report.SkippedActions);
        ErrorsText.Text = report.Errors.Count == 0 ? "Aucune" : string.Join("\n", report.Errors.Select(error => "- " + error));
        TechnicalDetailsText.Text = BuildTechnicalDetails(report);
        TechnicalDetailsPanel.Visibility = Visibility.Collapsed;
        ToggleTechnicalDetailsButton.Content = "VOIR DETAILS TECHNIQUES";
        SaveTransientReportButton.Visibility = isPersistent ? Visibility.Collapsed : Visibility.Visible;
        ExportSelectedReportButton.IsEnabled = true;
        ReportDetailsOverlay.Visibility = Visibility.Visible;
        ReportsMiniCore.SetState(report.Severity >= ReportSeverity.Error
            ? VirgilCoreState.Warning
            : VirgilCoreState.Success);
        VirgilStateRequested?.Invoke(VirgilCoreState.Communicating, "RAPPORT");
        CloseReportDetailsButton.Focus();
    }

    private void HideReport()
    {
        ReportDetailsOverlay.Visibility = Visibility.Collapsed;
        ReportsMiniCore.SetState(VirgilCoreState.Idle);
        LastPersistentReportButton.Focus();
    }

    private async Task ExportReportAsync(ReportEntry? report)
    {
        if (report is null)
        {
            VirgilMessageRequested?.Invoke("Aucun rapport selectionne pour l export.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exporter le rapport Virgil",
            Filter = "Rapport texte (*.txt)|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"Virgil-Rapport-{report.Date:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog() != true)
        {
            VirgilMessageRequested?.Invoke("Export annule.\nAucun fichier cree.");
            return;
        }

        var result = await _exportService.ExportAsync(
            report,
            dialog.FileName,
            includeTechnicalDetails: _technicalDetailsVisible,
            overwriteConfirmed: true,
            CancellationToken.None).ConfigureAwait(true);
        VirgilMessageRequested?.Invoke(result.Success
            ? "Rapport exporte manuellement en TXT.\nAucun envoi en ligne."
            : result.ReadableError ?? "Export impossible.");
    }

    private void RenderEmptyHistory()
    {
        HistoryPanel.Children.Clear();
        HistoryPanel.Children.Add(CreateTextCard("Aucun historique local. Les futurs rapports resteront uniquement sur cet appareil."));
    }

    private Border CreateTextCard(string text)
    {
        return new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 0, 0, 8),
            Child = CreateText(text)
        };
    }

    private TextBlock CreateText(string text, bool primary = false)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = TryFindResource("VirgilHudSecondaryText") as Style,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        };
        if (primary)
        {
            block.SetResourceReference(ForegroundProperty, "App.TextPrimaryBrush");
            block.FontWeight = FontWeights.SemiBold;
        }

        return block;
    }

    private Button CreateButton(string content, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = content,
            Style = TryFindResource(primary ? "VirgilPrimaryButton" : "VirgilSecondaryButton") as Style,
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 100
        };
        button.Click += async (_, _) => await action().ConfigureAwait(true);
        return button;
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
            ? "Aucun detail technique supplementaire."
            : string.Join("\n", values);
    }

    private static string KindLabel(ReportKind kind)
    {
        return kind switch
        {
            ReportKind.QuickScan => "SCAN RAPIDE",
            ReportKind.DeepScan => "ANALYSE APPROFONDIE",
            ReportKind.Cleanup => "NETTOYAGE",
            ReportKind.Updates => "MISES A JOUR",
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
            ReportSeverity.Success => "SUCCES",
            ReportSeverity.Warning => "ATTENTION",
            ReportSeverity.Error => "ERREUR",
            ReportSeverity.Critical => "CRITIQUE",
            _ => "INFO"
        };
    }
}
