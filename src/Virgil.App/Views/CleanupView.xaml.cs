using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Virgil.App.Controls;
using Virgil.Core.Cleanup;
using Virgil.Core.Reports;
using Virgil.Domain;

namespace Virgil.App.Views;

public partial class CleanupView : UserControl
{
    private readonly ICleanupPreviewService _previewService;
    private readonly ICleanupExecutionService _executionService;
    private readonly List<CleanupZonePreview> _lastPreview = new();
    private IReportHistoryService? _reportHistoryService;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource<CleanupZoneDecision>? _activeDecision;
    private CleanupSessionReport? _lastReport;
    private bool _operationInProgress;

    public CleanupView()
        : this(new CleanupPreviewService(), new CleanupExecutionService())
    {
    }

    public CleanupView(
        ICleanupPreviewService previewService,
        ICleanupExecutionService executionService)
    {
        InitializeComponent();
        _previewService = previewService;
        _executionService = executionService;
    }

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public void ConfigureReportHistory(IReportHistoryService reportHistoryService)
    {
        _reportHistoryService = reportHistoryService;
    }

    public void FocusAnalyzeButton()
    {
        AnalyzeZonesButton.Focus();
    }

    public void CancelActiveOperation()
    {
        _operationCancellation?.Cancel();
        CompleteDecision(CleanupZoneDecision.CancelAll);
    }

    public bool TryCloseOverlay()
    {
        if (CleanupReportOverlay.Visibility == Visibility.Visible)
        {
            HideReport();
            return true;
        }

        if (ZoneValidationOverlay.Visibility == Visibility.Visible)
        {
            CompleteDecision(CleanupZoneDecision.Skip);
            return true;
        }

        if (_operationInProgress)
        {
            CancelActiveOperation();
            VirgilMessageRequested?.Invoke("Annulation demandee.\nOperation en cours d'arret.");
            return true;
        }

        return false;
    }

