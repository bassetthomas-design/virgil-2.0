using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Virgil.App.Controls;
using Virgil.Core.Resources;
using Virgil.Core.Scanning;
using Virgil.Domain;

namespace Virgil.App.Views;

public partial class ResourcesView : UserControl
{
    private readonly IResourceMonitoringService _monitoringService;
    private readonly IProcessActionService _actionService;
    private readonly ResourceReportBuilder _reportBuilder;
    private readonly List<ResourceAnalysisReport> _analyses = new();
    private readonly List<ProcessActionResult> _actions = new();
    private readonly List<string> _skippedActions = new();
    private readonly List<string> _errors = new();
    private readonly HashSet<int> _completedProcessIds = new();
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource<ResourceDecision>? _activeDecision;
    private ProcessActionKind? _pendingAction;
    private ProcessResourceInfo? _pendingTarget;
    private ResourceAnalysisReport? _lastAnalysis;
    private bool _operationInProgress;

    public ResourcesView()
        : this(new ResourceMonitoringService(), new ProcessActionService(), new ResourceReportBuilder())
    {
    }

    public ResourcesView(
        IResourceMonitoringService monitoringService,
        IProcessActionService actionService,
        ResourceReportBuilder reportBuilder)
    {
        InitializeComponent();
        _monitoringService = monitoringService;
        _actionService = actionService;
        _reportBuilder = reportBuilder;
        RenderEmptyProcesses();
        ReleaseInactiveMemoryButton.Content = _actionService.CanReleaseInactiveMemory
            ? "LIBERER MEMOIRE INACTIVE"
            : "MEMOIRE INACTIVE - INFO";
    }

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public void FocusAnalyzeButton()
    {
        AnalyzeResourcesButton.Focus();
    }

    public void CancelActiveOperation()
    {
        _operationCancellation?.Cancel();
        CompleteDecision(ResourceDecision.CancelAll);
    }

    public bool TryCloseOverlay()
    {
        if (ResourcesReportOverlay.Visibility == Visibility.Visible)
        {
            HideReport();
            return true;
        }

        if (ResourceValidationOverlay.Visibility == Visibility.Visible)
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

    private async void AnalyzeResources_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeAsync(scrollToProcesses: false);
    }

    private async void HeavyProcesses_Click(object sender, RoutedEventArgs e)
    {
        if (_lastAnalysis is null)
        {
            await AnalyzeAsync(scrollToProcesses: true);
            return;
        }

        ProcessListHeading.BringIntoView();
    }

