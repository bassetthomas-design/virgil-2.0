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

    private sealed class Fixture
    {
        public RecordingConfirmation Confirmation { get; } = new();
        public Backend Backend { get; } = new();
        public History History { get; } = new();
        public Runner Runner { get; } = new();
        public UpdatesViewModel Vm { get; }
        public Fixture(IPendingRebootProbe? probe = null)
        {
            Vm = new UpdatesViewModel(new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(Runner), Runner, Confirmation.Authority),
                new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), Runner, History, Confirmation, new UpdateSequenceService(Backend, Confirmation.Authority, probe),
                new WingetClientModuleService(new Virgil.Infrastructure.Windows.WingetClientModuleInstaller(Runner), Confirmation.Authority));
        }
    }
    private sealed class Backend : IUpdateSequenceBackend
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
    private sealed class History : IReportHistoryService
    {
        public ReportEntry? Report { get; private set; }
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken)
        { Report = report; return Task.FromResult(new ReportSaveResult { Success = true }); }
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
