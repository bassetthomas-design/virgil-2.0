using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
using Virgil.App.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Virgil.Core.Interventions;
using Virgil.Core.Reports;
using Virgil.Contracts;

namespace Virgil.App.ViewModels;

public partial class InterventionsViewModel : ObservableObject
{
    private readonly IInterventionDiagnosticService _diagnosticService;
    private readonly IInterventionExecutionService _executionService;
    private readonly InterventionReportBuilder _reportBuilder;
    private readonly List<InterventionDiagnostic> _diagnostics = new();
    private readonly Dictionary<InterventionId, InterventionItemViewModel> _selectionById = new();
    private readonly IConfirmationService _confirmationService;
    private readonly IReportHistoryService _reportHistoryService;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource<InterventionDecision>? _activeDecision;
    private InterventionSessionReport? _lastReport;
    private bool _operationInProgress;
    private InterventionExecutionResult? _verificationResult;
    private string _lastProgressMessage = string.Empty;
    private DateTimeOffset _lastProgressAt;

    public InterventionsViewModel(
        IInterventionDiagnosticService diagnosticService,
        IInterventionExecutionService executionService,
        InterventionReportBuilder reportBuilder,
        IReportHistoryService reportHistoryService,
        IConfirmationService confirmationService)
    {
        _confirmationService = confirmationService;
        _diagnosticService = diagnosticService;
        _executionService = executionService;
        _reportBuilder = reportBuilder;
        _reportHistoryService = reportHistoryService;
        RenderDiagnostics();
    }

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public event EventHandler? ReportPersisted;

    public ObservableCollection<InterventionItemViewModel> SystemItems { get; } = new();
    public ObservableCollection<InterventionItemViewModel> NetworkItems { get; } = new();
    public ObservableCollection<InterventionItemViewModel> StorageItems { get; } = new();
    public ObservableCollection<InterventionItemViewModel> InterfaceItems { get; } = new();

    [ObservableProperty] private bool includeDeepScan;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string elapsedText = string.Empty;
    [ObservableProperty] private string typedConfirmation = string.Empty;
    [ObservableProperty] private string criticalConfirmationPrompt = string.Empty;
    [RelayCommand] private void CancelOperation() => CancelActiveOperation();

    [ObservableProperty] private string statusText = "DIAGNOSTIC, VALIDATION ET EXECUTION";
    [ObservableProperty] private string overallText = "NON ANALYSÉ";
    [ObservableProperty] private string recommendedCountText = "0";
    [ObservableProperty] private string systemNotice = string.Empty;
    [ObservableProperty] private string networkNotice = string.Empty;
    [ObservableProperty] private string storageNotice = string.Empty;
    [ObservableProperty] private string interfaceNotice = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isBusy;
    public bool IsIdle => !IsBusy;
    [ObservableProperty] private bool isRunGuidedEnabled;
    [ObservableProperty] private bool isReportButtonEnabled;
    [ObservableProperty] private bool isValidationOpen;
    [ObservableProperty] private string validationTitle = "VALIDATION REQUISE";
    [ObservableProperty] private string validationDetails = string.Empty;
    [ObservableProperty] private string validationCommand = string.Empty;
    [ObservableProperty] private string validationSafety = string.Empty;
    [ObservableProperty] private string executeActionLabel = "EXECUTER";
    [ObservableProperty] private bool isReportOpen;
    [ObservableProperty] private string reportText = string.Empty;
    [ObservableProperty] private VirgilCoreState miniCoreState = VirgilCoreState.Idle;
    [ObservableProperty] private VirgilCoreState validationCoreState = VirgilCoreState.Idle;

