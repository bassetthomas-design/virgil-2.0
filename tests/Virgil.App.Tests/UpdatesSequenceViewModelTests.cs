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
    public async Task PreviewAllowsExclusionsAndRunsSelectedComponentsWithOnePlan()
    {
        var f = new Fixture();
        Assert.True(f.Vm.IsRunAllEnabled);
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        Assert.True(f.Vm.IsValidationOpen);
        Assert.True(f.Vm.IsSequencePreview);
        Assert.Empty(f.Backend.Executed);
        Assert.Equal(0, f.Confirmation.Calls);
        var driver = Assert.Single(f.Vm.SequenceChoices, choice => choice.Step.Component == UpdateComponent.Driver);
        Assert.False(driver.IsSelected);
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

    private sealed class Fixture
    {
        public RecordingConfirmation Confirmation { get; } = new();
        public Backend Backend { get; } = new();
        public History History { get; } = new();
        public Runner Runner { get; } = new();
        public UpdatesViewModel Vm { get; }
        public Fixture()
        {
            Vm = new UpdatesViewModel(new Scan(), new WingetUpdateExecutionService(new WingetAvailabilityService(Runner), Runner, Confirmation.Authority),
                new WindowsUpdateStatusService(), Runner, History, Confirmation, new UpdateSequenceService(Backend, Confirmation.Authority),
                new WingetClientModuleService(new Virgil.Infrastructure.Windows.WingetClientModuleInstaller(Runner), Confirmation.Authority));
        }
    }
    private sealed class Backend : IUpdateSequenceBackend
    {
        public List<UpdateComponent> Executed { get; } = new();
        public Action? OnExecute { get; set; }
        public Func<UpdateSequenceStep, CancellationToken, Task<UpdateSequenceStepResult>>? Handler { get; set; }
        public Task<IReadOnlyList<UpdateSequenceStep>> ScanDriversAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UpdateSequenceStep>>([new("driver", "Pilote facultatif", UpdateComponent.Driver)]);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken cancellationToken)
        {
            Executed.Add(step.Component);
            OnExecute?.Invoke();
            return Handler?.Invoke(step, cancellationToken) ?? Task.FromResult(new UpdateSequenceStepResult(step, true, true, "Terminé"));
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
