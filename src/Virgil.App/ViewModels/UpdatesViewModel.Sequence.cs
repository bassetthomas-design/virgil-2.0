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
    private bool _isWaitingForSteam;
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
                Operation.SetStep(SequenceStepKey(step), DisplaySequenceTitle(step), "Non lancée");
            var acceptingProgress = true;
            var sequenceProgressLock = new object();
            var sequenceCancellation = _operationCancellation;
            var result = await _sequenceService.ExecuteAsync(_confirmedSequenceSteps.Concat(Array.Empty<UpdateSequenceStep>()).ToArray(), plan,
                new Progress<UpdateSequenceProgress>(progress =>
                {
                    lock (sequenceProgressLock)
                    {
                        if (!acceptingProgress || !ReferenceEquals(sequenceCancellation, _operationCancellation) || !Operation.IsRunning || Operation.IsCancelling) return;
                        Operation.Report(new Virgil.Core.Operations.OperationProgress(progress.Title, progress.Current, progress.Total));
                        SequenceStepText = $"Étape {progress.Current}/{progress.Total} — {progress.Title} : {progress.Status}";
                        StatusText = SequenceStepText;
                        Operation.Commentary = progress.Status;
                        _isWaitingForSteam = progress.Component == UpdateComponent.Launcher && progress.Status == LauncherCardSummary.Waiting;
                        Operation.IsNonInterruptible = !_isWaitingForSteam;
                        // Une phase technique sans étape correspondante reste du texte courant.
                        var confirmed = progress.StepId is not null && progress.Component is not null
                            ? _confirmedSequenceSteps.FirstOrDefault(step => step.Id == progress.StepId && step.Component == progress.Component)
                            : _confirmedSequenceSteps.Where(step => !step.IsInformational).ElementAtOrDefault(progress.Current - 1);
                        if (confirmed is null || !string.Equals(confirmed.Title, progress.Title, StringComparison.Ordinal)) return;
                        var key = SequenceStepKey(confirmed);
                        if (_sequenceProgressIndices.TryGetValue(key, out var index)) SequenceProgress[index] = SequenceStepText;
                        else { _sequenceProgressIndices.Add(key, SequenceProgress.Count); SequenceProgress.Add(SequenceStepText); }
                        Operation.SetStep(key, confirmed.Title, "En cours", progress.Status);
                    }
                }), _operationCancellation.Token);
            lock (sequenceProgressLock) acceptingProgress = false;
            _sequenceWasCancelled = result.WasCancelled;
            RebootRequired = result.RebootRequired;
            Operation.Commentary = "Contrôle final des sources en lecture seule…";
            var verification = await ReadFinalVerificationsAsync(result);
            var checks = verification.Sources;
            RebootRequired = result.RebootRequired || checks.Any(check => check.Status == "redémarrage nécessaire");
            var allCurrent = !result.WasRefused && !result.WasCancelled && !RebootRequired
                && result.UnsupportedApplications.Count == 0
                && checks.Count > 0 && checks.All(check => check.IsCurrent)
                && _confirmedSequenceSteps.Where(step => !step.IsInformational).All(step =>
                    result.Steps.Any(outcome => outcome.Step.Id == step.Id && outcome.Step.Component == step.Component
                        && outcome.Succeeded && !outcome.WasSkipped && !outcome.WasUnavailable));
            var manualChecks = checks.Where(check => !check.IsCurrent).ToArray();
            var onlyManualCompletion = !RebootRequired && !result.WasRefused && manualChecks.Length > 0
                && manualChecks.All(check => check.Status is "à vérifier" or "non pris en charge automatiquement");
            var conclusion = allCurrent ? "Tout est à jour." : result.WasCancelled
                ? "Séquence annulée entre deux étapes — contrôle final effectué."
                : onlyManualCompletion ? "Contrôle final effectué : " + string.Join(" ; ", manualChecks.Select(check => check.Detail).Distinct()) + "."
                : "Séquence terminée. Contrôle final : des sources restent à vérifier ou à terminer.";
            StatusText = conclusion;
            SequenceStepText = conclusion;
            foreach (var step in result.Steps.Where(outcome => outcome.Step.IsInformational
                || _confirmedSequenceSteps.Any(selected => selected.Id == outcome.Step.Id && selected.Component == outcome.Step.Component)
                || (outcome.Step.Id.StartsWith("SteamGame:", StringComparison.Ordinal) && _confirmedSequenceSteps.Any(selected => selected.Component == UpdateComponent.Launcher && selected.Id == "Steam"))))
            {
                if (step.Step.Component == UpdateComponent.Launcher && step.VerifiedStatus is not null)
                {
                    Operation.SetStep(SequenceStepKey(step.Step), step.Step.Title, step.VerifiedStatus, step.Message);
                    continue;
                }
                if (step.Step.Component == UpdateComponent.Store && step.VerifiedStatus is not null)
                {
                    Operation.SetStep(SequenceStepKey(step.Step), DisplaySequenceTitle(step.Step), step.VerifiedStatus, DisplayApplicationText(step.Step, step.Message));
                    continue;
                }
                if (step.Step.Component == UpdateComponent.Store && step.Step.Application is not null
                    && step.Message.StartsWith("Non lancée", StringComparison.Ordinal))
                {
                    Operation.SetStep(SequenceStepKey(step.Step), DisplaySequenceTitle(step.Step), "reste à installer", "Non lancée : annulation demandée. Cette application reste à installer.");
                    continue;
                }
                if (step.Step.Component == UpdateComponent.Winget)
                {
                    var card = WingetCardResult(step.Step, step, verification.Applications);
                    Operation.SetStep(SequenceStepKey(step.Step), DisplaySequenceTitle(step.Step), card.Status, card.Detail);
                    continue;
                }
                var check = checks.FirstOrDefault(check => check.Component == SourceComponent(step.Step.Component));
                var status = check?.Status ?? SequenceResultStatus(step);
                if (step.Message.StartsWith("Non lancée", StringComparison.Ordinal)) status = "Non lancée";
                else if (UpdateSequenceService.IsSafetyBlocked(step)) status = "bloquée";
                else if (!step.Succeeded && !step.WasUnavailable && !step.WasSkipped) status = "en erreur";
                else if (step.WasUnavailable && result.Verifications.Count == 0) status = "Source indisponible";
                var detail = DisplaySequenceText(step.Message) + (check is null || check.Status == "bloquée" || !step.Succeeded || step.WasUnavailable || step.WasSkipped
                        || (result.Verifications.Count == 0 && check.Component != UpdateComponent.Winget)
                    ? string.Empty : "\n" + check.Detail);
                if (status == "à vérifier" && check is not null) detail = check.Detail;
                else if (status is not "à jour" and not "mis à jour" and not "Terminée"
                    && detail.Contains("Terminé", StringComparison.OrdinalIgnoreCase))
                    detail = "Étape exécutée ; contrôle final : " + status + "."
                        + (check is not null && !check.Detail.Contains("Terminé", StringComparison.OrdinalIgnoreCase) ? "\n" + check.Detail : string.Empty);
                Operation.SetStep(SequenceStepKey(step.Step), DisplaySequenceTitle(step.Step), status, detail);
            }
            foreach (var step in _confirmedSequenceSteps.Where(step => !step.IsInformational
                && !result.Steps.Any(outcome => outcome.Step.Id == step.Id && outcome.Step.Component == step.Component)))
            {
                var card = step.Component == UpdateComponent.Winget ? WingetCardResult(step, null, verification.Applications)
                    : (Status: "Non lancée", Detail: "Aucun résultat confirmé. Non lancée.");
                Operation.SetStep(SequenceStepKey(step), step.Title, card.Status, card.Detail);
            }
            _sequenceFinalSummary = conclusion + "\n" + string.Join("\n", checks.Select(check => SourceTitle(check.Component) + " : " + check.Status + " — " + DisplaySequenceText(check.Detail)))
                + "\n" + (RebootRequired == result.RebootRequired
                    ? result.Steps.LastOrDefault(step => step.Step.Component == UpdateComponent.PendingReboot)?.Message
                        ?? $"Redémarrage nécessaire : {(RebootRequired ? "oui" : "non")}."
                    : "Redémarrage nécessaire : oui, détecté au contrôle final.");
            if (result.UnsupportedApplications.Count > 0)
                _sequenceFinalSummary += "\nNon pris en charge automatiquement\n" + string.Join("\n", result.UnsupportedApplications.Select(app =>
                    app.Name + " — " + (string.IsNullOrWhiteSpace(app.InstalledVersion) ? "version non disponible" : app.InstalledVersion) + " : " + app.Message));
            _sequenceReport = _sequenceFinalSummary;
            Operation.Commentary = allCurrent ? "Chaque source cochée a été contrôlée après les installations."
                : "Le bilan détaille les restes détectés et les contrôles indisponibles. Aucun redémarrage automatique.";
            await PersistReportAsync(new ReportEntry
            {
                Kind = ReportKind.Updates, Module = "Mises à jour", Title = "Tout mettre à jour",
                Summary = conclusion, SimpleView = _sequenceReport, RestartRequired = RebootRequired,
                Severity = allCurrent ? ReportSeverity.Success : ReportSeverity.Warning,
                SkippedActions = result.Steps.Where(step => step.Step.IsInformational || step.WasSkipped || step.WasUnavailable).Select(step => new ReportAction
                {
                    Name = step.Step.Title, Status = ReportActionStatus.InformationOnly, Result = step.WasUnavailable ? "Non disponible sur ce PC (information)" : DisplaySequenceText(step.Message)
                }).ToArray(),
                ExecutedActions = result.Steps.Where(step => !step.Step.IsInformational && !step.WasSkipped && !step.WasUnavailable).Select(step => new ReportAction
                {
                    Name = step.Step.Title, Status = step.Succeeded ? ReportActionStatus.Executed : ReportActionStatus.Failed,
                    Result = DisplaySequenceText(step.Message), RestartRequired = step.RebootRequired
                }).ToArray()
            });
            RequestVirgilState(result.WasCancelled ? VirgilCoreState.Idle : allCurrent ? VirgilCoreState.Success : VirgilCoreState.Warning, "MISES À JOUR");
            VirgilMessageRequested?.Invoke(_sequenceReport);
            Operation.Commentary = allCurrent ? "Chaque source cochée a été contrôlée après les installations."
                : "Le bilan détaille les restes détectés et les contrôles indisponibles. Aucun redémarrage automatique.";
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
            _isWaitingForSteam = false;
            EndOperation();
        }
    }

    private static string SequenceStepKey(UpdateSequenceStep step) => step.Id.StartsWith("SteamGame:", StringComparison.Ordinal) ? step.Id : $"{step.Component}:{step.Id}";
    private static UpdateComponent SourceComponent(UpdateComponent component) => component == UpdateComponent.WingetFinal ? UpdateComponent.Winget : component;
    private static string SourceTitle(UpdateComponent component) => component switch
    {
        UpdateComponent.Winget => "Applications",
        UpdateComponent.Windows => "Windows Update : logiciels",
        UpdateComponent.Driver => "Pilotes", UpdateComponent.Defender => "Microsoft Defender",
        UpdateComponent.Store => "Microsoft Store", UpdateComponent.MicrosoftUpdate => "Microsoft Update",
        UpdateComponent.WindowsFeatureUpgrade => "Nouvelle version de Windows",
        UpdateComponent.PendingReboot => "Sonde de redémarrage", _ => "Lanceurs"
    };
    private static string DisplaySequenceTitle(UpdateSequenceStep step) =>
        step.Application is null && step.Component != UpdateComponent.Launcher ? DisplaySequenceText(step.Title) : step.Title;

    private static string DisplayApplicationText(UpdateSequenceStep step, string text)
    {
        var display = DisplaySequenceText(text);
        if (!string.IsNullOrWhiteSpace(step.Id)) display = display.Replace(step.Id, "cette application", StringComparison.OrdinalIgnoreCase);
        if (step.Application is { Scope: not "Inconnu" } app && !string.IsNullOrWhiteSpace(app.Scope))
            display = display.Replace(app.Scope, "cette application", StringComparison.OrdinalIgnoreCase);
        return display;
    }
    private static string DisplaySequenceText(string? text) => string.IsNullOrWhiteSpace(text) ? "non renseigné" : text
        .Replace("tout est à jour", "état complet non prouvé", StringComparison.OrdinalIgnoreCase)
        .Replace("tout à jour", "état complet non prouvé", StringComparison.OrdinalIgnoreCase)
        .Replace("plus rien à faire", "contrôle final requis", StringComparison.OrdinalIgnoreCase);

    private static (string Status, string Detail) WingetCardResult(UpdateSequenceStep step, UpdateSequenceStepResult? outcome, UpdateScanReport? scan)
    {
        const string identity = "";
        var complete = scan is not null && scan.Winget.IsAvailable && scan.Errors.Count == 0;
        var ownItems = scan?.Items.Where(item => (item.Source is UpdateSource.Winget or UpdateSource.MicrosoftStore)
            && string.Equals(item.Id, step.Id, StringComparison.OrdinalIgnoreCase)) ?? Enumerable.Empty<UpdateItem>();
        var remains = complete && ownItems.Any(item => item.Status == UpdateItemStatus.Available);
        var informationOnly = complete && ownItems.Any(item => item.Status == UpdateItemStatus.InformationOnly);
        var notStarted = outcome?.Message.StartsWith("Non lancée", StringComparison.Ordinal) == true;
        if (outcome is null || notStarted || (outcome.WasSkipped && !UpdateSequenceService.IsSafetyBlocked(outcome)))
        {
            var reason = outcome is null ? "Aucun résultat confirmé. Non lancée." : outcome.WasSkipped
                ? "Non lancée : étape sautée." : DisplayApplicationText(step, outcome.Message);
            return (remains ? "reste à installer" : informationOnly ? "reste à vérifier" : "Non lancée", identity + reason
                + (remains ? "\nContrôle final : cette application reste à installer."
                    : informationOnly ? "\nContrôle final : cette application reste à vérifier." : string.Empty));
        }
        var ownDetail = DisplayApplicationText(step, outcome.Message).Replace("Étape terminée après annulation.",
            "Annulation demandée après cette étape.", StringComparison.Ordinal);
        if (UpdateSequenceService.IsSafetyBlocked(outcome))
            return ("bloquée", identity + (ownDetail.Contains("Terminé", StringComparison.OrdinalIgnoreCase)
                ? "Installation bloquée par une garde de sécurité." : ownDetail));
        if (outcome.WasUnavailable) return ("indisponible", identity + "Installation indisponible pour cette application.");
        if (!outcome.Succeeded)
            return ("en erreur", identity + (ownDetail.Contains("Terminé", StringComparison.OrdinalIgnoreCase) ? "Installation en échec." : ownDetail));
        if (!complete)
            return (scan?.Winget.IsAvailable == true ? "en erreur" : "indisponible", identity
                + "Étape exécutée ; contrôle final de cette application non prouvé.");
        if (remains) return ("reste à installer", identity + "Étape exécutée ; contrôle final : cette application reste à installer.");
        if (informationOnly) return ("reste à vérifier", identity + "Étape exécutée ; contrôle final : cette application reste à vérifier, mise à jour non prouvée.");
        if (outcome.RebootRequired) return ("redémarrage nécessaire", identity + "Étape exécutée ; redémarrage nécessaire pour cette application.");
        return ("mis à jour", identity + ownDetail + "\nContrôle final : cette application n'est plus proposée.");
    }

    private async Task<(IReadOnlyList<UpdateSourceVerification> Sources, UpdateScanReport? Applications)> ReadFinalVerificationsAsync(UpdateSequenceResult result)
    {
        var checks = result.Verifications.ToList();
        UpdateScanReport? finalApplications = null;
        var components = _confirmedSequenceSteps.Where(step => !step.IsInformational).Select(step => SourceComponent(step.Component)).Distinct().ToArray();
        if (components.Contains(UpdateComponent.Winget))
        {
            UpdateSourceVerification check;
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                var scan = await _scanService.ScanAsync(UpdateScanRequest.DeepPreview with { IncludeDriverInventory = false }, null, timeout.Token).WaitAsync(timeout.Token);
                finalApplications = scan with { Items = scan.Items.ToArray(), Errors = scan.Errors.ToArray() };
                if (scan.WindowsUpdate.PendingRebootDetected)
                    checks.Add(new(UpdateComponent.PendingReboot, "redémarrage nécessaire", "Détecté lors de la relecture applicative."));
                var available = scan.Items.Any(item => item.Status == UpdateItemStatus.Available);
                var informationOnly = scan.Items.Any(item => item.Status == UpdateItemStatus.InformationOnly);
                var remaining = available || informationOnly;
                check = !scan.Winget.IsAvailable ? new(UpdateComponent.Winget, "indisponible", "Source des applications indisponible au contrôle final.")
                    : scan.Errors.Count > 0 ? new(UpdateComponent.Winget, "en erreur", "Recherche applicative incomplète au contrôle final.")
                    : available
                        ? new(UpdateComponent.Winget, "reste à installer", "Contrôle final : reste applicatif détecté.")
                    : informationOnly ? new(UpdateComponent.Winget, "reste à vérifier", "Contrôle final : mises à jour détectées à vérifier ; leur installation n'est pas prouvée.")
                        : new(UpdateComponent.Winget, "à jour", "Contrôle final : aucun reste applicatif détecté.");
                if (remaining || (scan.Winget.IsAvailable && scan.Errors.Count == 0))
                    check = check with { HasRemaining = remaining };
            }
            catch { check = new(UpdateComponent.Winget, "indisponible", "Contrôle final des applications indisponible ou délai dépassé."); }
            checks.RemoveAll(value => value.Component == UpdateComponent.Winget);
            checks.Add(check);
        }
        foreach (var component in components)
        {
            var outcomes = result.Steps.Where(step => SourceComponent(step.Step.Component) == component).ToArray();
            var check = checks.FirstOrDefault(value => value.Component == component)
                ?? new(component, "indisponible", "Contrôle final non disponible pour cette source.");
            var started = outcomes.Where(step => !step.WasSkipped && !step.Message.StartsWith("Non lancée", StringComparison.Ordinal)).ToArray();
            var missing = _confirmedSequenceSteps.Where(step => SourceComponent(step.Component) == component).Any(step =>
                !outcomes.Any(outcome => outcome.Step.Id == step.Id && outcome.Step.Component == step.Component)
                || outcomes.Any(outcome => outcome.Step.Id == step.Id && outcome.Message.StartsWith("Non lancée", StringComparison.Ordinal)));
            var storeProof = component == UpdateComponent.Store && outcomes.Any(step => step.VerifiedStatus is not null);
            if (storeProof)
                check = UpdateSequenceService.SummarizeOutcomes(check, outcomes);
            else if (started.Length == 0 && missing)
                check = check with { Status = "non lancée", Detail = "Aucune étape de cette source n'a été lancée." };
            else if (component == UpdateComponent.Winget && finalApplications?.Items.Any(item => item.Status == UpdateItemStatus.Available) == true)
                check = UpdateSequenceService.SummarizeOutcomes(check with { Status = "reste à installer", HasRemaining = true, Detail = "Contrôle final : reste applicatif détecté." }, outcomes);
            else if (outcomes.Any(UpdateSequenceService.IsSafetyBlocked))
                check = check with { Status = "bloquée", Detail = "Installation bloquée par une garde de sécurité." };
            else if (outcomes.Any(step => step.WasUnavailable)) check = check with { Status = "indisponible", Detail = "Source indisponible sur ce PC." };
            else if (started.Any(step => !step.Succeeded))
                check = check with { Status = "en erreur", Detail = "Échec pendant l'exécution de cette source ; consultez les cartes." };
            if (component == UpdateComponent.Store && !storeProof && outcomes.Length > 0 && outcomes.All(step => step.Succeeded && !step.WasUnavailable))
                check = new(component, "à vérifier", "à terminer dans le Microsoft Store");
            if (component == UpdateComponent.Launcher && outcomes.Any(step => step.VerifiedStatus is not null))
                check = LauncherCardSummary.Summarize(outcomes);
            else if (component == UpdateComponent.Launcher && check.Status != "non lancée" && outcomes.Length > 0
                && outcomes.All(step => (step.Succeeded || step.WasSkipped) && !step.WasUnavailable)
                && !outcomes.Any(UpdateSequenceService.IsSafetyBlocked))
                check = new(component, "à vérifier", "à terminer dans le lanceur");
            if (check.IsCurrent && outcomes.Any(step => step.RebootRequired))
                check = new(component, "redémarrage nécessaire", check.Detail);
            checks.RemoveAll(value => value.Component == component);
            checks.Add(check);
        }
        var rebootStatus = _windowsUpdateStatusService.ReadStatus();
        if (rebootStatus.PendingRebootDetected || rebootStatus.Errors.Count > 0)
        {
            checks.RemoveAll(check => check.Component == UpdateComponent.PendingReboot);
            checks.Add(new(UpdateComponent.PendingReboot, rebootStatus.PendingRebootDetected ? "redémarrage nécessaire" : "indisponible",
                rebootStatus.PendingRebootDetected ? "Détecté par la sonde après les recherches finales." : "Lecture finale du redémarrage indisponible."));
        }
        return (checks, finalApplications);
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
        ValidationSafety = "Un point de restauration récent (moins de 24 h) pourra être réutilisé si Windows refuse d'en créer un.\nOffice Click-to-Run se met à jour lui-même.\nLes sources sont actualisées avant les applications et la passe finale. La passe finale installe les mises à jour trouvées, y compris hors de la liste affichée.\nCertaines étapes ne peuvent pas être interrompues une fois lancées.\nUne seule confirmation pour les composants cochés. Annulation possible entre les étapes. Aucun redémarrage automatique.\n"
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
        isSelected = !step.IsInformational;
    }
    public UpdateSequenceStep Step { get; }
    public bool CanSelect => !Step.IsInformational;
    public string AutomationId => "UpdatePlanStep." + (Step.Component switch
    {
        UpdateComponent.Winget => "Winget." + Step.Id,
        UpdateComponent.Windows => "WindowsSoftware",
        UpdateComponent.Driver => "Drivers",
        _ => Step.Component.ToString()
    });
    public bool HasHelp => Step.Component == UpdateComponent.WindowsFeatureUpgrade;
    public string HelpText => HasHelp ? "Opération longue, redémarrage obligatoire ; dernière étape de l'élévation Windows Update." : string.Empty;
    public string Title
    {
        get
        {
            if (Step.Component == UpdateComponent.Launcher && Step.Id != "launchers") return LauncherCardSummary.Confirmation(Step);
            if (Step.Application is not { } app) return Step.Title;
            var installed = string.IsNullOrWhiteSpace(app.InstalledVersion) ? "version non disponible" : app.InstalledVersion;
            if (Step.Component == UpdateComponent.OutsideWinget)
                return $"{Step.Title} : {installed} — non pris en charge automatiquement";
            var versions = string.IsNullOrWhiteSpace(app.AvailableVersion) ? installed : $"{installed} → {app.AvailableVersion}";
            return $"{Step.Title} : {versions} ({(app.Source == UpdateSource.MicrosoftStore ? "Microsoft Store" : "Applications")})";
        }
    }
    [ObservableProperty] private bool isSelected;
    partial void OnIsSelectedChanged(bool value)
    {

        _changed();
    }
}
