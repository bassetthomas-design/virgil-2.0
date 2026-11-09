using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Contracts.Actions;
using Virgil.Contracts;
using Virgil.Core.Actions;
using Virgil.Core.Updates;
using Virgil.App.Animation;

namespace Virgil.App.ViewModels;

public partial class UpdatesViewModel
{
    private readonly IUpdateSequenceService? _sequenceService;
    private readonly WingetClientModuleService? _wingetModuleService;
    [ObservableProperty] private bool isModuleInstallVisible;
    private IReadOnlyList<UpdateSequenceStep> _confirmedSequenceSteps = Array.Empty<UpdateSequenceStep>();
    private string? _sequenceReport;
    public ObservableCollection<UpdateSequenceChoiceViewModel> SequenceChoices { get; } = new();
    public ObservableCollection<string> SequenceProgress { get; } = new();
    private readonly Dictionary<string, int> _sequenceProgressIndices = new(StringComparer.Ordinal);
    private string? _sequenceFinalSummary;
    private bool _sequenceWasCancelled;
    [ObservableProperty] private bool isSequencePreview;
    [ObservableProperty] private bool isRunAllEnabled;
    [ObservableProperty] private string sequenceStepText = "Aucune séquence en cours.";
    [ObservableProperty] private bool rebootRequired;
    public string RebootStatusText => RebootRequired ? "REDÉMARRAGE WINDOWS REQUIS — Redémarrez puis relancez « Tout mettre à jour » pour terminer." : "Aucun redémarrage en attente.";
    partial void OnRebootRequiredChanged(bool value) => OnPropertyChanged(nameof(RebootStatusText));

    [RelayCommand]
    private async Task InstallWingetModuleAsync()
    {
        if (_wingetModuleService is null || !BeginOperation("Préparation du module WinGet…")) return;
        try
        {
            var preview = WingetClientModuleService.CreatePreview();
            ValidationTitle = preview.Title;
            ValidationDetails = "Le module Microsoft.WinGet.Client fournit un inventaire indépendant de la langue de Windows.";
            ValidationCommand = string.Join("\n", preview.Effects);
            ValidationSafety = "Installation pour l’utilisateur courant, après votre confirmation. La lecture du texte WinGet reste disponible sans ce module.";
            ExecuteLabel = "CONFIRMER L’INSTALLATION";
            SkipLabel = "ANNULER";
            IsCancelSequenceVisible = false;
            var decision = new TaskCompletionSource<UpdateDecision>();
            _activeDecision = decision;
            IsValidationOpen = true;
            if (await decision.Task != UpdateDecision.Execute) return;
            _operationCancellation!.Token.ThrowIfCancellationRequested();
            var plan = _confirmationService.Issue(preview, new ConfirmationConsent(ConfirmationLevel.Reinforced, null));
            var success = await _wingetModuleService.InstallAsync(plan, _operationCancellation.Token);
            StatusText = success ? "Module installé. Relancez le scan des mises à jour." : "Module non installé. La lecture du texte WinGet reste disponible.";
            IsModuleInstallVisible = !success;
        }
        catch (OperationCanceledException) { StatusText = "Installation du module annulée."; }
        catch (ConfirmationRefusedException) { StatusText = "Confirmation refusée. Aucun module installé."; }
        catch { StatusText = "Installation du module indisponible. La lecture du texte WinGet reste disponible."; }
        finally { EndOperation(); }
    }

    [RelayCommand]
    private void CancelUpdateOperation()
    {
        CancelActiveOperation();
    }

