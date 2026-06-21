using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Virgil.App.Controls;
using Virgil.Core.Reports;
using Virgil.Core.Updates;
using Virgil.Domain;

namespace Virgil.App.Views;

public partial class UpdatesView : UserControl
{
    private readonly IUpdateScanService _scanService;
    private readonly IUpdateExecutionService _executionService;
    private readonly WindowsUpdateStatusService _windowsUpdateStatusService;
    private readonly List<UpdateItem> _lastItems = new();
    private readonly Dictionary<string, CheckBox> _selectionById = new(StringComparer.OrdinalIgnoreCase);
    private IReportHistoryService? _reportHistoryService;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource<UpdateDecision>? _activeDecision;
    private UpdateScanReport? _lastScanReport;
    private UpdateSessionReport? _lastSessionReport;
    private bool _operationInProgress;

    public UpdatesView()
        : this(new WingetUpdateScanService(), new WingetUpdateExecutionService(), new WindowsUpdateStatusService())
    {
    }

    public UpdatesView(
        IUpdateScanService scanService,
        IUpdateExecutionService executionService,
        WindowsUpdateStatusService windowsUpdateStatusService)
    {
        InitializeComponent();
        _scanService = scanService;
        _executionService = executionService;
        _windowsUpdateStatusService = windowsUpdateStatusService;
    }

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public void ConfigureReportHistory(IReportHistoryService reportHistoryService)
    {
        _reportHistoryService = reportHistoryService;
    }

    public void FocusScanButton()
    {
        ScanUpdatesButton.Focus();
    }

    public void CancelActiveOperation()
    {
        _operationCancellation?.Cancel();
        CompleteDecision(UpdateDecision.CancelAll);
    }

    public bool TryCloseOverlay()
    {
        if (UpdatesReportOverlay.Visibility == Visibility.Visible)
        {
            HideReport();
            return true;
        }

        if (UpdateValidationOverlay.Visibility == Visibility.Visible)
        {
            CompleteDecision(UpdateDecision.Skip);
            return true;
        }

        if (_operationInProgress)
        {
            CancelActiveOperation();
            VirgilMessageRequested?.Invoke("Annulation demandee.\nSequence en cours d'arret.");
            return true;
        }

        return false;
    }

    private async void ScanUpdates_Click(object sender, RoutedEventArgs e)
    {
        await ScanUpdatesAsync(UpdateScanRequest.DeepPreview);
    }

    private async void RunSelected_Click(object sender, RoutedEventArgs e)
    {
        await RunSelectedAsync();
    }

    private async void OpenWindowsUpdate_Click(object sender, RoutedEventArgs e)
    {
        await OpenWindowsUpdateAsync();
    }

    private async void InspectDrivers_Click(object sender, RoutedEventArgs e)
    {
        await InspectDriversAsync();
    }

    private void InstallDrivers_Click(object sender, RoutedEventArgs e)
    {
        VirgilMessageRequested?.Invoke("Installation pilotes indisponible en V1.\nInventaire lecture seule uniquement.");
    }

    private void ViewUpdatesReport_Click(object sender, RoutedEventArgs e)
    {
        ShowReport();
    }

    private void ReturnHome_Click(object sender, RoutedEventArgs e)
    {
        ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExecuteUpdate_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(UpdateDecision.Execute);
    }

