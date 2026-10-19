using Virgil.App.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Virgil.Core.Reports;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Contracts;

namespace Virgil.App.ViewModels;

public partial class UpdatesViewModel : ObservableObject
{
    private readonly IUpdateScanService _scanService;
    private readonly IUpdateExecutionService _executionService;
    private readonly WindowsUpdateStatusService _windowsUpdateStatusService;
    private readonly List<UpdateItem> _lastItems = new();
    private readonly Dictionary<string, UpdateItemViewModel> _selectionById = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReportHistoryService _reportHistoryService;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource<UpdateDecision>? _activeDecision;
    private UpdateScanReport? _lastScanReport;
    private UpdateSessionReport? _lastSessionReport;
    private bool _operationInProgress;

    private readonly IProcessRunner _processRunner;

    public UpdatesViewModel(IUpdateScanService scanService, IUpdateExecutionService executionService,
        WindowsUpdateStatusService windowsUpdateStatusService, IProcessRunner processRunner,
        IReportHistoryService reportHistoryService)
    {
        _scanService = scanService ?? throw new ArgumentNullException(nameof(scanService));
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _windowsUpdateStatusService = windowsUpdateStatusService ?? throw new ArgumentNullException(nameof(windowsUpdateStatusService));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _reportHistoryService = reportHistoryService ?? throw new ArgumentNullException(nameof(reportHistoryService));
        Entries.Add(new UpdateTextCardViewModel("Aucun scan lance."));
        RenderDrivers(new DriverInventoryReport());
    }

    public ObservableCollection<object> Entries { get; } = new();
    public ObservableCollection<string> DriverCards { get; } = new();
    [ObservableProperty] private string statusText = "Diagnostic pret. Aucune action automatique.";
    [ObservableProperty] private string overallStatus = "NON ANALYSE";
    [ObservableProperty] private string wingetStatus = "NON DETECTE";
    [ObservableProperty] private string windowsUpdateStatus = "OUVERTURE MANUELLE UNIQUEMENT";
    [ObservableProperty] private string driversStatus = "NON INVENTORIE";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isBusy;
    public bool IsIdle => !IsBusy;
    [ObservableProperty] private bool isRunSelectedEnabled;
    [ObservableProperty] private bool isReportButtonEnabled;
    [ObservableProperty] private bool isInstallDriversVisible;
    [ObservableProperty] private bool isInstallDriversEnabled;
    [ObservableProperty] private bool isValidationOpen;
    [ObservableProperty] private string validationTitle = "VALIDATION REQUISE";
    [ObservableProperty] private string validationDetails = string.Empty;
    [ObservableProperty] private string validationCommand = string.Empty;
    [ObservableProperty] private string validationSafety = string.Empty;
    [ObservableProperty] private string executeLabel = "INSTALLER";
    [ObservableProperty] private string skipLabel = "PASSER";
    [ObservableProperty] private bool isCancelSequenceVisible = true;
    [ObservableProperty] private bool isReportOpen;
    [ObservableProperty] private string reportText = string.Empty;
    [ObservableProperty] private VirgilCoreState miniCoreState = VirgilCoreState.Idle;
    [ObservableProperty] private VirgilCoreState validationCoreState = VirgilCoreState.Idle;

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public event EventHandler? ReportPersisted;

    public void CancelActiveOperation()
    {
        _operationCancellation?.Cancel();
        CompleteDecision(UpdateDecision.CancelAll);
    }

