using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Virgil.App.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Virgil.Core.Resources;
using Virgil.Core.Reports;
using Virgil.Core.Scanning;
using Virgil.Contracts;

namespace Virgil.App.ViewModels;

public partial class ResourcesViewModel : ObservableObject
{
    private readonly IResourceMonitoringService _monitoringService;
    private readonly IProcessActionService _actionService;
    private readonly ResourceReportBuilder _reportBuilder;
    private readonly List<ResourceAnalysisReport> _analyses = new();
    private readonly List<ProcessActionResult> _actions = new();
    private readonly List<string> _skippedActions = new();
    private readonly List<string> _errors = new();
    private readonly HashSet<int> _completedProcessIds = new();
    private readonly IConfirmationService _confirmationService;
    private readonly IReportHistoryService _reportHistoryService;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource<ResourceDecision>? _activeDecision;
    private ResourceAnalysisReport? _lastAnalysis;
    private bool _operationInProgress;

    public ResourcesViewModel(
        IResourceMonitoringService monitoringService,
        IProcessActionService actionService,
        ResourceReportBuilder reportBuilder,
        IReportHistoryService reportHistoryService, IConfirmationService confirmationService)
    {
        _monitoringService = monitoringService;
        _actionService = actionService;
        _reportBuilder = reportBuilder;
        _confirmationService = confirmationService;
        _reportHistoryService = reportHistoryService;
        ReleaseInactiveMemoryLabel = _actionService.CanReleaseInactiveMemory
            ? "LIBÉRER MÉMOIRE INACTIVE"
            : "MÉMOIRE INACTIVE - INFO";
    }

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public event EventHandler? ReportPersisted;

    public event EventHandler? ProcessListScrollRequested;

    public ObservableCollection<ProcessResourceItemViewModel> Items { get; } = new();

    [ObservableProperty] private string statusText = "CPU, RAM ET PROCESSUS EN LECTURE SEULE";
    [ObservableProperty] private string cpuSummaryText = "NON ANALYSÉ";
    [ObservableProperty] private string memorySummaryText = "NON ANALYSÉ";
    [ObservableProperty] private string sessionSummaryText = "NON ANALYSÉ";
    [ObservableProperty] private string recommendationsText = "Lancez une analyse. Aucune action ne sera exécutée.";
    [ObservableProperty] private string processSummaryText = "Lancez une analyse pour identifier les principaux consommateurs CPU et RAM.";
    [ObservableProperty] private string processesNotice = "Analyse requise. Les processus système critiques, sécurité, VPN et matériel resteront protégés.";
    [ObservableProperty] private string releaseInactiveMemoryLabel = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isBusy;
    public bool IsIdle => !IsBusy;
    [ObservableProperty] private bool isReportButtonEnabled;
    [ObservableProperty] private bool isValidationOpen;
    [ObservableProperty] private string validationTitle = "VALIDATION REQUISE";
    [ObservableProperty] private string validationDetails = string.Empty;
    [ObservableProperty] private string validationSafety = string.Empty;
    [ObservableProperty] private string executeActionLabel = "EXÉCUTER";
    [ObservableProperty] private bool isReportOpen;
    [ObservableProperty] private string reportText = string.Empty;
    [ObservableProperty] private VirgilCoreState miniCoreState = VirgilCoreState.Idle;
    [ObservableProperty] private VirgilCoreState validationCoreState = VirgilCoreState.Idle;

    public void CancelActiveOperation()
    {
        _operationCancellation?.Cancel();
        CompleteDecision(ResourceDecision.CancelAll);
    }

    public bool TryCloseOverlay()
    {
        if (IsReportOpen)
        {
            CloseReport();
            return true;
        }

        if (IsValidationOpen)
        {
            CompleteDecision(ResourceDecision.Skip);
            return true;
        }

        if (_operationInProgress)
        {
            CancelActiveOperation();
            return true;
        }

        return false;
    }

    [RelayCommand]
    private Task AnalyzeAsync() => AnalyzeAsync(scrollToProcesses: false);

