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
    [ObservableProperty] private bool isSequencePreview;
    [ObservableProperty] private bool isRunAllEnabled;
    [ObservableProperty] private string sequenceStepText = "Aucune séquence en cours.";
    [ObservableProperty] private bool rebootRequired;

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
        SequenceStepText = "Annulation demandée. Arrêt entre les étapes ; une installation en cours peut devoir se terminer.";
    }

    [RelayCommand]
    private async Task RunAllAsync()
    {
        if (_sequenceService is null || !BeginOperation("Préparation de l’aperçu des mises à jour…")) return;
        SequenceProgress.Clear();
        RebootRequired = false;
        try
        {
            var scan = await _scanService.ScanAsync(UpdateScanRequest.DeepPreview,
                new Progress<string>(HandleProgress), _operationCancellation!.Token);
            _lastScanReport = scan;
            _lastItems.Clear();
            _lastItems.AddRange(scan.Items);
            RenderScanReport(scan);
            var steps = await _sequenceService.ScanAsync(_lastItems.ToArray(), _operationCancellation.Token);
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
            var result = await _sequenceService.ExecuteAsync(_confirmedSequenceSteps, plan,
                new Progress<UpdateSequenceProgress>(progress =>
                {
                    SequenceStepText = $"Étape {progress.Current}/{progress.Total} — {progress.Title} : {progress.Status}";
                    StatusText = SequenceStepText;
                    SequenceProgress.Add(SequenceStepText);
                }), _operationCancellation.Token);
            RebootRequired = result.RebootRequired;
            StatusText = result.Message;
            SequenceStepText = result.Message;
            _sequenceReport = result.Message + "\n" + string.Join("\n", result.Steps.Select(step => step.Step.Title + " : " + step.Message))
                + $"\nRedémarrage nécessaire : {(result.RebootRequired ? "oui" : "non")}.";
            await PersistReportAsync(new ReportEntry
            {
                Kind = ReportKind.Updates, Module = "Mises à jour", Title = "Tout mettre à jour",
                Summary = result.Message, SimpleView = _sequenceReport, RestartRequired = result.RebootRequired,
                Severity = result.WasRefused || result.Steps.Any(step => !step.Succeeded) ? ReportSeverity.Warning : ReportSeverity.Success,
                ExecutedActions = result.Steps.Select(step => new ReportAction
                {
                    Name = step.Step.Title, Status = step.Succeeded ? ReportActionStatus.Executed : ReportActionStatus.Failed,
                    Result = step.Message, RestartRequired = step.RebootRequired
                }).ToArray()
            });
            RequestVirgilState(result.WasRefused || result.Steps.Any(step => !step.Succeeded) ? VirgilCoreState.Warning : result.WasCancelled ? VirgilCoreState.Idle : VirgilCoreState.Success, "MISES À JOUR");
            VirgilMessageRequested?.Invoke(_sequenceReport);
        }
        catch (ConfirmationRefusedException)
        {
            StatusText = "Confirmation refusée. Aucune mise à jour lancée.";
        }
        catch (OperationCanceledException)
        {
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

    private void UpdateSequencePreview()
    {
        var selected = SequenceChoices.Where(choice => choice.IsSelected).Select(choice => choice.Step).ToArray();
        ValidationDetails = "Décochez les composants à exclure. Les pilotes sont facultatifs et décochés par défaut.";
        if (_sequenceService is null || selected.Length == 0)
        {
            ValidationCommand = "Aucun composant sélectionné.";
            ValidationSafety = "Aucune action sans confirmation.";
            return;
        }
        var preview = _sequenceService.CreatePreview(selected);
        ValidationCommand = string.Join("\n", preview.Effects);
        ValidationSafety = "Une seule confirmation pour les composants cochés. Annulation possible entre les étapes. Aucun redémarrage automatique.\n"
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
        isSelected = step.Component != UpdateComponent.Driver;
    }
    public UpdateSequenceStep Step { get; }
    public string Title => Step.Application is { } app
        ? $"{Step.Title} : {app.InstalledVersion} → {app.AvailableVersion} ({UpdatesViewModel.SourceLabel(app.Source)})"
        : Step.Title;
    [ObservableProperty] private bool isSelected;
    partial void OnIsSelectedChanged(bool value) => _changed();
}