    [RelayCommand]
    private async Task RunAllAsync()
    {
        if (_sequenceService is null || !BeginOperation("Préparation de l’aperçu des mises à jour…")) return;
        Operation.Start(StatusText, immediately: true, nonInterruptible: true);
        SequenceProgress.Clear();
        _sequenceProgressIndices.Clear();
        _sequenceFinalSummary = null;
        _sequenceWasCancelled = false;
        Operation.Commentary = "Préparation des catégories de mises à jour…";
        RebootRequired = false;
        try
        {
            using var previewCancellation = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(_operationCancellation!.Token);
            UpdateScanReport scan;
            try
            {
                scan = await _scanService.ScanAsync(UpdateScanRequest.DeepPreview, CreateTextProgress(), previewCancellation.Token).WaitAsync(TimeSpan.FromSeconds(45), _operationCancellation.Token);
            }
            catch (TimeoutException)
            {
                previewCancellation.Cancel();
                scan = new UpdateScanReport { Items = _lastItems.ToArray() };
            }
            _operationCancellation!.Token.ThrowIfCancellationRequested();
            _lastScanReport = scan;
            _lastItems.Clear();
            _lastItems.AddRange(scan.Items);
            RenderScanReport(scan);
            StatusText = "Préparation des catégories de mises à jour…";
            Operation.Report(new Virgil.Core.Operations.OperationProgress(StatusText));
            var steps = await _sequenceService.ScanAsync(_lastItems.ToArray(), _operationCancellation.Token);
            _operationCancellation.Token.ThrowIfCancellationRequested();
            SequenceChoices.Clear();
            foreach (var step in steps)
                SequenceChoices.Add(new UpdateSequenceChoiceViewModel(step, UpdateSequencePreview));
            _confirmedSequenceSteps = Array.Empty<UpdateSequenceStep>();
            IsSequencePreview = true;
            ValidationTitle = "TOUT METTRE À JOUR";
            ExecuteLabel = "CONFIRMER ET METTRE À JOUR";
            SkipLabel = "ANNULER";
            IsCancelSequenceVisible = false;
            UpdateSequencePreview();
            var decision = new TaskCompletionSource<UpdateDecision>();
            _activeDecision = decision;
            ValidationCoreState = VirgilCoreState.SensitiveAction;
            IsValidationOpen = true;
            if (await decision.Task != UpdateDecision.Execute) return;
            _operationCancellation.Token.ThrowIfCancellationRequested();
            var preview = _sequenceService.CreatePreview(_confirmedSequenceSteps);
            var level = preview.RiskLevel == RiskLevel.Critical ? ConfirmationLevel.Critical
                : preview.RiskLevel == RiskLevel.Sensitive ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple;
            var plan = _confirmationService.Issue(preview, new ConfirmationConsent(level, null));
            IsSequencePreview = false;
            RequestVirgilState(VirgilCoreState.Executing, "MISES À JOUR");
            foreach (var step in _confirmedSequenceSteps.Where(step => !step.IsInformational))
                Operation.SetStep(step.Title, step.Title, "Non lancée");
            var acceptingProgress = true;
            var sequenceCancellation = _operationCancellation;
            var result = await _sequenceService.ExecuteAsync(_confirmedSequenceSteps.Concat(Array.Empty<UpdateSequenceStep>()).ToArray(), plan,
                new Progress<UpdateSequenceProgress>(progress =>
                {
                    if (!acceptingProgress || !ReferenceEquals(sequenceCancellation, _operationCancellation) || !Operation.IsRunning || Operation.IsCancelling) return;
                    Operation.Report(new Virgil.Core.Operations.OperationProgress(progress.Title, progress.Current, progress.Total));
                    SequenceStepText = $"Étape {progress.Current}/{progress.Total} — {progress.Title} : {progress.Status}";
                    StatusText = SequenceStepText;
                    var key = $"{progress.Current}:{progress.Title}";
                    if (_sequenceProgressIndices.TryGetValue(key, out var index)) SequenceProgress[index] = SequenceStepText;
                    else { _sequenceProgressIndices.Add(key, SequenceProgress.Count); SequenceProgress.Add(SequenceStepText); }
                    Operation.SetStep(progress.Title, progress.Title, "En cours", progress.Status);
                    Operation.Commentary = progress.Status;
                }), _operationCancellation.Token);
            acceptingProgress = false;
            _sequenceWasCancelled = result.WasCancelled;
            RebootRequired = result.RebootRequired;
            StatusText = result.Message;
            SequenceStepText = result.Message;
            _sequenceReport = result.Message + "\n" + string.Join("\n", result.Steps.Select(step => step.Step.Title + " : " + step.Message))
                + $"\nRedémarrage nécessaire : {(result.RebootRequired ? "oui" : "non")}.";
            foreach (var step in result.Steps)
                Operation.SetStep(step.Step.Title, step.Step.Title, SequenceResultStatus(step), step.Message);
            _sequenceFinalSummary = result.Message + "\n" + string.Join("\n", result.Steps.Select(step => step.Step.Title + " : " + SequenceResultStatus(step)))
                + $"\nRedémarrage nécessaire : {(result.RebootRequired ? "oui" : "non")}.";
            Operation.Commentary = result.Message;
            await PersistReportAsync(new ReportEntry
            {
                Kind = ReportKind.Updates, Module = "Mises à jour", Title = "Tout mettre à jour",
                Summary = result.Message, SimpleView = _sequenceReport, RestartRequired = result.RebootRequired,
                Severity = result.WasRefused || result.HasFailures ? ReportSeverity.Warning : ReportSeverity.Success,
                SkippedActions = result.Steps.Where(step => step.Step.IsInformational || step.WasSkipped || step.WasUnavailable).Select(step => new ReportAction
                {
                    Name = step.Step.Title, Status = ReportActionStatus.InformationOnly, Result = step.WasUnavailable ? "Non disponible sur ce PC (information)" : step.Message
                }).ToArray(),
                ExecutedActions = result.Steps.Where(step => !step.Step.IsInformational && !step.WasSkipped && !step.WasUnavailable).Select(step => new ReportAction
                {
                    Name = step.Step.Title, Status = step.Succeeded ? ReportActionStatus.Executed : ReportActionStatus.Failed,
                    Result = step.Message, RestartRequired = step.RebootRequired
                }).ToArray()
            });
            RequestVirgilState(result.WasRefused || result.HasFailures ? VirgilCoreState.Warning : result.WasCancelled ? VirgilCoreState.Idle : VirgilCoreState.Success, "MISES À JOUR");
            VirgilMessageRequested?.Invoke(_sequenceReport);
        }
        catch (ConfirmationRefusedException)
        {
            StatusText = "Confirmation refusée. Aucune mise à jour lancée.";
        }
        catch (OperationCanceledException)
        {
            _sequenceWasCancelled = true;
            StatusText = "Séquence annulée. Les étapes suivantes n’ont pas été lancées.";
            SequenceStepText = StatusText;
        }
        catch
        {
            StatusText = "La séquence n’a pas pu se terminer. Consultez les étapes ci-dessous.";
            SequenceStepText = StatusText;
            RequestVirgilState(VirgilCoreState.Warning, "ATTENTION");
        }
        finally
        {
            IsSequencePreview = false;
            EndOperation();
        }
    }