    private async void ReleaseInactiveMemory_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginOperation("Information memoire inactive."))
        {
            return;
        }

        try
        {
            var result = await _actionService.ExecuteAsync(
                ProcessActionKind.ReleaseInactiveMemory,
                null,
                confirmed: true,
                reinforcedConfirmation: false,
                _operationCancellation!.Token);
            RecordResult(result);
            ResourcesStatusText.Text = "Information seulement. Aucune action memoire executee.";
            RequestVirgilState(VirgilCoreState.Idle, "INFORMATION");
            VirgilMessageRequested?.Invoke(
                "Memoire inactive : information seulement.\nAucun boost RAM magique.\nAucune action executee.");
        }
        catch (OperationCanceledException)
        {
            ResourcesStatusText.Text = "Information annulee.";
        }
        finally
        {
            EndOperation();
        }
    }

    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        await RequestConfirmedActionAsync(ProcessActionKind.RestartExplorer, null);
    }

    private void ViewResourcesReport_Click(object sender, RoutedEventArgs e)
    {
        ShowReport();
    }

    private void ReturnHome_Click(object sender, RoutedEventArgs e)
    {
        ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExecuteResourceAction_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(ResourceDecision.Execute);
    }

    private void SkipResourceAction_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(ResourceDecision.Skip);
    }

    private void CancelResourceAction_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(ResourceDecision.CancelAll);
    }

    private void CloseResourcesReport_Click(object sender, RoutedEventArgs e)
    {
        HideReport();
    }

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
                "Analyse ressources lancee.\nObservation CPU courte et lecture RAM.\nAucune action executee.");
            var progress = new Progress<ResourceProgress>(item =>
            {
                ResourcesStatusText.Text = $"{item.Message} {item.Percent} %";
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
                "ANALYSE TERMINEE");
            VirgilMessageRequested?.Invoke(BuildAnalysisMessage(report));
            if (scrollToProcesses)
            {
                ProcessListHeading.BringIntoView();
            }
        }
        catch (OperationCanceledException)
        {
            ResourcesStatusText.Text = "Analyse annulee. Aucune action executee.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Analyse ressources annulee.\nAucune action executee.");
        }
        catch
        {
            _errors.Add("Analyse ressources indisponible.");
            ResourcesStatusText.Text = "Analyse ressources indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Analyse ressources indisponible.\nAucune action executee.");
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
            VirgilMessageRequested?.Invoke("Operation deja en cours.");
            return;
        }

        using var confirmationCancellation = new CancellationTokenSource();
        var decision = await AskDecisionAsync(action, target, confirmationCancellation.Token).ConfigureAwait(true);
        if (decision != ResourceDecision.Execute)
        {
            var targetName = TargetName(action, target);
            _skippedActions.Add($"{ActionLabel(action)} - {targetName}");
            ViewResourcesReportButton.IsEnabled = true;
            ResourcesStatusText.Text = decision == ResourceDecision.CancelAll
                ? "Action annulee."
                : "Action passee.";
            VirgilMessageRequested?.Invoke("Action non executee.");
            return;
        }

        await ExecuteActionAsync(action, target).ConfigureAwait(true);
    }

    private async Task ExecuteActionAsync(ProcessActionKind action, ProcessResourceInfo? target)
    {
        if (!BeginOperation($"{ActionLabel(action)} en cours."))
        {
            return;
        }

        try
        {
            RequestVirgilState(VirgilCoreState.Executing, "EXECUTION");
            var result = await _actionService.ExecuteAsync(
                action,
                target,
                confirmed: true,
                reinforcedConfirmation: action == ProcessActionKind.KillProcess,
                _operationCancellation!.Token);
            RecordResult(result);

            if (result.Status == ProcessActionStatus.Completed &&
                target is not null &&
                action is ProcessActionKind.CloseMainWindow or ProcessActionKind.KillProcess)
            {
                _completedProcessIds.Add(target.ProcessId);
                RenderProcesses(_lastAnalysis);
            }

            ResourcesStatusText.Text = result.ReadableError is null
                ? result.Summary
                : $"{result.Summary} {result.ReadableError}";
            RequestVirgilState(
                result.Status == ProcessActionStatus.Completed
                    ? VirgilCoreState.Success
                    : VirgilCoreState.Warning,
                result.Status == ProcessActionStatus.Completed ? "TERMINE" : "ATTENTION");
            VirgilMessageRequested?.Invoke(ResultMessage(result));
        }
        catch (OperationCanceledException)
        {
            ResourcesStatusText.Text = "Action annulee.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
        }
        catch
        {
            _errors.Add($"{ActionLabel(action)} indisponible.");
            ResourcesStatusText.Text = "Action impossible. Aucune autre action executee.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ExecuteOpenLocationAsync(ProcessResourceInfo target)
    {
        await ExecuteActionAsync(ProcessActionKind.OpenLocation, target).ConfigureAwait(true);
    }

    private Task<ResourceDecision> AskDecisionAsync(
        ProcessActionKind action,
        ProcessResourceInfo? target,
        CancellationToken cancellationToken)
    {
        _pendingAction = action;
        _pendingTarget = target;
        _activeDecision = new TaskCompletionSource<ResourceDecision>();
        ShowValidation(action, target);
        cancellationToken.Register(() =>
            Dispatcher.BeginInvoke(new Action(() => CompleteDecision(ResourceDecision.CancelAll))));
        return _activeDecision.Task;
    }

    private void ShowValidation(ProcessActionKind action, ProcessResourceInfo? target)
    {
        var reinforced = action == ProcessActionKind.KillProcess;
        ValidationTitleText.Text = reinforced ? "CONFIRMATION RENFORCEE" : "VALIDATION REQUISE";
        ValidationDetailsText.Text = action switch
        {
            ProcessActionKind.CloseMainWindow => string.Join("\n", new[]
            {
                "Fermeture propre demandee.",
                $"Application : {TargetName(action, target)}",
                "Effet : une demande de fermeture sera envoyee a la fenetre principale.",
                "Des donnees non sauvegardees peuvent etre concernees selon l'application."
            }),
            ProcessActionKind.KillProcess => string.Join("\n", new[]
            {
                "Fermeture forcee demandee.",
                $"Application : {TargetName(action, target)}",
                "Cette action peut entrainer une perte de donnees non sauvegardees.",
                "A utiliser seulement si l'application ne repond plus."
            }),
            ProcessActionKind.RestartExplorer => string.Join("\n", new[]
            {
                "Relance d'Explorer Windows demandee.",
                "Le bureau et la barre des taches peuvent disparaitre quelques secondes.",
                "Les applications ouvertes ne seront pas fermees."
            }),
            _ => ActionLabel(action)
        };
        ValidationSafetyText.Text = reinforced
            ? "Confirmation renforcee obligatoire. Processus critique bloque. Aucun autre processus ne sera ferme."
            : "Validation explicite obligatoire. Aucune fermeture forcee automatique. Aucun redemarrage force.";
        ExecuteResourceActionButton.Content = reinforced ? "JE CONFIRME" : "EXECUTER";
        ResourceValidationOverlay.Visibility = Visibility.Visible;
        var state = reinforced ? VirgilCoreState.SensitiveAction : VirgilCoreState.Warning;
        ValidationCore.SetState(state);
        RequestVirgilState(state, "VALIDATION");
        ExecuteResourceActionButton.Focus();
    }

    private void CompleteDecision(ResourceDecision decision)
    {
        if (_activeDecision is null)
        {
            return;
        }

        ValidationCore.SetState(VirgilCoreState.Idle);
        ResourceValidationOverlay.Visibility = Visibility.Collapsed;
        _activeDecision.TrySetResult(decision);
        _activeDecision = null;
        _pendingAction = null;
        _pendingTarget = null;
    }

    private void RenderAnalysis(ResourceAnalysisReport report)
    {
        ResourcesStatusText.Text = $"Analyse terminee en {report.Duration.TotalSeconds:0.0} s.";
        CpuSummaryText.Text =
            $"Moyenne {report.AverageCpuPercent:0.0} % - maximum {report.MaximumCpuPercent:0.0} % - {HealthLabel(report.CpuHealth)}";
        var sample = report.Samples.LastOrDefault();
        MemorySummaryText.Text = sample is null || sample.TotalMemoryBytes == 0
            ? "Lecture RAM indisponible."
            : $"{report.AverageMemoryPercent:0.0} % utilisee - {HealthLabel(report.MemoryHealth)} - " +
              $"{ScanRules.FormatBytes((long)sample.UsedMemoryBytes)} / {ScanRules.FormatBytes((long)sample.TotalMemoryBytes)}";
        SessionSummaryText.Text = $"{FormatUptime(report.Uptime)} - {report.ProcessCount} processus" +
            (report.RestartRecommended ? " - redemarrage manuel conseille" : string.Empty);
        RecommendationsText.Text = report.Recommendations.Count == 0
            ? "Ressources stables. Aucune action automatique."
            : string.Join("\n", report.Recommendations.Select(item => "- " + item));
        RenderProcesses(report);
        ViewResourcesReportButton.IsEnabled = true;
    }

    private void RenderProcesses(ResourceAnalysisReport? report)
    {
        ProcessesPanel.Children.Clear();
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
        ProcessSummaryText.Text = heavyCount == 0
            ? "Aucun processus lourd selon les seuils V1. Principaux consommateurs affiches pour information."
            : $"{heavyCount} processus lourds detectes. Aucune fermeture automatique.";

        if (processes.Count == 0)
        {
            ProcessesPanel.Children.Add(CreateTextCard("Aucun processus accessible a afficher."));
            return;
        }

        foreach (var process in processes)
        {
            ProcessesPanel.Children.Add(CreateProcessCard(process));
        }
    }

    private UIElement CreateProcessCard(ProcessResourceInfo process)
    {
        var card = new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var stack = new StackPanel();
        stack.Children.Add(CreateText($"{process.Name} (PID {process.ProcessId}) - {StatusLabel(process.Status)}", primary: true));
        stack.Children.Add(CreateText(
            $"RAM {ScanRules.FormatBytes(process.WorkingSetBytes)} - CPU {process.CpuPercent:0.0} %"));
        stack.Children.Add(CreateText($"Editeur : {process.Publisher ?? "non disponible"}"));
        stack.Children.Add(CreateText(process.UserMessage));

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (!string.IsNullOrWhiteSpace(process.Path))
        {
            actions.Children.Add(CreateButton("OUVRIR EMPLACEMENT", async () => await ExecuteOpenLocationAsync(process)));
        }

        if (process.CanCloseGracefully)
        {
            actions.Children.Add(CreateButton(
                "FERMER PROPREMENT",
                async () => await RequestConfirmedActionAsync(ProcessActionKind.CloseMainWindow, process),
                primary: true));
        }

        if (process.CanForceClose)
        {
            actions.Children.Add(CreateButton(
                "FORCER FERMETURE",
                async () => await RequestConfirmedActionAsync(ProcessActionKind.KillProcess, process)));
        }

        actions.Children.Add(CreateButton("IGNORER", () =>
        {
            _skippedActions.Add($"Processus ignore - {process.Name} (PID {process.ProcessId})");
            _completedProcessIds.Add(process.ProcessId);
            RenderProcesses(_lastAnalysis);
            ViewResourcesReportButton.IsEnabled = true;
            VirgilMessageRequested?.Invoke("Processus ignore.\nAucune action executee.");
            return Task.CompletedTask;
        }));
        stack.Children.Add(actions);
        card.Child = stack;
        return card;
    }

    private Button CreateButton(string content, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = content,
            Style = TryFindResource(primary ? "VirgilPrimaryButton" : "VirgilSecondaryButton") as Style,
            Margin = new Thickness(0, 0, 8, 6),
            MinWidth = 125
        };
        button.Click += async (_, _) => await action().ConfigureAwait(true);
        return button;
    }

    private void RenderEmptyProcesses()
    {
        ProcessesPanel.Children.Clear();
        ProcessesPanel.Children.Add(CreateTextCard(
            "Analyse requise. Les processus systeme critiques, securite, VPN et materiel resteront proteges."));
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

    private bool BeginOperation(string status)
    {
        if (_operationInProgress)
        {
            VirgilMessageRequested?.Invoke("Operation deja en cours.");
            return false;
        }

        _operationInProgress = true;
        _operationCancellation = new CancellationTokenSource();
        ResourcesStatusText.Text = status;
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
    }

    private void SetButtonsEnabled(bool enabled)
    {
        AnalyzeResourcesButton.IsEnabled = enabled;
        HeavyProcessesButton.IsEnabled = enabled;
        ReleaseInactiveMemoryButton.IsEnabled = enabled;
        RestartExplorerButton.IsEnabled = enabled;
        ReturnHomeButton.IsEnabled = enabled;
        ProcessesPanel.IsEnabled = enabled;
        ViewResourcesReportButton.IsEnabled = enabled && HasReportData();
    }

    private void RecordResult(ProcessActionResult result)
    {
        _actions.Add(result);
        if (!string.IsNullOrWhiteSpace(result.ReadableError))
        {
            _errors.Add(result.ReadableError);
        }

        ViewResourcesReportButton.IsEnabled = true;
    }

    private void ShowReport()
    {
        if (!HasReportData())
        {
            VirgilMessageRequested?.Invoke("Aucun rapport ressources disponible.");
            return;
        }

        ResourcesReportText.Text = _reportBuilder.Build(BuildSessionReport(), includeTechnicalDetails: false);
        ResourcesReportOverlay.Visibility = Visibility.Visible;
        CloseResourcesReportButton.Focus();
    }

    private void HideReport()
    {
        ResourcesReportOverlay.Visibility = Visibility.Collapsed;
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

    private bool HasReportData()
    {
        return _analyses.Count > 0 || _actions.Count > 0 || _skippedActions.Count > 0 || _errors.Count > 0;
    }

    private void RequestVirgilState(VirgilCoreState state, string label)
    {
        ResourcesMiniCore.SetState(state);
        VirgilStateRequested?.Invoke(state, label);
    }

    private static string BuildAnalysisMessage(ResourceAnalysisReport report)
    {
        var heavy = report.TopMemoryProcesses
            .Concat(report.TopCpuProcesses)
            .GroupBy(process => process.ProcessId)
            .Count(group => group.First().Status == ProcessResourceStatus.Heavy);
        return $"Analyse ressources terminee.\nRAM : {report.AverageMemoryPercent:0.0} %\n" +
            $"CPU moyen : {report.AverageCpuPercent:0.0} %\n{heavy} processus lourds detectes.";
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
            ProcessActionKind.KillProcess => "Fermeture forcee",
            ProcessActionKind.OpenLocation => "Ouverture emplacement",
            ProcessActionKind.RestartExplorer => "Relance Explorer",
            ProcessActionKind.ReleaseInactiveMemory => "Memoire inactive",
            _ => action.ToString()
        };
    }

    private static string StatusLabel(ProcessResourceStatus status)
    {
        return status switch
        {
            ProcessResourceStatus.Heavy => "LOURD",
            ProcessResourceStatus.Review => "A VERIFIER",
            ProcessResourceStatus.Protected => "PROTEGE",
            ProcessResourceStatus.System => "SYSTEME PROTEGE",
            _ => "NORMAL"
        };
    }

    private static string HealthLabel(ResourceHealthLevel health)
    {
        return health switch
        {
            ResourceHealthLevel.Stable => "STABLE",
            ResourceHealthLevel.Watch => "A SURVEILLER",
            ResourceHealthLevel.InterventionRecommended => "INTERVENTION CONSEILLEE",
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
