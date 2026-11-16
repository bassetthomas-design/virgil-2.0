using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
using Virgil.App.Animation;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Reports;
using Virgil.Core.Updates;
using Xunit;
using System.Xml.Linq;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotHJargonTests
{
    [Fact]
    public Task Real_scan_cards_and_validation_show_plain_action_without_changing_prepared_command() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new Fixture(); await f.Analyze();
        var card = Assert.Single(f.Cards);
        Assert.Equal(new[] { "Version installée : 1", "Version disponible : 2", "Source : Applications", "Risque : validation requise - Motif", "Statut : disponible" }, card.Lines);
        // Ordinal, case sensitive at line start for ID; Ordinal substrings for Commande;
        // OrdinalIgnoreCase substrings for winget. None match imposed retained text.
        Assert.DoesNotContain(card.Lines, line => line.StartsWith("ID :", StringComparison.Ordinal));
        Assert.DoesNotContain(card.Lines, line => line.Contains("Commande :", StringComparison.Ordinal));
        Assert.DoesNotContain(card.Lines, line => line.Contains("winget", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Source : Applications", Assert.Single(card.Lines, line => line.StartsWith("Source :", StringComparison.Ordinal)));
        await LotHWpfTests.CaptureApplication(f.Vm);
        f.SelectAll(); var pending = f.Run();
        try
        {
            Assert.DoesNotContain(f.Vm.ValidationDetails.Split('\n'), line => line.StartsWith("ID :", StringComparison.Ordinal));
            Assert.DoesNotContain("winget", f.Vm.ValidationDetails, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Application : app\nVersion : 1 -> 2\nSource : Applications\nRisque : validation requise\nMotif : Motif", f.Vm.ValidationDetails);
            Assert.Equal("Mettre à jour app vers la version 2.", f.Vm.ValidationCommand);
            Assert.Equal("winget", card.Item.CommandPreview!.ExecutablePath);
            Assert.Equal(new[] { "upgrade", "app test" }, card.Item.CommandPreview.Arguments);
            Assert.Empty(f.Actions.Plans); Assert.Empty(f.Actions.Order);
        }
        finally { f.Vm.SkipDecisionCommand.Execute(null); await pending; }
    });
    private static UpdateItem Item(string id, UpdateRiskLevel risk = UpdateRiskLevel.ValidationRequired) => new() { Id = id, Name = id, InstalledVersion = "1", AvailableVersion = "2", RiskLevel = risk, RiskReason = "Motif", CommandPreview = new() { ExecutablePath = "winget", Arguments = new[] { "upgrade", "app test" } } };
    private static UpdateScanReport Report(params UpdateItem[] items) => new() { Items = items, OverallStatus = "Prêt", Winget = new() { IsAvailable = true, Message = "WinGet disponible" }, WindowsUpdate = new() { Status = "A jour", PendingRebootDetected = true } };
    private sealed class Fixture
    {
        public RecordingConfirmation Confirmation { get; } = new();
        public FakeScan Scan { get; } = new(); public FakeActions Actions { get; } = new(); public FakeHistory History { get; } = new(); public FakeRunner Runner { get; } = new();
        public UpdatesViewModel Vm { get; }
        public List<string> Messages { get; } = new(); public List<(VirgilCoreState State, string Label)> States { get; } = new(); public int Persisted { get; private set; }
        public IEnumerable<UpdateItemViewModel> Cards => Vm.Entries.OfType<UpdateItemViewModel>();
        public Fixture() { Vm = new(Scan, Actions, new WindowsUpdateStatusService(new EmptyPendingRebootProbe()), Runner, History, Confirmation); Vm.VirgilMessageRequested += Messages.Add; Vm.VirgilStateRequested += (s, l) => States.Add((s, l)); Vm.ReportPersisted += (_, _) => Persisted++; }
        public Task Analyze() => Vm.ScanUpdatesCommand.ExecuteAsync(null);
        public Task Run() => Vm.RunSelectedCommand.ExecuteAsync(null);
        public void SelectAll() { foreach (var card in Cards.Where(c => c.IsSelectable)) card.IsSelected = true; }
    }
    private sealed class FakeScan : IUpdateScanService
    {
        public UpdateScanReport Report { get; set; } = LotHJargonTests.Report(Item("app")); public UpdateScanRequest? Request { get; private set; }
        public Func<CancellationToken, Task<UpdateScanReport>>? Handler { get; set; }
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken cancellationToken) { Request = request; return Handler?.Invoke(cancellationToken) ?? Task.FromResult(Report); }
    }
    private sealed class FakeActions : IUpdateExecutionService
    {
        public IUpdateExecutionService? Executor { get; set; }
        public List<ConfirmedPlan> Plans { get; } = new();
        public List<string> Order { get; } = new(); public UpdateSessionReport? Report { get; private set; }
        public Func<UpdateItem, CancellationToken, Task<UpdateExecutionResult>>? Handler { get; set; }
        public Task<UpdateExecutionResult> ExecuteAsync(UpdateItem item, ConfirmedPlan? plan, CancellationToken cancellationToken) { Assert.NotNull(plan); Plans.Add(plan); Order.Add("Execute:" + item.Id); return Executor?.ExecuteAsync(item, plan, cancellationToken) ?? Handler?.Invoke(item, cancellationToken) ?? Task.FromResult(Result(item, UpdateItemStatus.Completed)); }
        public UpdateExecutionResult Skip(UpdateItem item) { Order.Add("Skip:" + item.Id); return Result(item, UpdateItemStatus.Skipped); }
        public UpdateExecutionResult Cancel(UpdateItem item) { Order.Add("Cancel:" + item.Id); return Result(item, UpdateItemStatus.Cancelled); }
        private static UpdateExecutionResult Result(UpdateItem item, UpdateItemStatus status) => new() { Item = item, Status = status };
        public UpdateSessionReport CreateReport(DateTimeOffset startedAt, IReadOnlyList<UpdateExecutionResult> results, IReadOnlyList<string> errors) { Report = new() { StartedAt = startedAt, Results = results.ToArray(), Errors = errors.ToArray() }; return Report; }
    }
    private sealed class FakeRunner : IProcessRunner
    {
        public int Calls { get; private set; } public int Failure { get; set; } public ProcessLaunchRequest? Request { get; private set; } public CancellationToken Token { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Aucune exécution processus attendue.");
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) { Calls++; Request = request; Token = cancellationToken; if (Failure == 2) throw new InvalidOperationException(); return Task.FromResult(new ProcessLaunchResult(Failure == 0, null, Failure == 1 ? "Erreur" : null)); }
    }
    private sealed class FakeHistory : IReportHistoryService
    {
        public ReportSaveResult Result { get; set; } = new() { Success = true }; public CancellationToken Token { get; private set; }
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken) { Token = cancellationToken; return Task.FromResult(Result); }
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
