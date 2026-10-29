using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
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
    private readonly IConfirmationService _confirmationService;
    private readonly IReportHistoryService _reportHistoryService;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource<UpdateDecision>? _activeDecision;
    private UpdateScanReport? _lastScanReport;
    private UpdateSessionReport? _lastSessionReport;
    private bool _operationInProgress;

    private readonly IProcessRunner _processRunner;

    public UpdatesViewModel(IUpdateScanService scanService, IUpdateExecutionService executionService,
        WindowsUpdateStatusService windowsUpdateStatusService, IProcessRunner processRunner,
        IReportHistoryService reportHistoryService, IConfirmationService confirmationService,
        IUpdateSequenceService? sequenceService = null, WingetClientModuleService? wingetModuleService = null)
    {
        Operation = new LongOperationViewModel(CancelActiveOperation);
        _sequenceService = sequenceService;
        _wingetModuleService = wingetModuleService;
        _scanService = scanService ?? throw new ArgumentNullException(nameof(scanService));
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _windowsUpdateStatusService = windowsUpdateStatusService ?? throw new ArgumentNullException(nameof(windowsUpdateStatusService));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _confirmationService = confirmationService;
        _reportHistoryService = reportHistoryService ?? throw new ArgumentNullException(nameof(reportHistoryService));
        Entries.Add(new UpdateTextCardViewModel("Aucun scan lancé."));
        RenderDrivers(new DriverInventoryReport());
        UpdateCommandState();
    }

    public ObservableCollection<object> Entries { get; } = new();
    public ObservableCollection<string> DriverCards { get; } = new();
    [ObservableProperty] private string statusText = "Diagnostic prêt. Aucune action automatique.";
    [ObservableProperty] private string overallStatus = "NON ANALYSÉ";
    [ObservableProperty] private string wingetStatus = "NON DÉTECTÉ";
    [ObservableProperty] private string windowsUpdateStatus = "OUVERTURE MANUELLE UNIQUEMENT";
    [ObservableProperty] private string driversStatus = "NON INVENTORIÉ";
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

    public LongOperationViewModel Operation { get; }
    public bool IsCancelling => Operation.IsCancelling;

    public void CancelActiveOperation()
    {
        Operation.AcknowledgeCancellation();
        OnPropertyChanged(nameof(IsCancelling));
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
            VirgilMessageRequested?.Invoke("Annulation demandée.\nSéquence en cours d'arrêt.");
            return true;
        }

        return false;
    }

    [RelayCommand]
    private Task ScanUpdatesAsync() => ScanUpdatesAsync(UpdateScanRequest.DeepPreview);

    [RelayCommand]
    private void InstallDrivers() => VirgilMessageRequested?.Invoke("Les pilotes disponibles sont proposés dans « Tout mettre à jour ».\nIls restent facultatifs et décochés par défaut.");
    [RelayCommand]
    private void ViewReport() => ShowReport();
    [RelayCommand]
    private void ReturnHome() => ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand]
    private void ExecuteDecision()
    {
        if (IsSequencePreview)
        {
            _confirmedSequenceSteps = SequenceChoices.Where(choice => choice.IsSelected && choice.CanSelect).Select(choice => choice.Step).ToArray();
            if (_confirmedSequenceSteps.Count == 0)
            {
                VirgilMessageRequested?.Invoke("Sélectionnez au moins un composant.");
                return;
            }
        }
        CompleteDecision(UpdateDecision.Execute);
    }
    [RelayCommand]
    private void SkipDecision() => CompleteDecision(UpdateDecision.Skip);
    [RelayCommand]
    private void CancelDecision() => CompleteDecision(UpdateDecision.CancelAll);
    [RelayCommand]
    private void CloseReport() => HideReport();

    private async Task ScanUpdatesAsync(UpdateScanRequest request)
    {
        if (!BeginOperation("Scan mises à jour en cours."))
        {
            return;
        }

        try
        {
            RequestVirgilState(VirgilCoreState.Scanning, "MISES À JOUR");
            VirgilMessageRequested?.Invoke("Scan mises à jour lancé.\nAucune installation.");

            var report = await _scanService
                .ScanAsync(request, CreateTextProgress(), _operationCancellation!.Token)
                .WaitAsync(_operationCancellation.Token).ConfigureAwait(true);

            _operationCancellation!.Token.ThrowIfCancellationRequested();
            _lastScanReport = report;
            _lastItems.Clear();
            _lastItems.AddRange(report.Items);
            await PersistReportAsync(ReportMapper.FromUpdateScan(report));

            RenderScanReport(report);
            RequestVirgilState(report.Errors.Count == 0 ? VirgilCoreState.Success : VirgilCoreState.Warning, "PRÊT");
            AnnounceScan(report);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan annulé.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Scan annulé.\nAucune action effectuée.");
        }
        catch
        {
            StatusText = "Scan mises à jour indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Scan indisponible.\nAucune action effectuée.");
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand]
    private async Task InspectDriversAsync()
    {
        if (!BeginOperation("Recherche Windows Update en cours — ne peut pas être interrompue"))
        {
            return;
        }

        Operation.IsNonInterruptible = true;
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
                .ScanAsync(request, CreateTextProgress(), _operationCancellation!.Token)
                .ConfigureAwait(true);

            _operationCancellation!.Token.ThrowIfCancellationRequested();
            _lastScanReport = MergeDriverReport(_lastScanReport, report);
            await PersistReportAsync(ReportMapper.FromUpdateScan(_lastScanReport));
            RenderScanReport(_lastScanReport);
            RequestVirgilState(report.Errors.Count == 0 ? VirgilCoreState.Success : VirgilCoreState.Warning, "PILOTES");
            VirgilMessageRequested?.Invoke("Inventaire pilotes terminé.\nLecture seule.");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Inventaire pilotes annulé.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Inventaire annulé.\nAucune action effectuée.");
        }
        catch
        {
            StatusText = "Inventaire pilotes indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Inventaire indisponible.\nAucune action effectuée.");
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
            VirgilMessageRequested?.Invoke("Aucune application sélectionnée.");
            return;
        }

        if (!BeginOperation("Séquence guidée en attente de validation."))
        {
            return;
        }

        var startedAt = DateTimeOffset.Now;
        var results = new List<UpdateExecutionResult>();
        var errors = new List<string>();

        Operation.Start(StatusText, immediately: true, nonInterruptible: true);
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
                    errors.Add("Séquence annulée par l'utilisateur.");
                    break;
                }

                if (decision == UpdateDecision.Skip)
                {
                    results.Add(_executionService.Skip(item));
                    SetItemStatus(item.Id, UpdateItemStatus.Skipped);
                    VirgilMessageRequested?.Invoke("Mise à jour passée.\nAucune action.");
                    continue;
                }

                ConfirmedPlan plan;
                try
                {
                    plan = _confirmationService.Issue(UpdateActionPreviewFactory.Create(item),
                        new ConfirmationConsent(item.RiskLevel == UpdateRiskLevel.Sensitive
                            ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple, null));
                }
                catch (ConfirmationRefusedException)
                {
                    results.Add(_executionService.Cancel(item));
                    errors.Add("Séquence annulée par l'utilisateur.");
                    break;
                }

                RequestVirgilState(VirgilCoreState.Executing, "INSTALLATION");
                var result = await _executionService
                    .ExecuteAsync(item, plan, _operationCancellation.Token)
                    .ConfigureAwait(true);

                if (Operation.IsCancelling) result = result with { UserMessage = result.UserMessage + " Étape terminée après annulation." };
                results.Add(result);
                SetItemStatus(item.Id, result.Status);
                VirgilMessageRequested?.Invoke(result.Status == UpdateItemStatus.Completed
                    ? "Mise à jour terminée.\nSéquence continue."
                    : "Mise à jour non terminée.\nRapport disponible.");
            }
        }
        catch (OperationCanceledException)
        {
            errors.Add("Séquence annulée.");
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
        }
        catch
        {
            errors.Add("Erreur pendant la séquence guidée.");
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
        }
        finally
        {
            if (Operation.IsCancelling)
            {
                if (!errors.Any(error => error.Contains("annul", StringComparison.OrdinalIgnoreCase))) errors.Add("Séquence annulée.");
                errors.AddRange(selectedItems.Where(item => results.All(result => result.Item.Id != item.Id))
                    .Select(item => item.Name + " : non lancée, annulation demandée."));
            }
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
            VirgilMessageRequested?.Invoke(result.ReadableError ?? "Historique local indisponible. Rapport conservé en mémoire.");
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
            VirgilMessageRequested?.Invoke("Opération déjà en cours.");
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
                : "Windows Update ouvert.\nVirgil ne lancé aucune installation.");
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
            VirgilMessageRequested?.Invoke("Opération déjà en cours.");
            return false;
        }

        _operationInProgress = true;
        Operation.Start(status);
        _operationCancellation = new CancellationTokenSource();
        StatusText = status;
        SetButtonsEnabled(false);
        return true;
    }

    private void EndOperation()
    {
        Operation.Complete(StatusText);
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
        IsRunAllEnabled = !_operationInProgress && _sequenceService is not null;
        IsReportButtonEnabled = !_operationInProgress && HasAnyReport();
        IsInstallDriversVisible = _lastScanReport?.Drivers.CanInstallDrivers == true;
        IsInstallDriversEnabled = !_operationInProgress && IsInstallDriversVisible;
    }

    private IProgress<string> CreateTextProgress()
    {
        var session = _operationCancellation;
        return new Progress<string>(message => { if (ReferenceEquals(session, _operationCancellation)) HandleProgress(message); });
    }

    private void HandleProgress(string message)
    {
        if (Operation.IsCancelling) return;
        Operation.Report(new Virgil.Core.Operations.OperationProgress(Operation.IsNonInterruptible
            ? message + " — l’étape en cours ne peut pas être interrompue" : message));
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
            ? "Aucune commande exécutable préparée."
            : FormatCommand(item.CommandPreview);
        ValidationSafety =
            "Validation individuelle obligatoire.\nAucun --all, aucune installation pilote, aucun redémarrage, aucune élévation demandee par Virgil.";
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
            $"Redémarrage en attente : {(info.PendingRebootDetected ? "oui" : "non")}",
            $"URI : {info.SettingsUri}"
        });
        ValidationCommand = "Ouverture des Paramètres Windows uniquement.";
        ValidationSafety =
            "Virgil n'appuie sur aucun bouton Windows Update.\nAucune recherche, installation ou redémarrage n'est déclenché.";
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
        IsModuleInstallVisible = report.IsWingetClientModuleMissing && _wingetModuleService is not null;
        OverallStatus = report.OverallStatus.ToUpperInvariant();
        WingetStatus = report.Winget.IsAvailable
            ? report.Winget.Message
            : "WinGet non détecté.";
        WindowsUpdateStatus = report.WindowsUpdate.Status;
        DriversStatus = report.Drivers.WasAnalyzed
            ? $"{report.Drivers.Drivers.Count} pilotes inventoriés. Mises à jour facultatives dans « Tout mettre à jour »."
            : "Non inventorié.";

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
            Entries.Add(new UpdateTextCardViewModel("Aucune mise à jour applicative prévisualisée."));
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
            DriverCards.Add("Inventaire disponible après analyse approfondie ou examen manuel.");
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
            VirgilMessageRequested?.Invoke("WinGet non détecté.\nWindows Update reste disponible.");
            return;
        }

        VirgilMessageRequested?.Invoke(report.Items.Count == 0
            ? "Scan terminé.\nAucune mise à jour applicative détectée."
            : $"Scan terminé.\n{report.Items.Count} mises à jour à vérifier.");
    }

    private void CompleteSequenceState(UpdateSessionReport report)
    {
        if (report.WasCancelled)
        {
            StatusText = "Séquence annulée.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            return;
        }

        if (report.FailedCount > 0 || report.Errors.Count > 0)
        {
            StatusText = "Séquence terminée avec erreurs.";
            RequestVirgilState(VirgilCoreState.Warning, "ATTENTION");
            return;
        }

        StatusText = "Séquence terminée.";
        RequestVirgilState(VirgilCoreState.Success, "TERMINÉ");
    }

    private void ShowReport()
    {
        if (!HasAnyReport())
        {
            VirgilMessageRequested?.Invoke("Aucun rapport mises à jour disponible.");
            return;
        }

        ReportText = FormatReport(_lastScanReport, _lastSessionReport) + (_sequenceReport is null ? string.Empty : "\n" + _sequenceReport);
        IsReportOpen = true;
    }

    private void HideReport()
    {
        IsReportOpen = false;
    }

    private bool HasAnyReport()
    {
        return _lastScanReport is not null || _lastSessionReport is not null || _sequenceReport is not null;
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
            builder.AppendLine($"État global : {scanReport.OverallStatus}");
            builder.AppendLine($"WinGet : {(scanReport.Winget.IsAvailable ? scanReport.Winget.Message : "non détecté")}");
            builder.AppendLine($"Applications : {scanReport.Items.Count}");
            builder.AppendLine($"Faible risque : {scanReport.SafeCount}");
            builder.AppendLine($"Validation : {scanReport.ValidationRequiredCount}");
            builder.AppendLine($"Sensibles : {scanReport.SensitiveCount}");
            builder.AppendLine($"Windows Update : {scanReport.WindowsUpdate.Status}");
            builder.AppendLine($"Redémarrage en attente : {(scanReport.WindowsUpdate.PendingRebootDetected ? "oui" : "non")}");
            builder.AppendLine($"Pilotes inventoriés : {scanReport.Drivers.Drivers.Count}");
            builder.AppendLine($"Bouton pilotes : {scanReport.Drivers.InstallButtonVisibilityReason}");
            AppendList(builder, "Recommandations", scanReport.Recommendations);
            AppendList(builder, "Erreurs lisibles", scanReport.Errors);
            builder.AppendLine();
        }

        if (sessionReport is not null)
        {
            builder.AppendLine($"Date séquence : {sessionReport.StartedAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Durée : {sessionReport.Duration.TotalSeconds:0.0} s");
            builder.AppendLine($"Terminées : {sessionReport.CompletedCount}");
            builder.AppendLine($"Passées : {sessionReport.SkippedCount}");
            builder.AppendLine($"Échecs : {sessionReport.FailedCount}");
            builder.AppendLine($"Annulation : {(sessionReport.WasCancelled ? "oui" : "non")}");

            foreach (var result in sessionReport.Results)
            {
                builder.AppendLine($"{result.Item.Name} : {StatusLabel(result.Status)}");
                if (!string.IsNullOrWhiteSpace(result.UserMessage))
                {
                    builder.AppendLine($"  {result.UserMessage}");
                }
            }

            AppendList(builder, "Erreurs séquence", sessionReport.Errors);
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
            UpdateItemStatus.Completed => "terminée",
            UpdateItemStatus.Skipped => "passée",
            UpdateItemStatus.Failed => "échec",
            UpdateItemStatus.Cancelled => "annulée",
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