    [RelayCommand]
    private async Task HeavyProcessesAsync()
    {
        if (_lastAnalysis is null)
        {
            await AnalyzeAsync(scrollToProcesses: true);
            return;
        }

        ProcessListScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ReleaseInactiveMemoryAsync()
    {
        if (!BeginOperation("Information mémoire inactive."))
        {
            return;
        }

        try
        {
            var result = await _actionService.ExecuteAsync(
                ProcessActionKind.ReleaseInactiveMemory,
                null,
                plan: null,
                _operationCancellation!.Token);
            RecordResult(result);
            await PersistCurrentReportAsync();
            StatusText = "Information seulement. Aucune action mémoire exécutée.";
            RequestVirgilState(VirgilCoreState.Idle, "INFORMATION");
            VirgilMessageRequested?.Invoke(
                "Mémoire inactive : information seulement.\nAucun boost RAM magique.\nAucune action exécutée.");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Information annulée.";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand]
    private Task RestartExplorerAsync() => RequestConfirmedActionAsync(ProcessActionKind.RestartExplorer, null);

    [RelayCommand]
    private void ReturnHome() => ReturnHomeRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ExecuteDecision() => CompleteDecision(ResourceDecision.Execute);

    [RelayCommand]
    private void SkipDecision() => CompleteDecision(ResourceDecision.Skip);

    [RelayCommand]
    private void CancelDecision() => CompleteDecision(ResourceDecision.CancelAll);

    private async Task AnalyzeAsync(bool scrollToProcesses)
    {
        if (!BeginOperation("Observation CPU et RAM en cours."))
        {
            return;
        }

        try
        {
            _completedProcessIds.Clear();
            RequestVirgilState(VirgilCoreState.Scanning, "RESSOURCES");
            VirgilMessageRequested?.Invoke(
                "Analyse ressources lancée.\nObservation CPU courte et lecture RAM.\nAucune action exécutée.");
            var progress = new Progress<ResourceProgress>(item =>
            {
                StatusText = $"{item.Message} {item.Percent} %";
            });
            var report = await _monitoringService
                .AnalyzeAsync(ResourceAnalysisRequest.Interactive, progress, _operationCancellation!.Token)
                .ConfigureAwait(true);
            _lastAnalysis = report;
            _analyses.Add(report);
            _errors.AddRange(report.Errors);
            RenderAnalysis(report);
            RequestVirgilState(
                report.OverallHealth >= ResourceHealthLevel.InterventionRecommended
                    ? VirgilCoreState.Warning
                    : VirgilCoreState.Success,
                "ANALYSE TERMINÉE");
            VirgilMessageRequested?.Invoke(BuildAnalysisMessage(report));
            await PersistCurrentReportAsync();
            if (scrollToProcesses)
            {
                ProcessListScrollRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Analyse annulée. Aucune action exécutée.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Analyse ressources annulée.\nAucune action exécutée.");
        }
        catch
        {
            _errors.Add("Analyse ressources indisponible.");
            StatusText = "Analyse ressources indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Analyse ressources indisponible.\nAucune action exécutée.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RequestConfirmedActionAsync(
        ProcessActionKind action,
        ProcessResourceInfo? target)
    {
        if (_operationInProgress)
        {
            VirgilMessageRequested?.Invoke("Opération déjà en cours.");
            return;
        }

        var decision = await AskDecisionAsync(action, target).ConfigureAwait(true);
        ConfirmedPlan? plan = null;
        if (decision == ResourceDecision.Execute)
        {
            try
            {
                plan = _confirmationService.Issue(ProcessActionPreviewFactory.Create(action, target),
                    new ConfirmationConsent(action == ProcessActionKind.KillProcess
                        ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple, null));
            }
            catch (ConfirmationRefusedException)
            {
                decision = ResourceDecision.CancelAll;
            }
        }
        if (decision != ResourceDecision.Execute)
        {
            var targetName = TargetName(action, target);
            _skippedActions.Add($"{ActionLabel(action)} - {targetName}");
            IsReportButtonEnabled = true;
            StatusText = decision == ResourceDecision.CancelAll
                ? "Action annulée."
                : "Action passée.";
            VirgilMessageRequested?.Invoke("Action non exécutée.");
            await PersistCurrentReportAsync();
            return;
        }

        await ExecuteActionAsync(action, target, plan).ConfigureAwait(true);
    }

    private async Task ExecuteActionAsync(ProcessActionKind action, ProcessResourceInfo? target, ConfirmedPlan? plan)
    {
        if (!BeginOperation($"{ActionLabel(action)} en cours."))
        {
            return;
        }

        try
        {
            RequestVirgilState(VirgilCoreState.Executing, "EXÉCUTION");
            var result = await _actionService.ExecuteAsync(
                action,
                target,
                plan,
                _operationCancellation!.Token);
            RecordResult(result);

            if (result.Status == ProcessActionStatus.Completed &&
                target is not null &&
                action is ProcessActionKind.CloseMainWindow or ProcessActionKind.KillProcess)
            {
                _completedProcessIds.Add(target.ProcessId);
                RenderProcesses(_lastAnalysis);
            }

            StatusText = result.ReadableError is null
                ? result.Summary
                : $"{result.Summary} {result.ReadableError}";
            RequestVirgilState(
                result.Status == ProcessActionStatus.Completed
                    ? VirgilCoreState.Success
                    : VirgilCoreState.Warning,
                result.Status == ProcessActionStatus.Completed ? "TERMINÉ" : "ATTENTION");
            VirgilMessageRequested?.Invoke(ResultMessage(result));
            await PersistCurrentReportAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Action annulée.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
        }
        catch
        {
            _errors.Add($"{ActionLabel(action)} indisponible.");
            StatusText = "Action impossible. Aucune autre action exécutée.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ExecuteOpenLocationAsync(ProcessResourceInfo target)
    {
        await ExecuteActionAsync(ProcessActionKind.OpenLocation, target, null).ConfigureAwait(true);
    }

    private Task<ResourceDecision> AskDecisionAsync(
        ProcessActionKind action,
        ProcessResourceInfo? target)
    {
        _activeDecision = new TaskCompletionSource<ResourceDecision>();
        ShowValidation(action, target);
        return _activeDecision.Task;
    }

    private void ShowValidation(ProcessActionKind action, ProcessResourceInfo? target)
    {
        var reinforced = action == ProcessActionKind.KillProcess;
        ValidationTitle = reinforced ? "CONFIRMATION RENFORCÉE" : "VALIDATION REQUISE";
        ValidationDetails = action switch
        {
            ProcessActionKind.CloseMainWindow => string.Join("\n", new[]
            {
                "Fermeture propre demandée.",
                $"Application : {TargetName(action, target)}",
                "Effet : une demande de fermeture sera envoyée à la fenêtre principale.",
                "Des données non sauvegardées peuvent être concernées selon l'application."
            }),
            ProcessActionKind.KillProcess => string.Join("\n", new[]
            {
                "Fermeture forcée demandée.",
                $"Application : {TargetName(action, target)}",
                "Cette action peut entraîner une perte de données non sauvegardées.",
                "À utiliser seulement si l'application ne répond plus."
            }),
            ProcessActionKind.RestartExplorer => string.Join("\n", new[]
            {
                "Relance d'Explorer Windows demandée.",
                "Le bureau et la barre des tâches peuvent disparaître quelques secondes.",
                "Les applications ouvertes ne seront pas fermées."
            }),
            _ => ActionLabel(action)
        };
        ValidationSafety = reinforced
            ? "Confirmation renforcée obligatoire. Processus critique bloqué. Aucun autre processus ne sera fermé."
            : "Validation explicite obligatoire. Aucune fermeture forcée automatique. Aucun redémarrage forcé.";
        ExecuteActionLabel = reinforced ? "JE CONFIRME" : "EXÉCUTER";
        IsValidationOpen = true;
        var state = reinforced ? VirgilCoreState.SensitiveAction : VirgilCoreState.Warning;
        ValidationCoreState = state;
        RequestVirgilState(state, "VALIDATION");
    }

    private void CompleteDecision(ResourceDecision decision)
    {
        if (_activeDecision is null)
        {
            return;
        }

        ValidationCoreState = VirgilCoreState.Idle;
        IsValidationOpen = false;
        var activeDecision = _activeDecision;
        _activeDecision = null;
        activeDecision.TrySetResult(decision);
    }

    private void RenderAnalysis(ResourceAnalysisReport report)
    {
        StatusText = $"Analyse terminée en {report.Duration.TotalSeconds:0.0} s.";
        CpuSummaryText =
            $"Moyenne {report.AverageCpuPercent:0.0} % - maximum {report.MaximumCpuPercent:0.0} % - {HealthLabel(report.CpuHealth)}";
        var sample = report.Samples.LastOrDefault();
        MemorySummaryText = sample is null || sample.TotalMemoryBytes == 0
            ? "Lecture RAM indisponible."
            : $"{report.AverageMemoryPercent:0.0} % utilisée - {HealthLabel(report.MemoryHealth)} - " +
              $"{ScanRules.FormatBytes((long)sample.UsedMemoryBytes)} / {ScanRules.FormatBytes((long)sample.TotalMemoryBytes)}";
        SessionSummaryText = $"{FormatUptime(report.Uptime)} - {report.ProcessCount} processus" +
            (report.RestartRecommended ? " - redémarrage manuel conseillé" : string.Empty);
        RecommendationsText = report.Recommendations.Count == 0
            ? "Ressources stables. Aucune action automatique."
            : string.Join("\n", report.Recommendations.Select(item => "- " + item));
        RenderProcesses(report);
        IsReportButtonEnabled = true;
    }

    private void RenderProcesses(ResourceAnalysisReport? report)
    {
        Items.Clear();
        if (report is null)
        {
            RenderEmptyProcesses();
            return;
        }

        var processes = report.TopMemoryProcesses
            .Concat(report.TopCpuProcesses)
            .Where(process => !_completedProcessIds.Contains(process.ProcessId))
            .GroupBy(process => process.ProcessId)
            .Select(group => group.First())
            .OrderByDescending(process => process.Status == ProcessResourceStatus.Heavy)
            .ThenByDescending(process => process.WorkingSetBytes)
            .Take(12)
            .ToList();
        var heavyCount = processes.Count(process => process.Status == ProcessResourceStatus.Heavy);
        ProcessSummaryText = heavyCount == 0
            ? "Aucun processus lourd selon les seuils V1. Principaux consommateurs affichés pour information."
            : $"{heavyCount} processus lourds détectés. Aucune fermeture automatique.";

        if (processes.Count == 0)
        {
            ProcessesNotice = "Aucun processus accessible à afficher.";
            return;
        }

        ProcessesNotice = string.Empty;
        foreach (var process in processes)
        {
            Items.Add(new ProcessResourceItemViewModel(process,
                () => ExecuteOpenLocationAsync(process),
                () => RequestConfirmedActionAsync(ProcessActionKind.CloseMainWindow, process),
                () => RequestConfirmedActionAsync(ProcessActionKind.KillProcess, process),
                () => IgnoreAsync(process)));
        }
    }

    private async Task IgnoreAsync(ProcessResourceInfo process)
    {
        _skippedActions.Add($"Processus ignoré - {process.Name} (PID {process.ProcessId})");
        _completedProcessIds.Add(process.ProcessId);
        RenderProcesses(_lastAnalysis);
        IsReportButtonEnabled = true;
        VirgilMessageRequested?.Invoke("Processus ignoré.\nAucune action exécutée.");
        await PersistCurrentReportAsync();
    }

    private void RenderEmptyProcesses()
    {
        Items.Clear();
        ProcessesNotice = "Analyse requise. Les processus système critiques, sécurité, VPN et matériel resteront protégés.";
    }

    private bool BeginOperation(string status)
    {
        if (_operationInProgress)
        {
            VirgilMessageRequested?.Invoke("Opération déjà en cours.");
            return false;
        }

        _operationInProgress = true;
        _operationCancellation = new CancellationTokenSource();
        StatusText = status;
        SetButtonsEnabled(false);
        IsBusy = true;
        return true;
    }

    private void EndOperation()
    {
        _operationInProgress = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        IsBusy = false;
        SetButtonsEnabled(true);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        IsReportButtonEnabled = enabled && HasReportData();
    }

    private void RecordResult(ProcessActionResult result)
    {
        _actions.Add(result);
        if (!string.IsNullOrWhiteSpace(result.ReadableError))
        {
            _errors.Add(result.ReadableError);
        }

        IsReportButtonEnabled = true;
    }

    [RelayCommand]
    private void ViewReport()
    {
        if (!HasReportData())
        {
            VirgilMessageRequested?.Invoke("Aucun rapport ressources disponible.");
            return;
        }

        ReportText = _reportBuilder.Build(BuildSessionReport(), includeTechnicalDetails: false);
        IsReportOpen = true;
    }

    [RelayCommand]
    private void CloseReport()
    {
        IsReportOpen = false;
    }

    private ResourceSessionReport BuildSessionReport()
    {
        var proposed = (_lastAnalysis?.Recommendations ?? Array.Empty<string>())
            .Concat((_lastAnalysis?.TopMemoryProcesses ?? Array.Empty<ProcessResourceInfo>())
                .Where(process => process.Status == ProcessResourceStatus.Heavy)
                .Select(process => $"Examiner {process.Name} (PID {process.ProcessId})"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ResourceSessionReport
        {
            CapturedAt = DateTimeOffset.Now,
            Analyses = _analyses.ToList(),
            ProposedActions = proposed,
            ExecutedActions = _actions.ToList(),
            SkippedActions = _skippedActions.ToList(),
            Errors = _errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RestartRecommended = _analyses.Any(analysis => analysis.RestartRecommended)
        };
    }

    private async Task PersistCurrentReportAsync()
    {
        if (!HasReportData())
        {
            return;
        }

        var result = await _reportHistoryService
            .SaveAsync(ReportMapper.FromResources(BuildSessionReport()), CancellationToken.None)
            .ConfigureAwait(true);
        if (!result.Success || !string.IsNullOrWhiteSpace(result.ReadableError))
        {
            VirgilMessageRequested?.Invoke(result.ReadableError ?? "Historique local indisponible. Rapport conservé en mémoire.");
        }

        if (result.Success)
        {
            ReportPersisted?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool HasReportData()
    {
        return _analyses.Count > 0 || _actions.Count > 0 || _skippedActions.Count > 0 || _errors.Count > 0;
    }

    private void RequestVirgilState(VirgilCoreState state, string label)
    {
        MiniCoreState = state;
        VirgilStateRequested?.Invoke(state, label);
    }

    private static string BuildAnalysisMessage(ResourceAnalysisReport report)
    {
        var heavy = report.TopMemoryProcesses
            .Concat(report.TopCpuProcesses)
            .GroupBy(process => process.ProcessId)
            .Count(group => group.First().Status == ProcessResourceStatus.Heavy);
        return $"Analyse ressources terminée.\nRAM : {report.AverageMemoryPercent:0.0} %\n" +
            $"CPU moyen : {report.AverageCpuPercent:0.0} %\n{heavy} processus lourds détectés.";
    }

    private static string ResultMessage(ProcessActionResult result)
    {
        return result.ReadableError is null
            ? result.Summary
            : $"{result.Summary}\n{result.ReadableError}";
    }

    private static string TargetName(ProcessActionKind action, ProcessResourceInfo? target)
    {
        return action == ProcessActionKind.RestartExplorer
            ? "Explorer Windows"
            : target is null ? "N/A" : $"{target.Name} (PID {target.ProcessId})";
    }

    private static string ActionLabel(ProcessActionKind action)
    {
        return action switch
        {
            ProcessActionKind.CloseMainWindow => "Fermeture propre",
            ProcessActionKind.KillProcess => "Fermeture forcée",
            ProcessActionKind.OpenLocation => "Ouverture emplacement",
            ProcessActionKind.RestartExplorer => "Relance Explorer",
            ProcessActionKind.ReleaseInactiveMemory => "Mémoire inactive",
            _ => action.ToString()
        };
    }

    private static string HealthLabel(ResourceHealthLevel health)
    {
        return health switch
        {
            ResourceHealthLevel.Stable => "STABLE",
            ResourceHealthLevel.Watch => "À SURVEILLER",
            ResourceHealthLevel.InterventionRecommended => "INTERVENTION CONSEILLÉE",
            ResourceHealthLevel.Critical => "CRITIQUE",
            _ => "INCONNU"
        };
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        return uptime.TotalDays >= 1
            ? $"Session {(int)uptime.TotalDays} j {uptime.Hours} h"
            : $"Session {uptime.Hours} h {uptime.Minutes} min";
    }

    private enum ResourceDecision
    {
        Execute,
        Skip,
        CancelAll
    }
}