    private void SkipUpdate_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(UpdateDecision.Skip);
    }

    private void CancelSequence_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(UpdateDecision.CancelAll);
    }

    private void CloseUpdatesReport_Click(object sender, RoutedEventArgs e)
    {
        HideReport();
    }

    private async Task ScanUpdatesAsync(UpdateScanRequest request)
    {
        if (!BeginOperation("Scan mises a jour en cours."))
        {
            return;
        }

        try
        {
            RequestVirgilState(VirgilCoreState.Scanning, "MISES A JOUR");
            VirgilMessageRequested?.Invoke("Scan mises a jour lance.\nAucune installation.");

            var report = await _scanService
                .ScanAsync(request, new Progress<string>(HandleProgress), _operationCancellation!.Token)
                .ConfigureAwait(true);

            _lastScanReport = report;
            _lastItems.Clear();
            _lastItems.AddRange(report.Items);
            await PersistReportAsync(ReportMapper.FromUpdateScan(report));

            RenderScanReport(report);
            RequestVirgilState(report.Errors.Count == 0 ? VirgilCoreState.Success : VirgilCoreState.Warning, "PRET");
            AnnounceScan(report);
        }
        catch (OperationCanceledException)
        {
            UpdatesStatusText.Text = "Scan annule.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Scan annule.\nAucune action effectuee.");
        }
        catch
        {
            UpdatesStatusText.Text = "Scan mises a jour indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Scan indisponible.\nAucune action effectuee.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task InspectDriversAsync()
    {
        if (!BeginOperation("Inventaire pilotes en cours."))
        {
            return;
        }

        try
        {
            RequestVirgilState(VirgilCoreState.Scanning, "PILOTES");
            var request = new UpdateScanRequest
            {
                Scope = UpdateScanScope.DeepPreview,
                IncludeApplicationUpdates = false,
                IncludeDriverInventory = true
            };

            var report = await _scanService
                .ScanAsync(request, new Progress<string>(HandleProgress), _operationCancellation!.Token)
                .ConfigureAwait(true);

            _lastScanReport = MergeDriverReport(_lastScanReport, report);
            await PersistReportAsync(ReportMapper.FromUpdateScan(_lastScanReport));
            RenderScanReport(_lastScanReport);
            RequestVirgilState(report.Errors.Count == 0 ? VirgilCoreState.Success : VirgilCoreState.Warning, "PILOTES");
            VirgilMessageRequested?.Invoke("Inventaire pilotes termine.\nLecture seule.");
        }
        catch (OperationCanceledException)
        {
            UpdatesStatusText.Text = "Inventaire pilotes annule.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Inventaire annule.\nAucune action effectuee.");
        }
        catch
        {
            UpdatesStatusText.Text = "Inventaire pilotes indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Inventaire indisponible.\nAucune action effectuee.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RunSelectedAsync()
    {
        var selectedItems = SelectedItems().ToList();
        if (selectedItems.Count == 0)
        {
            VirgilMessageRequested?.Invoke("Aucune application selectionnee.");
            return;
        }

        if (!BeginOperation("Sequence guidee en attente de validation."))
        {
            return;
        }

        var startedAt = DateTimeOffset.Now;
        var results = new List<UpdateExecutionResult>();
        var errors = new List<string>();

        try
        {
            RequestVirgilState(VirgilCoreState.SensitiveAction, "VALIDATION");

            foreach (var item in selectedItems)
            {
                _operationCancellation!.Token.ThrowIfCancellationRequested();

                var decision = await AskUpdateDecisionAsync(item, _operationCancellation.Token).ConfigureAwait(true);
                if (decision == UpdateDecision.CancelAll)
                {
                    results.Add(_executionService.Cancel(item));
                    errors.Add("Sequence annulee par l'utilisateur.");
                    break;
                }

                if (decision == UpdateDecision.Skip)
                {
                    results.Add(_executionService.Skip(item));
                    SetItemStatus(item.Id, UpdateItemStatus.Skipped);
                    VirgilMessageRequested?.Invoke("Mise a jour passee.\nAucune action.");
                    continue;
                }

                RequestVirgilState(VirgilCoreState.Executing, "INSTALLATION");
                var result = await _executionService
                    .ExecuteAsync(item, _operationCancellation.Token)
                    .ConfigureAwait(true);

                results.Add(result);
                SetItemStatus(item.Id, result.Status);
                VirgilMessageRequested?.Invoke(result.Status == UpdateItemStatus.Completed
                    ? "Mise a jour terminee.\nSequence continue."
                    : "Mise a jour non terminee.\nRapport disponible.");
            }
        }
        catch (OperationCanceledException)
        {
            errors.Add("Sequence annulee.");
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
        }
        catch
        {
            errors.Add("Erreur pendant la sequence guidee.");
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
        }
        finally
        {
            _lastSessionReport = _executionService.CreateReport(startedAt, results, errors);
            await PersistReportAsync(ReportMapper.FromUpdateSession(_lastSessionReport));
            RenderItems();
            EndOperation();
            CompleteSequenceState(_lastSessionReport);
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

    private async Task OpenWindowsUpdateAsync()
    {
        if (_operationInProgress)
        {
            VirgilMessageRequested?.Invoke("Operation deja en cours.");
            return;
        }

        var info = _lastScanReport?.WindowsUpdate ?? _windowsUpdateStatusService.ReadStatus();
        var decision = await AskWindowsUpdateDecisionAsync(info).ConfigureAwait(true);
        if (decision != UpdateDecision.Execute)
        {
            VirgilMessageRequested?.Invoke("Windows Update non ouvert.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = info.SettingsUri,
                UseShellExecute = true
            });
            VirgilMessageRequested?.Invoke("Windows Update ouvert.\nVirgil ne lance aucune installation.");
        }
        catch
        {
            VirgilMessageRequested?.Invoke("Ouverture Windows Update impossible.");
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
        UpdatesStatusText.Text = status;
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
        ScanUpdatesButton.IsEnabled = enabled;
        OpenWindowsUpdateButton.IsEnabled = enabled;
        InspectDriversButton.IsEnabled = enabled;
        ViewUpdatesReportButton.IsEnabled = enabled && HasAnyReport();
        ReturnHomeButton.IsEnabled = enabled;
        RunSelectedButton.IsEnabled = enabled && SelectedItems().Any();
        InstallDriversButton.IsEnabled = enabled && _lastScanReport?.Drivers.CanInstallDrivers == true;
    }

    private void UpdateCommandState()
    {
        RunSelectedButton.IsEnabled = !_operationInProgress && SelectedItems().Any();
        ViewUpdatesReportButton.IsEnabled = !_operationInProgress && HasAnyReport();
        InstallDriversButton.Visibility = _lastScanReport?.Drivers.CanInstallDrivers == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        InstallDriversButton.IsEnabled = !_operationInProgress && _lastScanReport?.Drivers.CanInstallDrivers == true;
    }

    private void HandleProgress(string message)
    {
        UpdatesStatusText.Text = message;
    }

    private async Task<UpdateDecision> AskUpdateDecisionAsync(UpdateItem item, CancellationToken cancellationToken)
    {
        ShowUpdateValidation(item);
        _activeDecision = new TaskCompletionSource<UpdateDecision>();

        using var registration = cancellationToken.Register(() =>
            Dispatcher.BeginInvoke(new Action(() => CompleteDecision(UpdateDecision.CancelAll))));

        return await _activeDecision.Task.ConfigureAwait(true);
    }

    private Task<UpdateDecision> AskWindowsUpdateDecisionAsync(WindowsUpdateInformation info)
    {
        ShowWindowsUpdateValidation(info);
        _activeDecision = new TaskCompletionSource<UpdateDecision>();
        return _activeDecision.Task;
    }

    private void ShowUpdateValidation(UpdateItem item)
    {
        ValidationTitleText.Text = item.RiskLevel == UpdateRiskLevel.Sensitive
            ? "VALIDATION SENSIBLE"
            : "VALIDATION REQUISE";
        ValidationDetailsText.Text = string.Join("\n", new[]
        {
            $"Application : {item.Name}",
            $"ID : {item.Id}",
            $"Version : {item.InstalledVersion} -> {item.AvailableVersion}",
            $"Source : {SourceLabel(item.Source)}",
            $"Risque : {RiskLabel(item.RiskLevel)}",
            $"Motif : {item.RiskReason}"
        });
        ValidationCommandText.Text = item.CommandPreview is null
            ? "Aucune commande executable preparee."
            : FormatCommand(item.CommandPreview);
        ValidationSafetyText.Text =
            "Validation individuelle obligatoire.\nAucun --all, aucune installation pilote, aucun redemarrage, aucune elevation demandee par Virgil.";
        ExecuteUpdateButton.Content = item.RiskLevel == UpdateRiskLevel.Sensitive ? "JE CONFIRME" : "INSTALLER";
        SkipUpdateButton.Content = "PASSER";
        CancelSequenceButton.Visibility = Visibility.Visible;
        UpdateValidationOverlay.Visibility = Visibility.Visible;
        ValidationCore.SetState(item.RiskLevel == UpdateRiskLevel.Sensitive
            ? VirgilCoreState.SensitiveAction
            : VirgilCoreState.Warning);
        RequestVirgilState(VirgilCoreState.SensitiveAction, "VALIDATION");
        ExecuteUpdateButton.Focus();
    }

    private void ShowWindowsUpdateValidation(WindowsUpdateInformation info)
    {
        ValidationTitleText.Text = "OUVRIR WINDOWS UPDATE";
        ValidationDetailsText.Text = string.Join("\n", new[]
        {
            info.Status,
            $"Redemarrage en attente : {(info.PendingRebootDetected ? "oui" : "non")}",
            $"URI : {info.SettingsUri}"
        });
        ValidationCommandText.Text = "Ouverture des Parametres Windows uniquement.";
        ValidationSafetyText.Text =
            "Virgil n'appuie sur aucun bouton Windows Update.\nAucune recherche, installation ou redemarrage n'est declenche.";
        ExecuteUpdateButton.Content = "OUVRIR";
        SkipUpdateButton.Content = "ANNULER";
        CancelSequenceButton.Visibility = Visibility.Collapsed;
        UpdateValidationOverlay.Visibility = Visibility.Visible;
        ValidationCore.SetState(VirgilCoreState.Warning);
        ExecuteUpdateButton.Focus();
    }

    private void CompleteDecision(UpdateDecision decision)
    {
        if (_activeDecision is null)
        {
            return;
        }

        ValidationCore.SetState(VirgilCoreState.Idle);
        UpdateValidationOverlay.Visibility = Visibility.Collapsed;
        _activeDecision.TrySetResult(decision);
        _activeDecision = null;
    }

    private void RenderScanReport(UpdateScanReport report)
    {
        OverallStatusText.Text = report.OverallStatus.ToUpperInvariant();
        WingetStatusText.Text = report.Winget.IsAvailable
            ? report.Winget.Message
            : "WinGet non detecte.";
        WindowsUpdateStatusText.Text = report.WindowsUpdate.Status;
        DriversStatusText.Text = report.Drivers.WasAnalyzed
            ? $"{report.Drivers.Drivers.Count} pilotes inventories. Installation masquee."
            : "Non inventorie.";

        RenderItems();
        RenderDrivers(report.Drivers);
        UpdateCommandState();
    }

    private void RenderItems()
    {
        UpdatesListPanel.Children.Clear();
        _selectionById.Clear();

        if (_lastItems.Count == 0)
        {
            UpdatesListPanel.Children.Add(CreateTextCard("Aucune mise a jour applicative previsualisee."));
            return;
        }

        foreach (var item in _lastItems.OrderBy(item => item.RiskLevel).ThenBy(item => item.Name))
        {
            UpdatesListPanel.Children.Add(CreateUpdateCard(item));
        }
    }

    private UIElement CreateUpdateCard(UpdateItem item)
    {
        var card = new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var stack = new StackPanel();
        var checkBox = new CheckBox
        {
            Content = $"{item.Name} - {item.AvailableVersion}",
            IsEnabled = item.Status == UpdateItemStatus.Available &&
                item.RiskLevel != UpdateRiskLevel.CriticalInformationOnly,
            Margin = new Thickness(0, 0, 0, 8)
        };
        checkBox.SetResourceReference(ForegroundProperty, "App.TextPrimaryBrush");
        checkBox.Checked += (_, _) => UpdateCommandState();
        checkBox.Unchecked += (_, _) => UpdateCommandState();
        _selectionById[item.Id] = checkBox;

        stack.Children.Add(checkBox);
        stack.Children.Add(CreateText($"ID : {item.Id}"));
        stack.Children.Add(CreateText($"Version installee : {item.InstalledVersion}"));
        stack.Children.Add(CreateText($"Version disponible : {item.AvailableVersion}"));
        stack.Children.Add(CreateText($"Source : {SourceLabel(item.Source)}"));
        stack.Children.Add(CreateText($"Risque : {RiskLabel(item.RiskLevel)} - {item.RiskReason}"));
        stack.Children.Add(CreateText($"Statut : {StatusLabel(item.Status)}"));

        if (item.CommandPreview is not null)
        {
            stack.Children.Add(CreateText($"Commande : {FormatCommand(item.CommandPreview)}"));
        }

        card.Child = stack;
        return card;
    }

    private void RenderDrivers(DriverInventoryReport drivers)
    {
        DriversListPanel.Children.Clear();

        if (!drivers.WasAnalyzed)
        {
            DriversListPanel.Children.Add(CreateTextCard("Inventaire disponible apres analyse approfondie ou examen manuel."));
            return;
        }

        DriversListPanel.Children.Add(CreateTextCard(drivers.InstallButtonVisibilityReason));

        foreach (var error in drivers.Errors.Take(3))
        {
            DriversListPanel.Children.Add(CreateTextCard("Erreur lisible : " + error));
        }

        foreach (var driver in drivers.Drivers.Take(8))
        {
            DriversListPanel.Children.Add(CreateTextCard(string.Join("\n", new[]
            {
                string.IsNullOrWhiteSpace(driver.PublishedName) ? "Pilote" : driver.PublishedName,
                $"Fournisseur : {TextOrUnknown(driver.Provider)}",
                $"Classe : {TextOrUnknown(driver.ClassName)}",
                $"Version : {TextOrUnknown(driver.Version)}",
                $"Signataire : {TextOrUnknown(driver.Signer)}"
            })));
        }
    }

    private UIElement CreateTextCard(string text)
    {
        var card = new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 0, 0, 8)
        };
        card.Child = CreateText(text);
        return card;
    }

    private TextBlock CreateText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Style = TryFindResource("VirgilHudSecondaryText") as Style,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        };
    }

    private IEnumerable<UpdateItem> SelectedItems()
    {
        return _lastItems.Where(item =>
            _selectionById.TryGetValue(item.Id, out var checkBox) &&
            checkBox.IsChecked == true &&
            item.Status == UpdateItemStatus.Available);
    }

    private void SetItemStatus(string id, UpdateItemStatus status)
    {
        var index = _lastItems.FindIndex(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _lastItems[index] = _lastItems[index] with { Status = status };
        }
    }

    private void AnnounceScan(UpdateScanReport report)
    {
        if (!report.Winget.IsAvailable)
        {
            VirgilMessageRequested?.Invoke("WinGet non detecte.\nWindows Update reste disponible.");
            return;
        }

        VirgilMessageRequested?.Invoke(report.Items.Count == 0
            ? "Scan termine.\nAucune mise a jour applicative detectee."
            : $"Scan termine.\n{report.Items.Count} mises a jour a verifier.");
    }

    private void CompleteSequenceState(UpdateSessionReport report)
    {
        if (report.WasCancelled)
        {
            UpdatesStatusText.Text = "Sequence annulee.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            return;
        }

        if (report.FailedCount > 0 || report.Errors.Count > 0)
        {
            UpdatesStatusText.Text = "Sequence terminee avec erreurs.";
            RequestVirgilState(VirgilCoreState.Warning, "ATTENTION");
            return;
        }

        UpdatesStatusText.Text = "Sequence terminee.";
        RequestVirgilState(VirgilCoreState.Success, "TERMINE");
    }

    private void ShowReport()
    {
        if (!HasAnyReport())
        {
            VirgilMessageRequested?.Invoke("Aucun rapport mises a jour disponible.");
            return;
        }

        UpdatesReportText.Text = FormatReport(_lastScanReport, _lastSessionReport);
        UpdatesReportOverlay.Visibility = Visibility.Visible;
        CloseUpdatesReportButton.Focus();
    }

    private void HideReport()
    {
        UpdatesReportOverlay.Visibility = Visibility.Collapsed;
        ViewUpdatesReportButton.Focus();
    }

    private bool HasAnyReport()
    {
        return _lastScanReport is not null || _lastSessionReport is not null;
    }

    private void RequestVirgilState(VirgilCoreState state, string label)
    {
        UpdatesMiniCore.SetState(state);
        VirgilStateRequested?.Invoke(state, label);
    }

    private static UpdateScanReport MergeDriverReport(UpdateScanReport? existing, UpdateScanReport driverReport)
    {
        if (existing is null)
        {
            return driverReport;
        }

        return existing with
        {
            WindowsUpdate = driverReport.WindowsUpdate,
            Drivers = driverReport.Drivers,
            Errors = existing.Errors.Concat(driverReport.Errors).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Recommendations = existing.Recommendations.Concat(driverReport.Recommendations).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static string FormatReport(UpdateScanReport? scanReport, UpdateSessionReport? sessionReport)
    {
        var builder = new StringBuilder();

        if (scanReport is not null)
        {
            builder.AppendLine($"Date scan : {scanReport.CapturedAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Etat global : {scanReport.OverallStatus}");
            builder.AppendLine($"WinGet : {(scanReport.Winget.IsAvailable ? scanReport.Winget.Message : "non detecte")}");
            builder.AppendLine($"Applications : {scanReport.Items.Count}");
            builder.AppendLine($"Faible risque : {scanReport.SafeCount}");
            builder.AppendLine($"Validation : {scanReport.ValidationRequiredCount}");
            builder.AppendLine($"Sensibles : {scanReport.SensitiveCount}");
            builder.AppendLine($"Windows Update : {scanReport.WindowsUpdate.Status}");
            builder.AppendLine($"Redemarrage en attente : {(scanReport.WindowsUpdate.PendingRebootDetected ? "oui" : "non")}");
            builder.AppendLine($"Pilotes inventories : {scanReport.Drivers.Drivers.Count}");
            builder.AppendLine($"Bouton pilotes : {scanReport.Drivers.InstallButtonVisibilityReason}");
            AppendList(builder, "Recommandations", scanReport.Recommendations);
            AppendList(builder, "Erreurs lisibles", scanReport.Errors);
            builder.AppendLine();
        }

        if (sessionReport is not null)
        {
            builder.AppendLine($"Date sequence : {sessionReport.StartedAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Duree : {sessionReport.Duration.TotalSeconds:0.0} s");
            builder.AppendLine($"Terminees : {sessionReport.CompletedCount}");
            builder.AppendLine($"Passees : {sessionReport.SkippedCount}");
            builder.AppendLine($"Echecs : {sessionReport.FailedCount}");
            builder.AppendLine($"Annulation : {(sessionReport.WasCancelled ? "oui" : "non")}");

            foreach (var result in sessionReport.Results)
            {
                builder.AppendLine($"{result.Item.Name} : {StatusLabel(result.Status)}");
                if (!string.IsNullOrWhiteSpace(result.UserMessage))
                {
                    builder.AppendLine($"  {result.UserMessage}");
                }
            }

            AppendList(builder, "Erreurs sequence", sessionReport.Errors);
        }

        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine(title + " :");

        if (values.Count == 0)
        {
            builder.AppendLine("- Aucune");
            return;
        }

        foreach (var value in values.Take(8))
        {
            builder.AppendLine("- " + value);
        }
    }

    private static string FormatCommand(UpdateCommandPreview command)
    {
        return command.ExecutablePath + " " + string.Join(" ", command.Arguments.Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        return argument.Contains(' ') ? $"\"{argument}\"" : argument;
    }

    private static string SourceLabel(UpdateSource source)
    {
        return source switch
        {
            UpdateSource.MicrosoftStore => "Microsoft Store",
            UpdateSource.WindowsUpdate => "Windows Update",
            UpdateSource.Driver => "Pilote",
            UpdateSource.FirmwareInformation => "Firmware information",
            _ => "WinGet"
        };
    }

    private static string RiskLabel(UpdateRiskLevel risk)
    {
        return risk switch
        {
            UpdateRiskLevel.Safe => "faible",
            UpdateRiskLevel.Sensitive => "sensible",
            UpdateRiskLevel.CriticalInformationOnly => "information uniquement",
            _ => "validation requise"
        };
    }

    private static string StatusLabel(UpdateItemStatus status)
    {
        return status switch
        {
            UpdateItemStatus.Completed => "terminee",
            UpdateItemStatus.Skipped => "passee",
            UpdateItemStatus.Failed => "echec",
            UpdateItemStatus.Cancelled => "annulee",
            UpdateItemStatus.InformationOnly => "information uniquement",
            _ => "disponible"
        };
    }

    private static string TextOrUnknown(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "N/A" : value;
    }

    private enum UpdateDecision
    {
        Execute,
        Skip,
        CancelAll
    }
}
