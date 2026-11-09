using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Actions;
using Virgil.Core.Processes;
using Virgil.Core.Reports;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class UpdatesSequenceViewModelTests
{
    [Theory]
    [InlineData("cards")]
    [InlineData("unfinished")]
    [InlineData("summary")]
    public async Task Blocked_elevated_preparation_has_no_orphan_or_unproven_success(string check)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineProgressContext());
        try
        {
            var f = new Fixture(wrap: service => new BlockedPreparationSequence(service));
            var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
            f.Vm.ExecuteDecisionCommand.Execute(null);
            await pending;
            if (check == "cards")
            {
                Assert.Equal(new[] { "Signatures Microsoft Defender", "Microsoft Update", "Windows Update" },
                    f.Vm.Operation.Steps.Select(step => step.Title));
                Assert.DoesNotContain(f.Vm.Operation.Steps, step => step.Title == BlockedPreparationSequence.PreparationTitle);
            }
            else if (check == "unfinished")
            {
                var defender = Assert.Single(f.Vm.Operation.Steps, step => step.Title == "Signatures Microsoft Defender");
                Assert.NotEqual("Terminée", defender.Status);
                Assert.All(f.Vm.Operation.Steps, step => Assert.NotEqual("Terminée", step.Status));
            }
            else
            {
                Assert.True(f.Vm.Operation.IsVisible);
                Assert.Contains("bloquée", f.Vm.Operation.FinalSummary);
                Assert.DoesNotContain("tout est à jour", f.Vm.Operation.FinalSummary, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Empty(f.Backend.Executed);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    internal sealed class BlockedPreparationSequence(IUpdateSequenceService previews) : IUpdateSequenceService
    {
        public const string PreparationTitle = "Point de restauration et préparation Microsoft Update";
        public Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> apps, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<UpdateSequenceStep>>([
                new("defender", "Signatures Microsoft Defender", UpdateComponent.Defender),
                new("microsoft-update", "Microsoft Update", UpdateComponent.MicrosoftUpdate),
                new("software", "Windows Update", UpdateComponent.Windows)]);
        public Virgil.Contracts.Actions.ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> selected) => previews.CreatePreview(selected);
        public Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> selected, ConfirmedPlan? plan,
            IProgress<UpdateSequenceProgress>? progress, CancellationToken token)
        {
            Assert.NotNull(plan);
            progress?.Report(new(1, 3, "Signatures Microsoft Defender", "En cours"));
            progress?.Report(new(1, 3, PreparationTitle, "En cours"));
            var preparation = selected.Single(step => step.Component == UpdateComponent.MicrosoftUpdate);
            var windows = selected.Single(step => step.Component == UpdateComponent.Windows);
            return Task.FromResult(new UpdateSequenceResult(false, false, false,
                [new(preparation, false, false, "Préparation Microsoft Update bloquée : point de restauration non vérifié.") { RestorePointVerified = false },
                 new(windows, false, false, "Installation Microsoft bloquée : point de restauration non vérifié.")],
                "Séquence terminée avec erreurs ; consultez les étapes."));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_steps_keep_order_latest_tick_and_honest_final_summary(bool cancel)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineProgressContext());
        try
        {
            var f = new Fixture(wrap: service => new RepeatingSequence(service, cancel));
            var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
            f.Vm.ExecuteDecisionCommand.Execute(null);
            await pending;
            Assert.Equal(new[] { "Source A", "Source B" }, f.Vm.Operation.Steps.Select(step => step.Title));
            Assert.Equal(cancel ? "Non lancée" : "Source indisponible", f.Vm.Operation.Steps[1].Status);
            Assert.Contains("fichiers 12/40", f.Vm.SequenceProgress[0]);
            Assert.True(f.Vm.Operation.IsVisible);
            if (cancel) Assert.NotEqual(100, f.Vm.Operation.Percent);
            else { Assert.Equal(100, f.Vm.Operation.Percent); Assert.False(f.Vm.Operation.IsIndeterminate); }
            Assert.Contains("Redémarrage nécessaire : oui", f.Vm.Operation.FinalSummary);
            Assert.DoesNotContain("tout est à jour", f.Vm.Operation.FinalSummary, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(f.Vm.Operation.Commentary);
            Assert.Empty(f.Backend.Executed);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    internal sealed class RepeatingSequence(IUpdateSequenceService previews, bool cancel = false) : IUpdateSequenceService
    {
        public Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> apps, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<UpdateSequenceStep>>([new("a", "Source A", UpdateComponent.WingetFinal), new("b", "Source B", UpdateComponent.Store)]);
        public Virgil.Contracts.Actions.ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> selected) => previews.CreatePreview(selected);
        public Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> selected, ConfirmedPlan? plan, IProgress<UpdateSequenceProgress>? progress, CancellationToken token)
        {
            Assert.NotNull(plan);
            var steps = selected.Where(step => !step.IsInformational).ToArray();
            progress?.Report(new(1, 2, steps[0].Title, "fichiers 1/40"));
            progress?.Report(new(1, 2, steps[0].Title, "fichiers 12/40"));
            if (!cancel)
            {
                progress?.Report(new(2, 2, steps[1].Title, "Lecture"));
                progress?.Report(new(2, 2, steps[1].Title, "Source bloquée"));
            }
            return Task.FromResult(new UpdateSequenceResult(false, cancel, true,
                [new(steps[0], true, true, "Terminée"), new(steps[1], false, false, cancel ? "Non lancée : annulation demandée." : "Source bloquée") { WasUnavailable = !cancel }],
                cancel ? "Séquence annulée entre deux étapes." : "Séquence terminée."));
        }
    }

    [Fact]
    public async Task Repeated_reports_for_one_sequence_step_update_a_single_visible_entry()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineProgressContext());
        try
        {
            var f = new Fixture();
            var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
            foreach (var choice in f.Vm.SequenceChoices)
                choice.IsSelected = choice.Step.Component == UpdateComponent.WingetFinal;
            f.Vm.ExecuteDecisionCommand.Execute(null);
            await pending;
            Assert.Single(f.Backend.Executed);
            var entry = Assert.Single(f.Vm.SequenceProgress);
            Assert.Contains("Passe finale", entry);
            Assert.Contains("Terminé", entry);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class InlineProgressContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Store_unavailable_is_persisted_as_information_with_success_state()
    {
        var f = new Fixture();
        var states = new List<Virgil.App.Animation.VirgilCoreState>();
        f.Vm.VirgilStateRequested += (state, _) => states.Add(state);
        f.Backend.Handler = (step, _) =>
        {
            var unavailable = step.Component == UpdateComponent.Store;
            var result = new UpdateSequenceStepResult(step, !unavailable, false,
                unavailable ? "Scan Store non disponible sur ce PC (information, best effort)." : "Terminé") { WasUnavailable = unavailable };
            return Task.FromResult(result);
        };
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Store;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        var report = Assert.IsType<ReportEntry>(f.History.Report);
        Assert.Equal(ReportSeverity.Success, report.Severity);
        Assert.Empty(report.ExecutedActions);
        var store = Assert.Single(report.SkippedActions, action => action.Name == "Rechercher les mises à jour du Microsoft Store");
        Assert.Equal(ReportActionStatus.InformationOnly, store.Status);
        Assert.Equal("Non disponible sur ce PC (information)", store.Result);
        Assert.Contains(Virgil.App.Animation.VirgilCoreState.Success, states);
        Assert.DoesNotContain(Virgil.App.Animation.VirgilCoreState.Warning, states);
    }

    [Theory]
    [Trait("Category", "Safety")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelSequenceOrModuleAcknowledgesImmediatelyAndBlocksNext(bool module)
    {
        var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishStep = new TaskCompletionSource<UpdateSequenceStepResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishModule = new TaskCompletionSource<ProcessRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Backend.Handler = (step, token) => { entered.SetResult(); return finishStep.Task; };
        f.Runner.Handler = async token => { entered.SetResult(); return await finishModule.Task.WaitAsync(token); };
        var pending = module ? f.Vm.InstallWingetModuleCommand.ExecuteAsync(null) : f.Vm.RunAllCommand.ExecuteAsync(null);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await entered.Task;
        f.Vm.Operation.CancelCommand.Execute(null);
        Assert.True(!f.Vm.IsBusy || f.Vm.IsCancelling);
        Assert.False(f.Vm.Operation.CanCancel);
        if (!module)
        {
            Assert.Single(f.Backend.Executed);
            Assert.False(pending.IsCompleted);
            finishStep.SetResult(new(new("windows", "Windows", UpdateComponent.Windows), true, false, "Terminée"));
        }
        await pending.WaitAsync(TimeSpan.FromSeconds(1));
        if (!module) Assert.Single(f.Backend.Executed);
        else Assert.Equal(1, f.Runner.Calls);
        Assert.True(f.Vm.IsIdle);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public async Task Update_plan_lists_script_steps_in_order_with_drivers_checked()
    {
        var f = new Fixture();
        Assert.True(f.Vm.IsRunAllEnabled);
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        Assert.True(f.Vm.IsValidationOpen);
        Assert.True(f.Vm.IsSequencePreview);
        Assert.Contains("Un point de restauration récent (moins de 24 h) pourra être réutilisé si Windows refuse d'en créer un.", f.Vm.ValidationSafety);
        var feature = Assert.Single(f.Vm.SequenceChoices, choice => choice.Step.Component == UpdateComponent.WindowsFeatureUpgrade);
        Assert.True(feature.HasHelp);
        Assert.Contains("redémarrage obligatoire", feature.HelpText);
        Assert.Contains("dernière étape", feature.HelpText);
        Assert.Empty(f.Backend.Executed);
        Assert.Equal(0, f.Confirmation.Calls);
        var driver = Assert.Single(f.Vm.SequenceChoices, choice => choice.Step.Component == UpdateComponent.Driver);
        Assert.True(driver.IsSelected);
        Assert.True(Assert.Single(f.Vm.SequenceChoices, choice => choice.Step.Id == "windows-feature-upgrade").IsSelected);
        Assert.Equal(new[] { "restore", "defender", "microsoft-update", "launchers", "software", "drivers", "store", "windows-feature-upgrade", "winget-final", "pending-reboot" }, f.Vm.SequenceChoices.Select(choice => choice.Step.Id));
        Assert.Equal(new[] { UpdateComponent.Restore, UpdateComponent.Defender, UpdateComponent.MicrosoftUpdate, UpdateComponent.Launcher, UpdateComponent.Windows, UpdateComponent.Driver, UpdateComponent.Store, UpdateComponent.WindowsFeatureUpgrade, UpdateComponent.WingetFinal, UpdateComponent.PendingReboot }, f.Vm.SequenceChoices.Select(choice => choice.Step.Component));
        var information = f.Vm.SequenceChoices.Where(choice => choice.Step.Component == UpdateComponent.Launcher).ToArray();
        Assert.Single(information);
        foreach (var choice in information)
        {
            Assert.True(choice.CanSelect);
            Assert.True(choice.IsSelected);
        }
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Defender;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(1, f.Confirmation.Calls);
        Assert.Equal(new[] { UpdateComponent.Defender }, f.Backend.Executed);
        Assert.True(f.Vm.RebootRequired);
        Assert.True(f.Vm.IsIdle);
        Assert.False(f.Vm.IsSequencePreview);
        Assert.NotNull(f.History.Report);
        Assert.True(f.History.Report.RestartRequired);
        Assert.Single(f.History.Report.ExecutedActions);
        Assert.Single(f.History.Report.SkippedActions);
        Assert.All(f.History.Report.SkippedActions, line =>
        {
            Assert.Equal(ReportActionStatus.InformationOnly, line.Status);
            Assert.StartsWith("Redémarrage nécessaire : oui", line.Result);
        });
        Assert.Equal(ReportSeverity.Success, f.History.Report.Severity);
        f.Vm.ViewReportCommand.Execute(null);
        Assert.Contains("Redémarrage nécessaire : oui", f.Vm.ReportText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrRejectedConfirmationHasNoEffects(bool reject)
    {
        var f = new Fixture();
        f.Confirmation.Refuse = reject;
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        if (reject) f.Vm.ExecuteDecisionCommand.Execute(null);
        else f.Vm.CancelUpdateOperationCommand.Execute(null);
        await pending;
        Assert.Empty(f.Backend.Executed);
        Assert.Equal(reject ? 1 : 0, f.Confirmation.Calls);
        Assert.True(f.Vm.IsIdle);
    }

    [Fact]
    public async Task EmptySelectionCannotConfirmAndCancelStopsBeforeNextStep()
    {
        var f = new Fixture();
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = false;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        Assert.True(f.Vm.IsValidationOpen);
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, f.Confirmation.Calls);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = true;
        f.Backend.OnExecute = f.Vm.CancelActiveOperation;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Single(f.Backend.Executed);
        Assert.Equal(1, f.Confirmation.Calls);
        Assert.Contains("annulée", f.Vm.StatusText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalModuleIsInstalledOnlyAfterItsOwnPreviewConfirmation(bool confirm)
    {
        var f = new Fixture();
        await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
        Assert.True(f.Vm.IsModuleInstallVisible);
        var pending = f.Vm.InstallWingetModuleCommand.ExecuteAsync(null);
        Assert.True(f.Vm.IsValidationOpen);
        Assert.Contains("module WinGet", f.Vm.ValidationTitle);
        Assert.Equal(0, f.Runner.Calls);
        Assert.Equal(0, f.Confirmation.Calls);
        if (confirm) f.Vm.ExecuteDecisionCommand.Execute(null);
        else f.Vm.SkipDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(confirm ? 1 : 0, f.Confirmation.Calls);
        Assert.Equal(confirm ? 1 : 0, f.Runner.Calls);
        Assert.Equal(!confirm, f.Vm.IsModuleInstallVisible);
        Assert.True(f.Vm.IsIdle);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Pending_reboot_probe_drives_banner_and_report()
    {
        var f = new Fixture(new CbsProbe());
        f.Backend.Reboot = false;
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.WingetFinal;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Contains("REDÉMARRAGE WINDOWS REQUIS", f.Vm.RebootStatusText);
        Assert.Contains("oui (Maintenance des composants)", f.History.Report!.SimpleView);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Cancel_during_Windows_Update_acknowledges_non_interruptible_step_immediately()
    {
        var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<UpdateSequenceStepResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Backend.Handler = (step, token) => { entered.SetResult(); return finish.Task; };
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Windows;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        f.Vm.Operation.CancelCommand.Execute(null);
        Assert.True(f.Vm.IsCancelling);
        Assert.Contains("ne peut pas être interrompue", f.Vm.SequenceStepText);
        Assert.False(pending.IsCompleted);
        finish.SetResult(new(new("software", "Windows Update", UpdateComponent.Windows), true, false, "Terminée"));
        await pending.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Single(f.Backend.Executed);
    }
    [Fact, Trait("Category", "Safety")]
    public async Task Blocked_launcher_is_reported_as_not_executed()
    {
        var f = new Fixture();
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Launcher;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Empty(f.Backend.Executed);
        Assert.Empty(f.History.Report!.ExecutedActions);
        Assert.Contains(f.History.Report.SkippedActions, action => action.Result.Contains("mis à jour au prochain lancement", StringComparison.Ordinal));
        Assert.Equal(ReportSeverity.Success, f.History.Report.Severity);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Update_plan_displays_generic_recent_restore_point_sentence()
    {
        var f = new Fixture();
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        Assert.Contains("Un point de restauration récent (moins de 24 h) pourra être réutilisé si Windows refuse d'en créer un.", f.Vm.ValidationSafety);
        f.Vm.SkipDecisionCommand.Execute(null);
        await pending;
        Assert.Empty(f.Backend.Executed);
    }

    private sealed class CbsProbe : IPendingRebootProbe
    {
        public PendingRebootStatus Read() => new(true, ["Maintenance des composants"]);
    }

    internal sealed class Fixture
    {
        public RecordingConfirmation Confirmation { get; } = new();
        public Backend Backend { get; } = new();
        public History History { get; } = new();
        public Runner Runner { get; } = new();
        public UpdatesViewModel Vm { get; }
        public Fixture(IPendingRebootProbe? probe = null, Func<IUpdateSequenceService, IUpdateSequenceService>? wrap = null)
        {
            IUpdateSequenceService sequence = new UpdateSequenceService(Backend, Confirmation.Authority, probe);
            Vm = new UpdatesViewModel(new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(Runner), Runner, Confirmation.Authority),
                new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), Runner, History, Confirmation, wrap?.Invoke(sequence) ?? sequence,
                new WingetClientModuleService(new Virgil.Infrastructure.Windows.WingetClientModuleInstaller(Runner), Confirmation.Authority));
        }
    }
    internal sealed class Backend : IUpdateSequenceBackend
    {
        public List<UpdateComponent> Executed { get; } = new();
        public bool Reboot { get; set; } = true;
        public Action? OnExecute { get; set; }
        public Func<UpdateSequenceStep, CancellationToken, Task<UpdateSequenceStepResult>>? Handler { get; set; }
        public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["Steam", "Battle.net"]);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken cancellationToken)
        {
            Executed.Add(step.Component);
            OnExecute?.Invoke();
            return Handler?.Invoke(step, cancellationToken) ?? Task.FromResult(new UpdateSequenceStepResult(step, true, Reboot, "Terminé"));
        }
    }
    private sealed class Scan : IUpdateScanService
    {
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken cancellationToken) => Task.FromResult(new UpdateScanReport { IsWingetClientModuleMissing = true });
    }
    internal sealed class Runner : IProcessRunner
    {
        public int Calls { get; private set; }
        public Func<CancellationToken, Task<ProcessRunResult>>? Handler { get; set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Contains("Install-Module", request.Arguments.Last());
            return Handler?.Invoke(cancellationToken) ?? Task.FromResult(new ProcessRunResult(0, "", ""));
        }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Aucun lancement autorisé dans ce test.");
    }
    internal sealed class History : IReportHistoryService
    {
        public ReportEntry? Report { get; private set; }
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken)
        { Report = report; return Task.FromResult(new ReportSaveResult { Success = true }); }
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