    private static string SequenceResultStatus(UpdateSequenceStepResult step) =>
        step.WasUnavailable ? "Source indisponible" : step.WasSkipped ? "Sautée"
        : step.Message.StartsWith("Non lancée", StringComparison.Ordinal) ? "Non lancée"
        : step.Succeeded ? "Terminée" : "En échec";

    private void UpdateSequencePreview()
    {
        var needsRestore = SequenceChoices.Any(choice => choice.IsSelected && UpdateSequenceService.IsElevated(choice.Step.Component));
        foreach (var required in SequenceChoices.Where(choice => choice.Step.Component is UpdateComponent.Restore or UpdateComponent.PendingReboot))
            required.IsSelected = required.Step.Component == UpdateComponent.PendingReboot || needsRestore;
        var selected = SequenceChoices.Where(choice => choice.IsSelected && choice.CanSelect).Select(choice => choice.Step).ToArray();
        ValidationDetails = "Les pilotes Windows Update sont cochés par défaut. Décochez-les pour les laisser de côté. Un point de restauration est créé avant toute installation. Certaines étapes ne peuvent pas être interrompues une fois lancées.";
        if (_sequenceService is null || selected.Length == 0)
        {
            ValidationCommand = "Aucun composant sélectionné.";
            ValidationSafety = "Aucune action sans confirmation.";
            return;
        }
        var preview = _sequenceService.CreatePreview(selected);
        ValidationCommand = string.Join("\n", preview.Effects);
        ValidationSafety = "Un point de restauration récent (moins de 24 h) pourra être réutilisé si Windows refuse d'en créer un.\nOffice Click-to-Run se met à jour lui-même.\nAvant les paquets et la passe finale : winget source update. La passe finale installe tout ce que WinGet trouve, y compris hors de la liste affichée.\nCertaines étapes ne peuvent pas être interrompues une fois lancées.\nUne seule confirmation pour les composants cochés. Annulation possible entre les étapes. Aucun redémarrage automatique.\n"
            + string.Join("\n", preview.RequiredBackups);
    }
}

public partial class UpdateSequenceChoiceViewModel : ObservableObject
{
    private readonly Action _changed;
    public UpdateSequenceChoiceViewModel(UpdateSequenceStep step, Action changed)
    {
        Step = step;
        _changed = changed;
        isSelected = step.Component is not UpdateComponent.Restore and not UpdateComponent.PendingReboot;
    }
    public UpdateSequenceStep Step { get; }
    public bool CanSelect => Step.Component is not UpdateComponent.Restore and not UpdateComponent.PendingReboot;
    public string AutomationId => "UpdatePlanStep." + (Step.Component switch
    {
        UpdateComponent.Winget => "Winget." + Step.Id,
        UpdateComponent.Windows => "WindowsSoftware",
        UpdateComponent.Driver => "Drivers",
        _ => Step.Component.ToString()
    });
    public bool HasHelp => Step.Component == UpdateComponent.WindowsFeatureUpgrade;
    public string HelpText => HasHelp ? "Opération longue, redémarrage obligatoire ; dernière étape de l'élévation Windows Update." : string.Empty;
    public string Title => Step.Application is { } app
        ? $"{Step.Title} : {app.InstalledVersion} → {app.AvailableVersion} ({UpdatesViewModel.SourceLabel(app.Source)})"
        : Step.Title;
    [ObservableProperty] private bool isSelected;
    partial void OnIsSelectedChanged(bool value)
    {

        _changed();
    }
}