    private async void AnalyzeZones_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeZonesAsync();
    }

    private async void LaunchCleanup_Click(object sender, RoutedEventArgs e)
    {
        await LaunchCleanupAsync();
    }

    private void ViewCleanupReport_Click(object sender, RoutedEventArgs e)
    {
        ShowReport();
    }

    private void ReturnHome_Click(object sender, RoutedEventArgs e)
    {
        ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExecuteZone_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(CleanupZoneDecision.Execute);
    }

    private void SkipZone_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(CleanupZoneDecision.Skip);
    }

    private void CancelAll_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(CleanupZoneDecision.CancelAll);
    }

    private void CloseCleanupReport_Click(object sender, RoutedEventArgs e)
    {
        HideReport();
    }

    private async Task AnalyzeZonesAsync()
    {
        if (!BeginOperation("Analyse des zones autorisees."))
        {
            return;
        }

        try
        {
            _lastPreview.Clear();
            CleanupStatusText.Text = "Analyse en cours.";
            RequestVirgilState(VirgilCoreState.Scanning, "NETTOYAGE");
            VirgilMessageRequested?.Invoke("Analyse des zones de nettoyage initialisee.");

            var previews = await _previewService
                .PreviewAsync(new Progress<CleanupProgress>(HandleProgress), _operationCancellation!.Token)
                .ConfigureAwait(true);

            _lastPreview.AddRange(previews);
            RenderPreviews();
            UpdateCommandState();
            CleanupStatusText.Text = "Previsualisation disponible. Validation par zone requise.";
            RequestVirgilState(VirgilCoreState.Success, "PRET");
            AnnouncePreviewResult();
        }
        catch (OperationCanceledException)
        {
            CleanupStatusText.Text = "Analyse annulee.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Nettoyage interrompu.\nLes zones restantes n'ont pas ete modifiees.");
        }
        catch
        {
            CleanupStatusText.Text = "Analyse nettoyage indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Analyse nettoyage indisponible.\nAucune action effectuee.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task LaunchCleanupAsync()
    {
        if (_lastPreview.All(preview => !preview.HasEligibleCandidates))
        {
            VirgilMessageRequested?.Invoke("Aucune zone eligible.\nRelancez une analyse.");
            return;
        }

        if (!BeginOperation("Nettoyage guide en attente de validation."))
        {
            return;
        }

        var startedAt = DateTimeOffset.Now;
        var results = new List<CleanupStepResult>();
        var sessionErrors = new List<string>();

        try
        {
            RequestVirgilState(VirgilCoreState.Scanning, "GUIDE");

            foreach (var preview in _lastPreview.Where(preview => preview.HasEligibleCandidates))
            {
                _operationCancellation!.Token.ThrowIfCancellationRequested();
                var decision = await AskZoneDecisionAsync(preview, _operationCancellation.Token).ConfigureAwait(true);
                RequestVirgilState(VirgilCoreState.Scanning, "GUIDE");

                if (decision == CleanupZoneDecision.CancelAll)
                {
                    results.Add(_executionService.CancelZone(preview));
                    sessionErrors.Add("Session annulee par l'utilisateur.");
                    break;
                }

                if (decision == CleanupZoneDecision.Skip)
                {
                    results.Add(_executionService.SkipZone(preview));
                    VirgilMessageRequested?.Invoke("Zone passee.\nAucune modification effectuee.");
                    continue;
                }

                RequestVirgilState(VirgilCoreState.Executing, "NETTOYAGE");
                var result = await _executionService
                    .ExecuteZoneAsync(preview, new Progress<CleanupProgress>(HandleProgress), _operationCancellation.Token)
                    .ConfigureAwait(true);

                results.Add(result);
                AnnounceZoneResult(result);
            }
        }
        catch (OperationCanceledException)
        {
            sessionErrors.Add("Session annulee.");
        }
        catch
        {
            sessionErrors.Add("Erreur pendant le nettoyage guide.");
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
        }
        finally
        {
            _lastReport = _executionService.CreateReport(startedAt, results, sessionErrors);
            await PersistReportAsync(ReportMapper.FromCleanup(_lastReport, _lastPreview.Count));
            ViewCleanupReportButton.IsEnabled = true;
            RenderPreviews();
            EndOperation();
            CompleteSessionState(_lastReport);
            AnnounceSessionEnd(_lastReport);
            ShowReport();
        }
    }

    private async Task PersistReportAsync(ReportEntry report)
    {
        if (_reportHistoryService is null)
        {
            return;
        }

        var result = await _reportHistoryService.SaveAsync(report, CancellationToken.None).ConfigureAwait(true);
        if (!result.Success || !string.IsNullOrWhiteSpace(result.ReadableError))
        {
            VirgilMessageRequested?.Invoke(result.ReadableError ?? "Historique local indisponible. Rapport conserve en memoire.");
        }
    }

    private bool BeginOperation(string status)
    {
        if (_operationInProgress)
        {
            VirgilMessageRequested?.Invoke("Operation deja en cours.");
            return false;
        }

        _operationInProgress = true;
        _operationCancellation = new CancellationTokenSource();
        CleanupStatusText.Text = status;
        SetButtonsEnabled(false);
        Mouse.OverrideCursor = Cursors.Wait;
        return true;
    }

    private void EndOperation()
    {
        _operationInProgress = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        Mouse.OverrideCursor = null;
        SetButtonsEnabled(true);
        UpdateCommandState();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        AnalyzeZonesButton.IsEnabled = enabled;
        LaunchCleanupButton.IsEnabled = enabled && _lastPreview.Any(preview => preview.HasEligibleCandidates);
        ViewCleanupReportButton.IsEnabled = enabled && _lastReport is not null;
        ReturnHomeButton.IsEnabled = enabled;
    }

    private void UpdateCommandState()
    {
        LaunchCleanupButton.IsEnabled = !_operationInProgress && _lastPreview.Any(preview => preview.HasEligibleCandidates);
        ViewCleanupReportButton.IsEnabled = !_operationInProgress && _lastReport is not null;
    }

    private void HandleProgress(CleanupProgress progress)
    {
        if (progress.TotalFiles > 0)
        {
            CleanupStatusText.Text = string.Join(" - ", new[]
            {
                $"{progress.Message} : {progress.ProcessedFiles}/{progress.TotalFiles} fichiers",
                $"{progress.DeletedFiles} supprimes",
                $"{FormatBytes(progress.DeletedBytes)} liberes",
                $"{progress.SkippedFiles + progress.ErrorFiles} ignores"
            });
            return;
        }

        CleanupStatusText.Text = progress.Percent.HasValue
            ? $"{progress.Step} - {progress.Percent.Value}%"
            : progress.Step;
    }

    private async Task<CleanupZoneDecision> AskZoneDecisionAsync(
        CleanupZonePreview preview,
        CancellationToken cancellationToken)
    {
        ShowValidation(preview);
        _activeDecision = new TaskCompletionSource<CleanupZoneDecision>();

        using var registration = cancellationToken.Register(() => CompleteDecision(CleanupZoneDecision.CancelAll));
        return await _activeDecision.Task.ConfigureAwait(true);
    }

    private void ShowValidation(CleanupZonePreview preview)
    {
        ValidationZoneTitleText.Text = "VALIDATION REQUISE";
        ValidationReasonText.Text = $"Zone : {preview.Definition.DisplayName}\n{preview.Definition.Description}";
        ValidationRootText.Text = LogicalRootLabel(preview.Definition.Id);
        ValidationMetricsText.Text = string.Join("\n", new[]
        {
            $"Taille : {FormatBytes(preview.EligibleBytes)}",
            $"Fichiers : {preview.EligibleFileCount}",
            $"Age minimal : {FormatAge(preview.Definition.MinimumAge)}",
            $"Risque : {RiskLabel(preview.Definition.RiskLevel)}"
        });
        ValidationEffectText.Text = $"{preview.Definition.Effect}\nSuppression definitive apres confirmation de cette zone uniquement.";
        ValidationNotTouchedText.Text = preview.Definition.NotTouched;
        ValidationWarningText.Text = preview.Definition.Warning;
        ZoneValidationOverlay.Visibility = Visibility.Visible;
        RequestVirgilState(VirgilCoreState.SensitiveAction, "VALIDATION");
        ValidationCore.SetState(VirgilCoreState.SensitiveAction);
        VirgilMessageRequested?.Invoke($"Validation requise.\nZone : {preview.Definition.DisplayName.ToLowerInvariant()}.\nAucune action ne sera effectuee sans confirmation.");
        ExecuteZoneButton.Focus();
    }

    private void CompleteDecision(CleanupZoneDecision decision)
    {
        if (_activeDecision is null)
        {
            return;
        }

        ValidationCore.SetState(VirgilCoreState.Idle);
        ZoneValidationOverlay.Visibility = Visibility.Collapsed;
        _activeDecision.TrySetResult(decision);
        _activeDecision = null;
    }

    private void RenderPreviews()
    {
        ZonesPanel.Children.Clear();

        foreach (var preview in _lastPreview.OrderBy(preview => preview.Definition.DisplayOrder))
        {
            ZonesPanel.Children.Add(CreateZoneCard(preview));
        }

        if (ZonesPanel.Children.Count == 0)
        {
            ZonesPanel.Children.Add(CreateTextCard("Aucune zone analysee."));
        }
    }

    private UIElement CreateZoneCard(CleanupZonePreview preview)
    {
        var card = new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 6, 0, 6)
        };

        var stack = new StackPanel();
        stack.Children.Add(CreateTitle(preview.Definition.DisplayName.ToUpperInvariant()));
        stack.Children.Add(CreateText(preview.Definition.Description));
        stack.Children.Add(CreateText($"Racine : {LogicalRootLabel(preview.Definition.Id)}"));
        stack.Children.Add(CreateText($"Risque : {RiskLabel(preview.Definition.RiskLevel)}"));
        stack.Children.Add(CreateText($"Examines : {preview.ExaminedFileCount} fichiers"));
        stack.Children.Add(CreateText($"Eligibles : {preview.EligibleFileCount} fichiers, {FormatBytes(preview.EligibleBytes)}"));
        stack.Children.Add(CreateText($"Exclus : {preview.ExcludedFileCount} fichiers"));
        stack.Children.Add(CreateText($"Anciennete minimale : {FormatAge(preview.Definition.MinimumAge)}"));
        stack.Children.Add(CreateText($"Avertissement : {preview.Definition.Warning}"));

        if (preview.Errors.Count > 0)
        {
            stack.Children.Add(CreateText("Erreurs lisibles : " + string.Join(" | ", preview.Errors.Take(3))));
        }

        card.Child = stack;
        return card;
    }

    private UIElement CreateTextCard(string message)
    {
        var card = new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 6, 0, 6)
        };

        card.Child = CreateText(message);
        return card;
    }

    private TextBlock CreateTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            Style = TryFindResource("VirgilHudSectionTitle") as Style,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private TextBlock CreateText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Style = TryFindResource("VirgilHudSecondaryText") as Style,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
    }

    private void AnnouncePreviewResult()
    {
        var totalBytes = _lastPreview.Sum(preview => preview.EligibleBytes);

        if (totalBytes == 0)
        {
            VirgilMessageRequested?.Invoke("Analyse terminee.\nAucun nettoyage necessaire.");
            return;
        }

        VirgilMessageRequested?.Invoke($"Previsualisation terminee.\n{_lastPreview.Count} zones analysees.\n{FormatBytes(totalBytes)} potentiellement recuperables.");
    }

    private void AnnounceSessionEnd(CleanupSessionReport report)
    {
        if (IsCancellationReport(report))
        {
            VirgilMessageRequested?.Invoke("Nettoyage interrompu.\nLes zones restantes n'ont pas ete modifiees.");
            return;
        }

        if (HasBlockingFailure(report))
        {
            VirgilMessageRequested?.Invoke("Nettoyage interrompu.\nRapport partiel disponible.");
            return;
        }

        VirgilMessageRequested?.Invoke($"Nettoyage termine.\nEspace libere : {FormatBytes(report.DeletedBytes)}.\nRapport disponible.");
    }

    private void AnnounceZoneResult(CleanupStepResult result)
    {
        if (result.Status == CleanupStepStatus.Expired)
        {
            VirgilMessageRequested?.Invoke($"Zone expiree : {result.Zone.DisplayName}.\nRelancez l'analyse.");
            return;
        }

        VirgilMessageRequested?.Invoke($"Zone traitee.\n{FormatBytes(result.DeletedBytes)} liberes.\n{result.ErrorFiles} fichiers verrouilles ignores.");
    }

    private void CompleteSessionState(CleanupSessionReport report)
    {
        CleanupStatusText.Text = $"Rapport pret : {report.DeletedFiles} fichiers, {FormatBytes(report.DeletedBytes)}.";

        if (IsCancellationReport(report))
        {
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            return;
        }

        if (HasBlockingFailure(report))
        {
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            return;
        }

        if (report.Errors.Count > 0 || report.ErrorFiles > 0)
        {
            RequestVirgilState(VirgilCoreState.Warning, "ATTENTION");
            return;
        }

        RequestVirgilState(VirgilCoreState.Success, "TERMINE");
    }

    private void ShowReport()
    {
        if (_lastReport is null)
        {
            VirgilMessageRequested?.Invoke("Aucun rapport nettoyage disponible.");
            return;
        }

        CleanupReportText.Text = FormatReport(_lastReport, _lastPreview.Count);
        CleanupReportOverlay.Visibility = Visibility.Visible;
        ReportCore.SetState(IsCancellationReport(_lastReport) || _lastReport.Errors.Count > 0 || _lastReport.ErrorFiles > 0
            ? VirgilCoreState.Warning
            : VirgilCoreState.Success);
        CloseCleanupReportButton.Focus();
    }

    private void HideReport()
    {
        ReportCore.SetState(VirgilCoreState.Idle);
        CleanupReportOverlay.Visibility = Visibility.Collapsed;
        ViewCleanupReportButton.Focus();
    }

    private static string FormatReport(CleanupSessionReport report, int analyzedZones)
    {
        var ignoredFiles = report.Results.Sum(result => result.SkippedFiles);
        var partialErrors = report.Errors.Count + report.Results.Sum(result => result.Errors.Count);
        var builder = new StringBuilder();
        builder.AppendLine($"Date : {report.StartedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Duree : {report.Duration.TotalSeconds:0.0} s");
        builder.AppendLine($"Zones analysees : {analyzedZones}");
        builder.AppendLine($"Zones executees : {CountExecutedZones(report)}");
        builder.AppendLine($"Zones passees : {report.SkippedZones}");
        builder.AppendLine($"Annulation : {(IsCancellationReport(report) ? "oui" : "non")}");
        builder.AppendLine($"Fichiers supprimes : {report.DeletedFiles}");
        builder.AppendLine($"Volume libere : {FormatBytes(report.DeletedBytes)}");
        builder.AppendLine($"Fichiers verrouilles : {report.ErrorFiles}");
        builder.AppendLine($"Fichiers ignores : {ignoredFiles}");
        builder.AppendLine($"Erreurs partielles : {partialErrors}");
        builder.AppendLine();

        foreach (var result in report.Results)
        {
            builder.AppendLine($"{result.Zone.DisplayName} : {StatusLabel(result.Status)}");
            builder.AppendLine($"  Supprimes : {result.DeletedFiles} - {FormatBytes(result.DeletedBytes)}");
            builder.AppendLine($"  Ignores : {result.SkippedFiles} - Erreurs : {result.ErrorFiles}");

            foreach (var error in result.Errors.Take(3))
            {
                builder.AppendLine($"  Erreur : {error}");
            }
        }

        if (report.Errors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Erreurs session :");

            foreach (var error in report.Errors.Take(5))
            {
                builder.AppendLine($"- {error}");
            }
        }

        return builder.ToString();
    }

    private void RequestVirgilState(VirgilCoreState state, string label)
    {
        CleanupMiniCore.SetState(state);
        VirgilStateRequested?.Invoke(state, label);
    }

    private static int CountExecutedZones(CleanupSessionReport report)
    {
        return report.Results.Count(result => result.Status is
            CleanupStepStatus.Completed or
            CleanupStepStatus.PartialFailure or
            CleanupStepStatus.Expired or
            CleanupStepStatus.Failed);
    }

    private static bool IsCancellationReport(CleanupSessionReport report)
    {
        return report.CancelledZones > 0 ||
            report.Errors.Any(error => error.Contains("annulee", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasBlockingFailure(CleanupSessionReport report)
    {
        return report.Results.Count == 0 &&
            report.Errors.Any(error => error.Contains("Erreur", StringComparison.OrdinalIgnoreCase));
    }

    private static string StatusLabel(CleanupStepStatus status)
    {
        return status switch
        {
            CleanupStepStatus.Completed => "terminee",
            CleanupStepStatus.Skipped => "passee",
            CleanupStepStatus.Cancelled => "annulee",
            CleanupStepStatus.Expired => "preview expiree",
            CleanupStepStatus.PartialFailure => "partielle",
            _ => "echec"
        };
    }

    private static string LogicalRootLabel(CleanupZoneId zoneId)
    {
        return zoneId switch
        {
            CleanupZoneId.UserTemporaryFiles => "%TEMP% utilisateur",
            CleanupZoneId.UserCrashDumps => "%LOCALAPPDATA%\\CrashDumps",
            CleanupZoneId.DirectXShaderCache => "%LOCALAPPDATA%\\D3DSCache",
            _ => "Zone autorisee"
        };
    }

    private static string RiskLabel(CleanupRiskLevel risk)
    {
        return risk switch
        {
            CleanupRiskLevel.Low => "faible",
            CleanupRiskLevel.Medium => "moyen",
            _ => "eleve"
        };
    }

    private static string FormatAge(TimeSpan age)
    {
        return age.TotalDays >= 1 ? $"{age.TotalDays:0} jours" : $"{age.TotalHours:0} heures";
    }

    private static string FormatBytes(long bytes)
    {
        return ScanRules.FormatBytes(bytes);
    }

    private enum CleanupZoneDecision
    {
        Execute,
        Skip,
        CancelAll
    }
}