    public void CancelActiveOperation()
    {
        _operationCancellation?.Cancel();
        CompleteDecision(InterventionDecision.CancelAll);
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
            CompleteDecision(InterventionDecision.Skip);
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
    private void ViewReport() => ShowReport();

    [RelayCommand]
    private void ReturnHome() => ReturnHomeRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ExecuteDecision() => CompleteDecision(InterventionDecision.Execute);

    [RelayCommand]
    private void SkipDecision() => CompleteDecision(InterventionDecision.Skip);

    [RelayCommand]
    private void CancelDecision() => CompleteDecision(InterventionDecision.CancelAll);

    [RelayCommand]
    private async Task VerifyHealthAsync()
    {
        if (!BeginOperation("Vérification en attente de confirmation de l’élévation.")) return;
        var started = DateTimeOffset.Now;
        var results = new List<InterventionExecutionResult>();
        var errors = new List<string>();
        var checks = new InterventionDiagnostic
        {
            Definition = new InterventionCatalog().Get(IncludeDeepScan ? InterventionId.VerifyHealthDeep : InterventionId.VerifyHealth),
            IsAvailable = true, StateBefore = "Non vérifié"
        };
        try
        {
            var decision = await AskDecisionAsync(checks).ConfigureAwait(true);
            if (decision != InterventionDecision.Execute)
            {
                results.Add(decision == InterventionDecision.Skip ? _executionService.Skip(checks) : _executionService.Cancel(checks));
                return;
            }
            var plan = _confirmationService.Issue(InterventionActionPreviewFactory.Create(checks.Definition),
                new ConfirmationConsent(ConfirmationLevel.Simple, null));
            _verificationResult = await _executionService.ExecuteAsync(checks, plan, CreateProgress(), _operationCancellation!.Token).ConfigureAwait(true);
            results.Add(_verificationResult);
            StatusText = _verificationResult.ReadableError ?? "Vérification terminée. Consultez le rapport détaillé.";
            VirgilMessageRequested?.Invoke(StatusText);
            var available = await _diagnosticService.DiagnoseAllAsync(_operationCancellation.Token).ConfigureAwait(true);
            var repairs = InterventionSafety.RepairsFor(_verificationResult.SummaryOutput);
            _diagnostics.Clear();
            _diagnostics.AddRange(available.Where(item => repairs.Contains(item.Definition.Id))
                .Select(item => item with { Status = InterventionStatus.Recommended, StateBefore = "Constat issu de la vérification ; voir le rapport." }));
            RenderDiagnostics();
        }
        catch (OperationCanceledException) { errors.Add("Vérification annulée."); }
        catch (Exception ex) { errors.Add("Vérification indisponible : " + ex.Message); }
        finally
        {
            _lastReport = _executionService.CreateReport(started, [checks], results, errors);
            await PersistReportAsync(ReportMapper.FromInterventions(_lastReport));
            EndOperation();
            if (errors.Count > 0) { StatusText = string.Join("\n", errors); VirgilMessageRequested?.Invoke(StatusText); }
            ShowReport();
        }
    }

    [RelayCommand]
    private async Task ScanRepairAsync()
    {
        if (_operationInProgress) return;
        _verificationResult = null;
        await VerifyHealthAsync().ConfigureAwait(true);
        if (_verificationResult?.Status != InterventionStatus.Completed || !GuidedCandidates().Any()) return;
        IsReportOpen = false;
        await RunGuidedAsync().ConfigureAwait(true);
        // A fresh, separately confirmed read-only check reports the actual post-repair state.
        if (_lastReport?.Results.Any(result => result.WasConfirmed && result.Status is InterventionStatus.Completed or InterventionStatus.RebootRequired) == true)
        {
            var repairReport = _lastReport;
            IsReportOpen = false;
            await VerifyHealthAsync().ConfigureAwait(true);
            if (repairReport is not null && _lastReport is not null)
            {
                _lastReport = _executionService.CreateReport(repairReport.StartedAt, repairReport.ProposedActions,
                    repairReport.Results.Concat(_lastReport.Results).ToArray(), repairReport.Errors.Concat(_lastReport.Errors).ToArray());
                await PersistReportAsync(ReportMapper.FromInterventions(_lastReport));
                ShowReport();
            }
        }
    }

    private IProgress<InterventionProgress> CreateProgress() => new Progress<InterventionProgress>(item =>
    {
        Progress = item.Percent ?? 0;
        ElapsedText = $"Temps écoulé : {item.Elapsed:hh\\:mm\\:ss}";
        StatusText = item.Step + (item.Percent.HasValue ? $" — {item.Percent} %" : " — en cours…");
        if (StatusText != _lastProgressMessage || DateTimeOffset.Now - _lastProgressAt >= TimeSpan.FromSeconds(10))
        {
            _lastProgressMessage = StatusText;
            _lastProgressAt = DateTimeOffset.Now;
            VirgilMessageRequested?.Invoke(StatusText + " · " + ElapsedText);
        }
    });

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (!BeginOperation("Diagnostic interventions en cours."))
        {
            return;
        }

        try
        {
            RequestVirgilState(VirgilCoreState.Scanning, "INTERVENTIONS");
            VirgilMessageRequested?.Invoke("Diagnostic interventions lance.\nAucune action executee.");

            var diagnostics = await _diagnosticService
                .DiagnoseAllAsync(_operationCancellation!.Token)
                .ConfigureAwait(true);
            _diagnostics.Clear();
            _diagnostics.AddRange(diagnostics);
            RenderDiagnostics();
            RequestVirgilState(VirgilCoreState.Success, "PRET");
            AnnounceDiagnostics();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Diagnostic annule.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Diagnostic annule.\nAucune action executee.");
        }
        catch
        {
            StatusText = "Diagnostic indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Diagnostic indisponible.\nAucune action executee.");
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand]
    private async Task RunGuidedAsync()
    {
        if (_operationInProgress)
        {
            VirgilMessageRequested?.Invoke("Operation deja en cours.");
            return;
        }

        var candidates = GuidedCandidates().ToList();
        if (candidates.Count == 0)
        {
            VirgilMessageRequested?.Invoke("Aucune intervention selectionnee.");
            return;
        }

        if (!BeginOperation("Parcours guide en attente de validation."))
        {
            return;
        }

        var startedAt = DateTimeOffset.Now;
        var results = new List<InterventionExecutionResult>();
        var errors = new List<string>();

        try
        {
            foreach (var diagnostic in candidates.OrderBy(diagnostic => diagnostic.Definition.DisplayOrder))
            {
                _operationCancellation!.Token.ThrowIfCancellationRequested();
                var decision = await AskDecisionAsync(diagnostic).ConfigureAwait(true);

                ConfirmedPlan? plan = null;
                if (decision == InterventionDecision.Execute)
                {
                    try
                    {
                        plan = _confirmationService.Issue(InterventionActionPreviewFactory.Create(diagnostic.Definition),
                            new ConfirmationConsent(diagnostic.Definition.RiskLevel == InterventionRiskLevel.Sensitive
                                ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple, null));
                    }
                    catch (ConfirmationRefusedException)
                    {
                        decision = InterventionDecision.CancelAll;
                    }
                }

                if (decision == InterventionDecision.CancelAll)
                {
                    results.Add(_executionService.Cancel(diagnostic));
                    errors.Add("Parcours annule par l'utilisateur.");
                    break;
                }

                if (decision == InterventionDecision.Skip)
                {
                    results.Add(_executionService.Skip(diagnostic));
                    VirgilMessageRequested?.Invoke("Intervention passee.\nAucune action executee.");
                    continue;
                }

                RequestVirgilState(VirgilCoreState.Executing, "EXECUTION");
                SetButtonsEnabled(false);

                var result = await _executionService
                    .ExecuteAsync(diagnostic, plan, CreateProgress(), _operationCancellation.Token)
                    .ConfigureAwait(true);

                results.Add(result);
                StatusText = result.ReadableError ?? ResultMessage(result);
                VirgilMessageRequested?.Invoke(StatusText);
                if (result.ReadableError?.StartsWith("Réparation bloquée : point de restauration", StringComparison.Ordinal) == true)
                {
                    var unprotected = diagnostic with { Definition = diagnostic.Definition with { AllowWithoutRestorePoint = true } };
                    var retry = await AskDecisionAsync(unprotected).ConfigureAwait(true);
                    if (retry == InterventionDecision.Execute)
                    {
                        try
                        {
                            var overridePlan = _confirmationService.Issue(InterventionActionPreviewFactory.Create(unprotected.Definition),
                                new ConfirmationConsent(ConfirmationLevel.Critical, TypedConfirmation));
                            var retried = await _executionService.ExecuteAsync(unprotected, overridePlan, CreateProgress(), _operationCancellation.Token).ConfigureAwait(true);
                            results.Add(retried);
                            StatusText = retried.ReadableError ?? ResultMessage(retried);
                            VirgilMessageRequested?.Invoke(StatusText);
                        }
                        catch (ConfirmationRefusedException) { errors.Add("Confirmation Critique incorrecte : réparation sans point de restauration refusée."); }
                    }
                    else if (retry == InterventionDecision.CancelAll) break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            errors.Add("Parcours annule.");
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
        }
        catch
        {
            errors.Add("Erreur pendant le parcours guide.");
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
        }
        finally
        {
            _lastReport = _executionService.CreateReport(startedAt, candidates, results, errors);
            await PersistReportAsync(ReportMapper.FromInterventions(_lastReport));
            EndOperation();
            CompleteSessionState(_lastReport);
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
        UpdateCommandState();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        IsRunGuidedEnabled = enabled && GuidedCandidates().Any();
        IsReportButtonEnabled = enabled && _lastReport is not null;
    }

    private void UpdateCommandState()
    {
        IsRunGuidedEnabled = !_operationInProgress && GuidedCandidates().Any();
        IsReportButtonEnabled = !_operationInProgress && _lastReport is not null;
    }

    private Task<InterventionDecision> AskDecisionAsync(InterventionDiagnostic diagnostic)
    {
        _activeDecision = new TaskCompletionSource<InterventionDecision>();
        ShowValidation(diagnostic);
        return _activeDecision.Task;
    }

    private void ShowValidation(InterventionDiagnostic diagnostic)
    {
        var definition = diagnostic.Definition;
        TypedConfirmation = string.Empty;
        CriticalConfirmationPrompt = definition.AllowWithoutRestorePoint
            ? "Aucun point de restauration. Pour continuer sans cette protection, recopiez : " +
                ConfirmationService.ExpectedCriticalText(InterventionActionPreviewFactory.Create(definition)) : string.Empty;
        ValidationTitle = definition.RiskLevel == InterventionRiskLevel.Sensitive
            ? "ACTION SENSIBLE"
            : "VALIDATION REQUISE";
        ValidationDetails = string.Join("\n", new[]
        {
            definition.Title,
            definition.Description,
            $"Effet attendu : {definition.ExpectedEffect}",
            $"Non touche : {definition.NotTouched}",
            $"Risque : {RiskLabel(definition.RiskLevel)}",
            $"Admin : {(definition.RequiresAdministrator ? "oui" : "non")}",
            $"Redemarrage possible : {(definition.RebootPossible ? "oui" : "non")}",
            $"Diagnostic : {diagnostic.StateBefore}",
            $"Recommandation : {diagnostic.Recommendation}"
        });
        ValidationCommand = FormatCommands(diagnostic);
        ValidationSafety = string.Join("\n", diagnostic.Warnings
            .Concat(new[]
            {
                "Validation separee obligatoire.",
                "Aucune commande libre.",
                "Aucun redemarrage automatique.",
                "Aucun Take Ownership dans cette PR."
            }));
        ExecuteActionLabel = definition.RiskLevel == InterventionRiskLevel.Sensitive
            ? "JE CONFIRME"
            : "EXECUTER";
        IsValidationOpen = true;
        ValidationCoreState = definition.RiskLevel == InterventionRiskLevel.Sensitive
            ? VirgilCoreState.SensitiveAction
            : VirgilCoreState.Warning;
        RequestVirgilState(definition.RiskLevel == InterventionRiskLevel.Sensitive
            ? VirgilCoreState.SensitiveAction
            : VirgilCoreState.Warning,
            "VALIDATION");
    }

    private void CompleteDecision(InterventionDecision decision)
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

    private void RenderDiagnostics()
    {
        SystemItems.Clear();
        NetworkItems.Clear();
        StorageItems.Clear();
        InterfaceItems.Clear();
        _selectionById.Clear();

        if (_diagnostics.Count == 0)
        {
            SystemNotice = "Lancez une analyse pour afficher les actions systeme.";
            NetworkNotice = "Lancez une analyse pour afficher les actions reseau.";
            StorageNotice = "Lancez une analyse pour afficher les actions stockage.";
            InterfaceNotice = "Lancez une analyse pour afficher les actions interface.";
            OverallText = "NON ANALYSÉ";
            RecommendedCountText = "0";
            UpdateCommandState();
            return;
        }

        SystemNotice = NetworkNotice = StorageNotice = InterfaceNotice = string.Empty;
        foreach (var diagnostic in _diagnostics.OrderBy(diagnostic => diagnostic.Definition.DisplayOrder))
        {
            var item = new InterventionItemViewModel(diagnostic, UpdateCommandState);
            _selectionById[diagnostic.Definition.Id] = item;
            ItemsFor(diagnostic.Definition.Category).Add(item);
        }

        var available = _diagnostics.Count(diagnostic => diagnostic.IsAvailable);
        var recommended = _diagnostics.Count(diagnostic => diagnostic.Status == InterventionStatus.Recommended);
        OverallText = $"{available} disponibles. Aucune execution automatique.";
        RecommendedCountText = recommended.ToString();
        UpdateCommandState();
    }

    private ObservableCollection<InterventionItemViewModel> ItemsFor(InterventionCategory category)
    {
        return category switch
        {
            InterventionCategory.Network => NetworkItems,
            InterventionCategory.Storage => StorageItems,
            InterventionCategory.Interface => InterfaceItems,
            _ => SystemItems
        };
    }

    private IEnumerable<InterventionDiagnostic> GuidedCandidates()
    {
        var selected = _diagnostics
            .Where(diagnostic => _selectionById.TryGetValue(diagnostic.Definition.Id, out var item) &&
                item.IsSelected &&
                diagnostic.IsAvailable)
            .ToList();

        if (selected.Count > 0)
        {
            return selected;
        }

        return _diagnostics.Where(diagnostic =>
            diagnostic.Status == InterventionStatus.Recommended &&
            diagnostic.Definition.RiskLevel != InterventionRiskLevel.Sensitive);
    }

    private void AnnounceDiagnostics()
    {
        var available = _diagnostics.Count(diagnostic => diagnostic.IsAvailable);
        var recommended = _diagnostics.Count(diagnostic => diagnostic.Status == InterventionStatus.Recommended);
        VirgilMessageRequested?.Invoke($"Diagnostic termine.\n{available} actions disponibles.\n{recommended} recommandees.");
    }

    private void CompleteSessionState(InterventionSessionReport report)
    {
        if (report.CancelledActions > 0)
        {
            StatusText = "Parcours annule.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            return;
        }

        if (report.Failures > 0 || report.Errors.Count > 0)
        {
            StatusText = report.Results.LastOrDefault(result => !string.IsNullOrWhiteSpace(result.ReadableError))?.ReadableError
                ?? string.Join("\n", report.Errors.DefaultIfEmpty("Parcours terminé avec erreurs."));
            RequestVirgilState(VirgilCoreState.Warning, "ATTENTION");
            return;
        }

        StatusText = "Parcours termine.";
        RequestVirgilState(VirgilCoreState.Success, "TERMINE");
    }

    private void ShowReport()
    {
        if (_lastReport is null)
        {
            VirgilMessageRequested?.Invoke("Aucun rapport interventions disponible.");
            return;
        }

        ReportText = _reportBuilder.Build(_lastReport);
        IsReportOpen = true;
    }

    [RelayCommand]
    private void CloseReport()
    {
        IsReportOpen = false;
    }

    private void RequestVirgilState(VirgilCoreState state, string label)
    {
        MiniCoreState = state;
        VirgilStateRequested?.Invoke(state, label);
    }

    private static string ResultMessage(InterventionExecutionResult result)
    {
        return result.Status switch
        {
            InterventionStatus.Completed => "Intervention terminee.\nRapport mis a jour.",
            InterventionStatus.RebootRequired => "Intervention terminee.\nRedemarrage manuel probablement requis.",
            InterventionStatus.PartialFailure => "Intervention partielle.\nRapport disponible.",
            InterventionStatus.Failed => "Intervention en echec.\nRapport disponible.",
            _ => "Intervention traitee.\nRapport disponible."
        };
    }

    private static string FormatCommands(InterventionDiagnostic diagnostic)
    {
        if (diagnostic.Definition.CommandPreviews.Count == 0)
        {
            return "Aucune commande externe.";
        }

        return string.Join("\n", diagnostic.Definition.CommandPreviews.Select(command =>
        {
            var args = command.Arguments.Select(argument => ResolveArgument(argument, diagnostic));
            return command.Executable + " " + string.Join(" ", args);
        }));
    }

    private static string ResolveArgument(string argument, InterventionDiagnostic diagnostic)
    {
        if (argument == "<system-drive>" &&
            diagnostic.TechnicalData.TryGetValue("SystemDrive", out var systemDrive))
        {
            return systemDrive;
        }

        return argument.Contains(' ') ? "\"" + argument + "\"" : argument;
    }

    internal static string RiskLabel(InterventionRiskLevel risk)
    {
        return risk switch
        {
            InterventionRiskLevel.Low => "faible",
            InterventionRiskLevel.Moderate => "modere",
            _ => "sensible"
        };
    }

    internal static string StatusLabel(InterventionStatus status)
    {
        return status switch
        {
            InterventionStatus.Available => "disponible",
            InterventionStatus.Recommended => "recommande",
            InterventionStatus.Unavailable => "indisponible",
            InterventionStatus.Completed => "terminee",
            InterventionStatus.PartialFailure => "partielle",
            InterventionStatus.Failed => "echec",
            InterventionStatus.Skipped => "passee",
            InterventionStatus.Cancelled => "annulee",
            InterventionStatus.RebootRequired => "redemarrage requis",
            _ => status.ToString()
        };
    }

    private enum InterventionDecision
    {
        Execute,
        Skip,
        CancelAll
    }
}
