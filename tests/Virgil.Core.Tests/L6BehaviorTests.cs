using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Actions;
using Virgil.Core.Diagnostic;
using Virgil.Core.Startup;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows.Startup;
using Xunit;
using StartupApprovalKind = Virgil.Core.Startup.StartupApprovalKind;

namespace Virgil.Tests;

public sealed class L6BehaviorTests
{
    [Fact, Trait("Category", "Safety")]
    public void Startup_current_user_run_entries_are_listed_once_with_run_approval()
    {
        var reader = new RegistryReader();
        var inventory = WindowsStartupInventorySource.ReadRunEntries(reader);
        var item = Assert.Single(inventory.Items, i => i.ApprovalTarget!.Hive == StartupApprovalHive.CurrentUser);
        Assert.Equal(StartupApprovalKind.Run, item.ApprovalTarget!.Kind);
        Assert.Equal("Compte actuel · Registre Run", item.Source);
        Assert.DoesNotContain((StartupApprovalHive.CurrentUser, true), reader.Reads);
        // Ceinture : même une ancienne entrée HKCU Run32 est fusionnée vers Run.
        var duplicated = StartupInventoryMerger.Merge([item, item with { Path = "  exemple.exe   /start ", ApprovalTarget = item.ApprovalTarget with { Kind = StartupApprovalKind.Run32 } }]);
        Assert.Equal(StartupApprovalKind.Run, Assert.Single(duplicated).ApprovalTarget!.Kind);
    }

    [Fact]
    public void Startup_local_machine_views_keep_distinct_names_and_correct_approval()
    {
        var items = WindowsStartupInventorySource.ReadRunEntries(new RegistryReader()).Items
            .Where(i => i.ApprovalTarget!.Hive == StartupApprovalHive.LocalMachine).ToArray();
        Assert.Equal(2, items.Length);
        Assert.Contains(items, i => i.Name == "Machine64" && i.ApprovalTarget!.Kind == StartupApprovalKind.Run);
        Assert.Contains(items, i => i.Name == "Machine32" && i.ApprovalTarget!.Kind == StartupApprovalKind.Run32);
    }

    [Fact]
    public void Absent_sensors_do_not_make_health_partial_or_cost_points()
    {
        var result = DiagnosticAnalyzer.Analyze(WithoutSensors(), DiagnosticAnalysisTests.Signals());
        Assert.False(result.Score.IsPartial);
        Assert.Equal(100, result.Score.Value);
        Assert.Equal(0, result.Score.FailedReadCount);
        Assert.NotEmpty(result.Unavailable);
        Assert.All(result.Unavailable, reason => Assert.StartsWith("Non mesuré sur ce PC", reason));
    }

    [Fact]
    public void Failed_process_read_makes_health_partial_with_one_failure()
    {
        var signals = DiagnosticAnalysisTests.Signals() with { Processes = DiagnosticSignals.Unavailable.Processes };
        var result = DiagnosticAnalyzer.Analyze(WithoutSensors(), signals);
        Assert.True(result.Score.IsPartial);
        Assert.Equal(1, result.Score.FailedReadCount);
        Assert.Equal(100, result.Score.Value);
    }

    [Theory]
    [InlineData("steam")]
    [InlineData("Battle.net")]
    [InlineData("Battle.net.exe")]
    [InlineData("Agent.exe")]
    public void Tray_only_launcher_over_threshold_is_reported_without_close_action(string name)
    {
        var (snapshot, signals) = DiagnosticAnalysisTests.Problem("process");
        snapshot = snapshot with { Processes = [snapshot.Processes[0] with { Name = name, CpuPercent = DiagnosticAnalysisTests.V(0d), WorkingSetBytes = DiagnosticAnalysisTests.V(600d * 1024 * 1024) }] };
        signals = signals with { Processes = DiagnosticAnalysisTests.V<IReadOnlyList<ProcessResourceInfo>>([DiagnosticAnalysisTests.Target() with { Name = name, CanCloseGracefully = false, CanForceClose = false, MainWindowTitle = "" }]) };
        var result = DiagnosticAnalyzer.Analyze(snapshot, signals);
        var issue = Assert.Single(result.Issues);
        Assert.Equal((name == "steam" ? "Steam" : name) + " : lanceur gourmand en arrière-plan", issue.Title);
        Assert.Contains("Quittez-le depuis son icône", issue.Description);
        Assert.Null(issue.Process);
        Assert.Equal(DiagnosticSuggestion.None, issue.Suggestion);
        Assert.DoesNotContain(result.Issues, i => i.Suggestion == DiagnosticSuggestion.Close);
        Assert.Empty(result.ProposedActions);
        Assert.Equal(95, result.Score.Value);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Launcher_lines_are_informational_never_executed_nor_counted_as_success()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var backend = new Backend { Installed = ["Steam", "Battle.net"] };
        var service = new UpdateSequenceService(backend, authority);
        var scan = await service.ScanAsync([], CancellationToken.None);
        Assert.Equal(2, scan.Count(step => step.Component == UpdateComponent.Launcher));
        var preview = service.CreatePreview(scan);
        Assert.DoesNotContain(preview.Targets, target => target.Contains("Steam", StringComparison.Ordinal));
        Assert.DoesNotContain("Battle.net", preview.Parameters["Steps"]);
        var plan = authority.Issue(preview, new(ConfirmationLevel.Reinforced, null));
        var result = await service.ExecuteAsync(scan, plan, null, CancellationToken.None);
        Assert.False(result.WasRefused);
        Assert.DoesNotContain(backend.Executed, step => step.Component == UpdateComponent.Launcher);
        Assert.Equal(2, result.Steps.Count(step => step.Message == "À faire dans le lanceur"));
        Assert.All(result.Steps.Where(step => step.Step.Component == UpdateComponent.Launcher), step => Assert.False(step.Succeeded));
        Assert.Equal(2, result.SuccessfulCount);
        Assert.False(result.HasFailures);
        backend.Installed = [];
        Assert.DoesNotContain(await service.ScanAsync([], CancellationToken.None), step => step.Component == UpdateComponent.Launcher);
    }

    internal static DiagnosticSnapshot WithoutSensors()
    {
        var s = DiagnosticAnalysisTests.Healthy();
        return s with
        {
            Cpu = s.Cpu with { TemperatureCelsius = DiagnosticValue<double>.Unmeasured("Capteur absent") },
            Gpu = [],
            Disks = [s.Disks[1]]
        };
    }
    private sealed class RegistryReader : IStartupRegistryReader
    {
        public List<(StartupApprovalHive, bool)> Reads { get; } = [];
        public IReadOnlyList<StartupRunValue> ReadRun(StartupApprovalHive hive, bool is32Bit)
        {
            Reads.Add((hive, is32Bit));
            // HKCU renvoie délibérément la même valeur quelle que soit la vue demandée.
            return [new(hive == StartupApprovalHive.CurrentUser ? "Utilisateur" : is32Bit ? "Machine32" : "Machine64", "exemple.exe /start", new byte[12])];
        }
    }
    private sealed class Backend : IUpdateSequenceBackend
    {
        public IReadOnlyList<string> Installed { get; set; } = [];
        public List<UpdateSequenceStep> Executed { get; } = [];
        public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken token) => Task.FromResult(Installed);
        public Task<IReadOnlyList<UpdateSequenceStep>> ScanDriversAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<UpdateSequenceStep>>([]);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token)
        { Executed.Add(step); return Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Terminée")); }
    }
}
