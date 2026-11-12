using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Virgil.App.Animation;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Updates;
using Virgil.Core.Reports;

namespace Virgil.App.ViewModels;

public partial class UpdatesViewModel
{
    public ObservableCollection<UpdateTextCardViewModel> SingleCards { get; } = new();
    public IEnumerable<UpdateTextCardViewModel> SourceCards => SingleCards.Where(c => c.Step?.Component != UpdateComponent.Winget);
    public string SingleSourceSummary { get; private set; } = string.Empty;

    private void MergeSingleApplications()
    {
        // The current screen scan is enough: no sequence scan or source refresh here.
        SingleCards.Clear();
        foreach (var item in _lastItems)
        {
            var step = new UpdateSequenceStep(item.Id, item.Name, UpdateComponent.Winget, item);
            AddSingleCard(new(step, false, false, item.Message ?? "Cette mise à jour reste à installer.")
                { VerifiedStatus = UpdateSequenceService.IsSingleTarget(step) ? "reste à installer" : DriverCardSummary.Unsupported });
        }
        RefreshSingleState();
    }
    private UpdateTextCardViewModel AddSingleCard(UpdateSequenceStepResult outcome)
    {
        var key = SequenceStepKey(outcome.Step);
        var card = SingleCards.FirstOrDefault(c => c.Key == key);
        if (card is not null) return card;
        if (outcome.Step.Component == UpdateComponent.Store && string.IsNullOrWhiteSpace(outcome.Step.Application?.AvailableVersion))
            outcome = outcome with { VerifiedStatus = DriverCardSummary.Unsupported, Message = "La version proposée ne peut pas être confirmée." };
        card = new(string.Empty) { Step = outcome.Step, Status = outcome.VerifiedStatus ?? "reste à installer", Detail = outcome.Message, Busy = _operationInProgress };
        SingleCards.Add(card);
        return card;
    }
    private async Task LoadSingleSourceCardsAsync(CancellationToken token)
    {
        if (_sequenceService is null) return;
        foreach (var outcome in await _sequenceService.ReadSingleItemsAsync(token))
        {
            // The software route already owns duplicate Microsoft Store applications.
            if (outcome.Step.Component == UpdateComponent.Store && SingleCards.Any(c => c.Step?.Component == UpdateComponent.Winget
                && string.Equals(c.Step.Application?.Id, outcome.Step.Id, StringComparison.OrdinalIgnoreCase))) continue;
            AddSingleCard(outcome);
        }
        RefreshSingleState();
    }
    private void ApplySingleSequenceResults(UpdateSequenceResult result)
    {
        foreach (var outcome in result.Steps.Where(r => r.Step.Component is UpdateComponent.Winget or UpdateComponent.Driver or UpdateComponent.Store or UpdateComponent.Launcher
            && r.Step.Id is not ("drivers" or "store" or "launchers")))
        {
            var shown = Operation.Steps.FirstOrDefault(c => c.Key == SequenceStepKey(outcome.Step));
            if (shown is null) continue;
            var card = AddSingleCard(outcome.Step.Id.StartsWith("SteamGame:", StringComparison.Ordinal)
                ? outcome with { Step = outcome.Step with { InformationOnly = true } } : outcome);
            card.Status = shown.Status == "Non lancée" ? "non lancée" : shown.Status;
            card.Detail = shown.Detail;
        }
        RefreshSingleState();
    }
    private void RefreshSingleState()
    {
        foreach (var card in SingleCards) card.Busy = _operationInProgress;
        var lines = new List<string>();
        foreach (var component in new[] { UpdateComponent.Winget, UpdateComponent.Store, UpdateComponent.Driver, UpdateComponent.Launcher })
        {
            var cards = SingleCards.Where(c => c.Step?.Component == component).ToArray();
            if (cards.Length == 0) continue;
            var active = cards.Where(c => !c.Step!.IsInformational && c.Status != DriverCardSummary.Unsupported).ToArray();
            var priority = new[] { "bloquée", "en erreur", "reste à installer", "non lancée", "indisponible", "à vérifier", "redémarrage nécessaire", "mis à jour", "à jour", "à jour selon Steam" };
            var status = priority.FirstOrDefault(s => active.Any(c => c.Status == s)) ?? "à jour";
            var counts = new List<string>();
            foreach (var value in new[] { "mis à jour", "à jour", "en erreur", "bloquée", "reste à installer", "non lancée", "indisponible", "à vérifier", "redémarrage nécessaire", DriverCardSummary.Unsupported })
            {
                var count = cards.Count(c => c.Status == value);
                if (count == 0) continue;
                var label = count > 1 ? value switch { "reste à installer" => "restent à installer", "non lancée" => "non lancées", "bloquée" => "bloquées", _ => value } : value;
                counts.Add(count + " " + label);
            }
            lines.Add(SourceTitle(component) + " : " + status + (status == "à jour" ? string.Empty : " — " + string.Join(", ", counts)));
        }
        SingleSourceSummary = string.Join("\n", lines);
        OnPropertyChanged(nameof(SingleSourceSummary));
        OnPropertyChanged(nameof(SourceCards));
        RunSingleCommand.NotifyCanExecuteChanged();
    }
    private bool CanRunSingle(UpdateSequenceStep? step)
    {
        if (_operationInProgress || _sequenceService is null || step is null) return false;
        var card = SingleCards.FirstOrDefault(c => c.Key == SequenceStepKey(step));
        return card?.CanUpdate == true && card.Step == step;
    }
    [RelayCommand(CanExecute = nameof(CanRunSingle))]
    private async Task RunSingleAsync(UpdateSequenceStep? step)
    {
        if (!CanRunSingle(step)) return;
        var card = SingleCards.First(c => c.Key == SequenceStepKey(step!));
        var target = card.Step! with { IsSingleItem = true };
        if (!BeginOperation("Préparation de la mise à jour de " + target.Title + "…")) return;
        Operation.Start(StatusText, immediately: true, nonInterruptible: true);
        _confirmedSequenceSteps = [target];
        _sequenceWasCancelled = false;
        _sequenceFinalSummary = null;
        IsSequencePreview = false;
        try
        {
            var preview = _sequenceService!.CreatePreview([target]);
            ValidationTitle = preview.Title;
            var item = target.Application;
            var versions = item is not null && !string.IsNullOrWhiteSpace(item.AvailableVersion)
                ? (string.IsNullOrWhiteSpace(item.InstalledVersion) ? "version non disponible" : item.InstalledVersion) + " → " + item.AvailableVersion
                : "version non disponible";
            ValidationDetails = target.Title + "\n" + versions + "\n" + card.MethodText;
            ValidationCommand = string.Empty;
            ValidationSafety = (UpdateSequenceService.IsElevated(target.Component)
                ? "Un point de restauration de la session doit être vérifié. Un point de restauration récent (moins de 24 h) pourra être réutilisé si Windows refuse d'en créer un. " : string.Empty)
                + "Aucun redémarrage automatique.";
            ExecuteLabel = "Mettre à jour";
            SkipLabel = "ANNULER";
            IsCancelSequenceVisible = false;
            ValidationCoreState = VirgilCoreState.SensitiveAction;
            var decision = new TaskCompletionSource<UpdateDecision>();
            _activeDecision = decision;
            IsValidationOpen = true;
            if (await decision.Task != UpdateDecision.Execute || _operationCancellation!.IsCancellationRequested)
            {
                card.Status = "non lancée"; card.Detail = "Non lancée : annulation avant le début."; return;
            }
            var level = preview.RiskLevel == RiskLevel.Sensitive ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple;
            var plan = _confirmationService.Issue(preview, new(level, null));
            var session = _operationCancellation;
            var accepting = true;
            Operation.SetStep(card.Key, card.Title, "En cours", card.MethodText);
            var result = await _sequenceService.ExecuteAsync([target], plan, new Progress<UpdateSequenceProgress>(progress =>
            {
                if (!accepting || !ReferenceEquals(session, _operationCancellation) || Operation.IsCancelling) return;
                _isWaitingForSteam = progress.Component == UpdateComponent.Launcher && progress.Status == LauncherCardSummary.Waiting;
                Operation.IsNonInterruptible = !_isWaitingForSteam;
                Operation.Commentary = progress.Status;
                SequenceStepText = progress.Status;
            }), session!.Token);
            accepting = false;
            _sequenceWasCancelled = result.WasCancelled;
            RebootRequired |= result.RebootRequired;
            var own = result.Steps.FirstOrDefault(r => r.Step.Component == target.Component && r.Step.Id == target.Id);
            if (target.Component == UpdateComponent.Winget)
            {
                var verification = await ReadFinalVerificationsAsync(result);
                var state = WingetCardResult(target, own, verification.Applications);
                card.Status = state.Status == "Non lancée" ? "non lancée" : state.Status;
                card.Detail = state.Detail;
                foreach (var itemAfter in verification.Applications?.Items ?? [])
                    AddSingleCard(new(new(itemAfter.Id, itemAfter.Name, UpdateComponent.Winget, itemAfter), false, false,
                        "Cette application reste proposée.") { VerifiedStatus = "reste à installer" });
            }
            else
            {
                card.Status = result.WasRefused ? "bloquée" : own is null ? "non lancée"
                    : UpdateSequenceService.IsSafetyBlocked(own) ? "bloquée" : own.VerifiedStatus
                        ?? (own.WasUnavailable ? "indisponible" : own.Succeeded ? "à vérifier" : "en erreur");
                card.Detail = own?.Message ?? (result.WasRefused ? "Confirmation refusée. Aucune action lancée." : "Aucun résultat confirmé. Non lancée.");
                if (target.Component == UpdateComponent.Driver && target.Application is not null && own?.Succeeded == true)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    try
                    {
                        var check = await _scanService.ScanAsync(UpdateScanRequest.DeepPreview with { IncludeDriverInventory = false }, null, timeout.Token).WaitAsync(timeout.Token);
                        if (!check.Winget.IsAvailable || check.Errors.Count > 0) { card.Status = "indisponible"; card.Detail = "Installation exécutée ; contrôle final de cet élément indisponible."; }
                        else if (check.Items.Any(itemAfter => itemAfter.Id == target.Application.Id && itemAfter.Status == UpdateItemStatus.Available))
                        { card.Status = "reste à installer"; card.Detail = "Cet élément reste proposé au contrôle final."; }
                    }
                    catch { card.Status = "indisponible"; card.Detail = "Installation exécutée ; contrôle final de cet élément indisponible."; }
                }
                foreach (var other in result.Steps.Where(r => r.Step.Component is UpdateComponent.Driver or UpdateComponent.Launcher && r.Step.Id != target.Id))
                    AddSingleCard(other);
            }
            Operation.SetStep(card.Key, card.Title, card.Status, card.Detail);
            RefreshSingleState();
            _sequenceReport = card.Title + " : " + card.Status + "\n" + SingleSourceSummary;
            await PersistReportAsync(new ReportEntry
            {
                Kind = ReportKind.Updates, Module = "Mises à jour", Title = "Mettre à jour " + card.Title,
                Summary = card.Title + " : " + card.Status, SimpleView = _sequenceReport, RestartRequired = RebootRequired,
                Severity = card.Status is "mis à jour" or "à jour" ? ReportSeverity.Success : ReportSeverity.Warning,
                ExecutedActions = own is null || own.WasSkipped || result.WasRefused ? [] : [new ReportAction
                { Name = card.Title, Result = card.Detail, Status = own.Succeeded ? ReportActionStatus.Executed : ReportActionStatus.Failed }]
            });
        }
        catch (OperationCanceledException) { card.Status = "non lancée"; card.Detail = "Non lancée : annulation avant le début."; }
        catch (ConfirmationRefusedException) { card.Status = "bloquée"; card.Detail = "Confirmation refusée. Aucune action lancée."; }
        catch { card.Status = "indisponible"; card.Detail = "Le résultat de cette mise à jour n'a pas pu être vérifié."; }
        finally
        {
            _isWaitingForSteam = false;
            RefreshSingleState();
            StatusText = card.Title + " : " + card.Status;
            _sequenceFinalSummary = StatusText + "\n" + SingleSourceSummary;
            EndOperation();
        }
    }
}