    public bool TryCloseOverlay()
    {
        if (IsReportOpen)
        {
            HideReport();
            return true;
        }

        if (IsValidationOpen)
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

    [RelayCommand]
    private Task ScanUpdatesAsync() => ScanUpdatesAsync(UpdateScanRequest.DeepPreview);

    [RelayCommand]
    private void InstallDrivers() => VirgilMessageRequested?.Invoke("Installation pilotes indisponible en V1.\nInventaire lecture seule uniquement.");
    [RelayCommand]
    private void ViewReport() => ShowReport();
    [RelayCommand]
    private void ReturnHome() => ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand]
    private void ExecuteDecision() => CompleteDecision(UpdateDecision.Execute);
    [RelayCommand]
    private void SkipDecision() => CompleteDecision(UpdateDecision.Skip);
    [RelayCommand]
    private void CancelDecision() => CompleteDecision(UpdateDecision.CancelAll);
    [RelayCommand]
    private void CloseReport() => HideReport();

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
            StatusText = "Scan annule.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Scan annule.\nAucune action effectuee.");
        }
        catch
        {
            StatusText = "Scan mises a jour indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Scan indisponible.\nAucune action effectuee.");
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand]
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
            StatusText = "Inventaire pilotes annule.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Inventaire annule.\nAucune action effectuee.");
        }
        catch
        {
            StatusText = "Inventaire pilotes indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Inventaire indisponible.\nAucune action effectuee.");
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand]
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
        var result = await _reportHistoryService.SaveAsync(report, CancellationToken.None).ConfigureAwait(true);
        if (!result.Success || !string.IsNullOrWhiteSpace(result.ReadableError))
        {
            VirgilMessageRequested?.Invoke(result.ReadableError ?? "Historique local indisponible. Rapport conserve en memoire.");
        }

        if (result.Success)
        {
            ReportPersisted?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
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
            var result = await _processRunner.LaunchAsync(new ProcessLaunchRequest(
                info.SettingsUri, Array.Empty<string>(), UseShellExecute: true), CancellationToken.None);
            VirgilMessageRequested?.Invoke(result.LaunchError is not null
                ? "Ouverture Windows Update impossible."
                : "Windows Update ouvert.\nVirgil ne lance aucune installation.");
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
        StatusText = status;
        SetButtonsEnabled(false);
        return true;
    }

    private void EndOperation()
    {
        _operationInProgress = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        SetButtonsEnabled(true);
        UpdateCommandState();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        IsBusy = !enabled;
        UpdateCommandState();
    }

    private void UpdateCommandState()
    {
        IsRunSelectedEnabled = !_operationInProgress && SelectedItems().Any();
        IsReportButtonEnabled = !_operationInProgress && HasAnyReport();
        IsInstallDriversVisible = _lastScanReport?.Drivers.CanInstallDrivers == true;
        IsInstallDriversEnabled = !_operationInProgress && IsInstallDriversVisible;
    }

    private void HandleProgress(string message)
    {
        StatusText = message;
    }

    private async Task<UpdateDecision> AskUpdateDecisionAsync(UpdateItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decision = new TaskCompletionSource<UpdateDecision>();
        _activeDecision = decision;
        ShowUpdateValidation(item);
        return await decision.Task.ConfigureAwait(true);
    }

    private Task<UpdateDecision> AskWindowsUpdateDecisionAsync(WindowsUpdateInformation info)
    {
        var decision = new TaskCompletionSource<UpdateDecision>();
        _activeDecision = decision;
        ShowWindowsUpdateValidation(info);
        return decision.Task;
    }

    private void ShowUpdateValidation(UpdateItem item)
    {
        ValidationTitle = item.RiskLevel == UpdateRiskLevel.Sensitive
            ? "VALIDATION SENSIBLE"
            : "VALIDATION REQUISE";
        ValidationDetails = string.Join("\n", new[]
        {
            $"Application : {item.Name}",
            $"ID : {item.Id}",
            $"Version : {item.InstalledVersion} -> {item.AvailableVersion}",
            $"Source : {SourceLabel(item.Source)}",
            $"Risque : {RiskLabel(item.RiskLevel)}",
            $"Motif : {item.RiskReason}"
        });
        ValidationCommand = item.CommandPreview is null
            ? "Aucune commande executable preparee."
            : FormatCommand(item.CommandPreview);
        ValidationSafety =
            "Validation individuelle obligatoire.\nAucun --all, aucune installation pilote, aucun redemarrage, aucune elevation demandee par Virgil.";
        ExecuteLabel = item.RiskLevel == UpdateRiskLevel.Sensitive ? "JE CONFIRME" : "INSTALLER";
        SkipLabel = "PASSER";
        IsCancelSequenceVisible = true;
        ValidationCoreState = item.RiskLevel == UpdateRiskLevel.Sensitive
            ? VirgilCoreState.SensitiveAction
            : VirgilCoreState.Warning;
        RequestVirgilState(VirgilCoreState.SensitiveAction, "VALIDATION");
        IsValidationOpen = true;
    }

    private void ShowWindowsUpdateValidation(WindowsUpdateInformation info)
    {
        ValidationTitle = "OUVRIR WINDOWS UPDATE";
        ValidationDetails = string.Join("\n", new[]
        {
            info.Status,
            $"Redemarrage en attente : {(info.PendingRebootDetected ? "oui" : "non")}",
            $"URI : {info.SettingsUri}"
        });
        ValidationCommand = "Ouverture des Parametres Windows uniquement.";
        ValidationSafety =
            "Virgil n'appuie sur aucun bouton Windows Update.\nAucune recherche, installation ou redemarrage n'est declenche.";
        ExecuteLabel = "OUVRIR";
        SkipLabel = "ANNULER";
        IsCancelSequenceVisible = false;
        ValidationCoreState = VirgilCoreState.Warning;
        IsValidationOpen = true;
    }

    private void CompleteDecision(UpdateDecision decision)
    {
        if (_activeDecision is null)
        {
            return;
        }

        ValidationCoreState = VirgilCoreState.Idle;
        IsValidationOpen = false;
        var pending = _activeDecision;
        _activeDecision = null;
        pending.TrySetResult(decision);
    }

    private void RenderScanReport(UpdateScanReport report)
    {
        OverallStatus = report.OverallStatus.ToUpperInvariant();
        WingetStatus = report.Winget.IsAvailable
            ? report.Winget.Message
            : "WinGet non detecte.";
        WindowsUpdateStatus = report.WindowsUpdate.Status;
        DriversStatus = report.Drivers.WasAnalyzed
            ? $"{report.Drivers.Drivers.Count} pilotes inventories. Installation masquee."
            : "Non inventorie.";

        RenderItems();
        RenderDrivers(report.Drivers);
        UpdateCommandState();
    }

    private void RenderItems()
    {
        Entries.Clear();
        _selectionById.Clear();
        if (_lastItems.Count == 0)
        {
            Entries.Add(new UpdateTextCardViewModel("Aucune mise a jour applicative previsualisee."));
            return;
        }
        foreach (var item in _lastItems.OrderBy(item => item.RiskLevel).ThenBy(item => item.Name))
        {
            var card = new UpdateItemViewModel(item, UpdateCommandState);
            _selectionById[item.Id] = card;
            Entries.Add(card);
        }
    }

    private void RenderDrivers(DriverInventoryReport drivers)
    {
        DriverCards.Clear();

        if (!drivers.WasAnalyzed)
        {
            DriverCards.Add("Inventaire disponible apres analyse approfondie ou examen manuel.");
            return;
        }

        DriverCards.Add(drivers.InstallButtonVisibilityReason);

        foreach (var error in drivers.Errors.Take(3))
        {
            DriverCards.Add("Erreur lisible : " + error);
        }

        foreach (var driver in drivers.Drivers.Take(8))
        {
            DriverCards.Add(string.Join("\n", new[]
            {
                string.IsNullOrWhiteSpace(driver.PublishedName) ? "Pilote" : driver.PublishedName,
                $"Fournisseur : {TextOrUnknown(driver.Provider)}",
                $"Classe : {TextOrUnknown(driver.ClassName)}",
                $"Version : {TextOrUnknown(driver.Version)}",
                $"Signataire : {TextOrUnknown(driver.Signer)}"
            }));
        }
    }

    private IEnumerable<UpdateItem> SelectedItems()
    {
        return _lastItems.Where(item =>
            _selectionById.TryGetValue(item.Id, out var card) &&
            card.IsSelected &&
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
            StatusText = "Sequence annulee.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            return;
        }

        if (report.FailedCount > 0 || report.Errors.Count > 0)
        {
            StatusText = "Sequence terminee avec erreurs.";
            RequestVirgilState(VirgilCoreState.Warning, "ATTENTION");
            return;
        }

        StatusText = "Sequence terminee.";
        RequestVirgilState(VirgilCoreState.Success, "TERMINE");
    }

    private void ShowReport()
    {
        if (!HasAnyReport())
        {
            VirgilMessageRequested?.Invoke("Aucun rapport mises a jour disponible.");
            return;
        }

        ReportText = FormatReport(_lastScanReport, _lastSessionReport);
        IsReportOpen = true;
    }

    private void HideReport()
    {
        IsReportOpen = false;
    }

    private bool HasAnyReport()
    {
        return _lastScanReport is not null || _lastSessionReport is not null;
    }

    private void RequestVirgilState(VirgilCoreState state, string label)
    {
        MiniCoreState = state;
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

    internal static string FormatCommand(UpdateCommandPreview command)
    {
        return command.ExecutablePath + " " + string.Join(" ", command.Arguments.Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        return argument.Contains(' ') ? $"\"{argument}\"" : argument;
    }

    internal static string SourceLabel(UpdateSource source)
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

    internal static string RiskLabel(UpdateRiskLevel risk)
    {
        return risk switch
        {
            UpdateRiskLevel.Safe => "faible",
            UpdateRiskLevel.Sensitive => "sensible",
            UpdateRiskLevel.CriticalInformationOnly => "information uniquement",
            _ => "validation requise"
        };
    }

    internal static string StatusLabel(UpdateItemStatus status)
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
