using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Interventions;
using Virgil.Core.Reports;
using Xunit;

namespace Virgil.Tests;

public sealed class LotBureauViewModelTests
{
    [Fact]
    public async Task T5_Verify_then_scan_repair_never_requests_or_executes_explorer_restart()
    {
        var authority = new RecordingConfirmation();
        var helper = new Helper();
        var explorer = new Explorer();
        var history = new History();
        var vm = new InterventionsViewModel(new Diagnostics(),
            new InterventionExecutionService(helper, explorer, authority.Authority),
            new InterventionReportBuilder(), history, authority);
        // La fixture coche explicitement les constats rendus : les réparations du vrai catalogue sont sensibles.
        foreach (var items in new[] { vm.SystemItems, vm.NetworkItems, vm.StorageItems, vm.InterfaceItems })
            items.CollectionChanged += (_, change) =>
            {
                if (change.NewItems is null) return;
                foreach (var item in change.NewItems.OfType<InterventionItemViewModel>()) item.IsSelected = true;
            };
        var decisions = new List<string>();
        await Complete(vm.VerifyHealthCommand.ExecuteAsync(null));
        Assert.Equal(new[] { InterventionId.VerifyHealth }, helper.Actions);
        Assert.DoesNotContain(vm.InterfaceItems, item => item.Diagnostic.Definition.Id == InterventionId.RestartExplorer);
        vm.CloseReportCommand.Execute(null);
        await Complete(vm.ScanRepairCommand.ExecuteAsync(null));
        Assert.Contains(InterventionId.FlushDns, helper.Actions);
        Assert.Contains(InterventionId.SfcScan, helper.Actions);
        Assert.Contains(InterventionId.CreateRestorePoint, helper.Actions);
        Assert.Equal(3, helper.Actions.Count(id => id == InterventionId.VerifyHealth));
        Assert.DoesNotContain(InterventionId.RestartExplorer, helper.Actions);
        Assert.DoesNotContain("interventions.restartexplorer", authority.Actions);
        Assert.All(decisions, title => Assert.NotEqual(new InterventionCatalog().Get(InterventionId.RestartExplorer).Title, title));
        Assert.Equal(0, explorer.Calls);
        Assert.NotEmpty(history.Reports);
        Assert.All(history.Reports, report => Assert.Empty(report.Errors));

        async Task Complete(Task operation)
        {
            for (var count = 0; !operation.IsCompleted && count < 30; count++)
            {
                Assert.True(vm.IsValidationOpen);
                decisions.Add(vm.ValidationDetails.Split('\n')[0]);
                Assert.NotEqual(new InterventionCatalog().Get(InterventionId.RestartExplorer).Title, decisions[^1]);
                vm.ExecuteDecisionCommand.Execute(null);
            }
            Assert.True(operation.IsCompleted, "Le parcours doit se terminer après les décisions explicites.");
            await operation;
        }
    }

    private sealed class RecordingConfirmation : IConfirmationService
    {
        public ConfirmationService Authority { get; } = new(TimeProvider.System);
        public List<string> Actions { get; } = new();
        public ConfirmedPlan Issue(ActionPreview preview, ConfirmationConsent consent)
        { Actions.Add(preview.ActionId); return Authority.Issue(preview, consent); }
    }

    private sealed class Helper : IInterventionElevatedHelperClient
    {
        public List<InterventionId> Actions { get; } = new();
        public Task<ElevatedInterventionResult> ExecuteAsync(InterventionDefinition definition, CancellationToken cancellationToken)
        {
            Actions.Add(definition.Id);
            return Task.FromResult(new ElevatedInterventionResult
            {
                ActionId = definition.Id, Status = InterventionStatus.Completed,
                SummaryOutput = string.Join('\n', new[] { "SFC", "DISM", "Audiosrv", "AudioEndpointBuilder", "PrintQueue", "Spooler", "DNS", "CHKDSK", "Services", "Gateway", "Reboot" }
                    .Select(key => $"VIRGIL|{key}|FINDING|Constat"))
            });
        }
    }

    private sealed class Diagnostics : IInterventionDiagnosticService
    {
        public Task<IReadOnlyList<InterventionDiagnostic>> DiagnoseAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InterventionDiagnostic>>(new InterventionCatalog().GetAll()
                .Select(definition => new InterventionDiagnostic { Definition = definition, IsAvailable = true, Status = InterventionStatus.Available }).ToArray());
        public Task<InterventionDiagnostic> DiagnoseAsync(InterventionId id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Explorer : IExplorerRestarter
    {
        public int Calls { get; private set; }
        public Task<InterventionExecutionResult> RestartAsync(InterventionDiagnostic diagnostic, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException(); }
    }

    private sealed class History : IReportHistoryService
    {
        public List<ReportEntry> Reports { get; } = new();
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken)
        { Reports.Add(report); return Task.FromResult(new ReportSaveResult { Success = true }); }
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
