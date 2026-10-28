using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Actions;
using Virgil.Core.Diagnostic;
using Virgil.Core.Startup;
using Virgil.Core.Resources;
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

    [Fact, Trait("Category", "Safety")]
    public void Startup_identical_local_machine_Run_and_Run32_keep_separate_approval_targets()
    {
        var run = new StartupItem("Identique", "Tous les comptes · Registre Run (64 bits)", "exemple.exe /start",
            "Activé", "Éditeur", "Non vérifiée", "Non mesuré",
            new(StartupApprovalHive.LocalMachine, StartupApprovalKind.Run, "Identique"));
        var run32 = run with
        {
            Source = "Tous les comptes · Registre Run (32 bits)",
            ApprovalTarget = run.ApprovalTarget! with { Kind = StartupApprovalKind.Run32 }
        };
        var items = StartupInventoryMerger.Merge([run, run32, run with { Path = "  exemple.exe   /start  " }]);
        Assert.Equal(2, items.Count);
        Assert.Equal(run, Assert.Single(items, item => item.ApprovalTarget == run.ApprovalTarget));
        Assert.Equal(run32, Assert.Single(items, item => item.ApprovalTarget == run32.ApprovalTarget));
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

    [Theory, Trait("Category", "Safety")]
    [InlineData("steam")]
    [InlineData("Battle.net")]
    [InlineData("Battle.net.exe")]
    [InlineData("Agent.exe")]
    public void Tray_only_launcher_over_threshold_is_reported_without_close_action(string name)
    {
        var (snapshot, signals) = DiagnosticAnalysisTests.Problem("process");
        var observation = LauncherObservation(name);
        var target = EvaluateLauncher(observation);
        Assert.Equal(ProcessResourceStatus.Review, target.Status);
        Assert.True(target.IsCriticalSystemProcess);
        Assert.False(target.CanCloseGracefully);
        Assert.False(target.CanForceClose);
        snapshot = snapshot with { Processes = [snapshot.Processes[0] with
        {
            Name = name, Path = DiagnosticAnalysisTests.V(observation.Path!), CpuPercent = DiagnosticAnalysisTests.V(0d),
            WorkingSetBytes = DiagnosticAnalysisTests.V((double)observation.WorkingSetBytes)
        }] };
        signals = signals with { Processes = DiagnosticAnalysisTests.V<IReadOnlyList<ProcessResourceInfo>>([target]) };
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

    [Theory, Trait("Category", "Safety")]
    [InlineData("security")]
    [InlineData("system")]
    [InlineData("identity")]
    public void Protected_launcher_without_window_does_not_become_a_Review_suggestion(string protection)
    {
        var observation = LauncherObservation("steam");
        observation = protection switch
        {
            "security" => observation with { Publisher = "Defender" },
            "system" => observation with { Path = Path.Combine(PolicySystemDirectory, "steam.exe") },
            _ => observation with { AccessDenied = true }
        };
        var target = EvaluateLauncher(observation);
        Assert.True(target.IsCriticalSystemProcess);
        Assert.NotEqual(ProcessResourceStatus.Review, target.Status);
        Assert.False(target.CanCloseGracefully);
        Assert.False(target.CanForceClose);
        var (snapshot, signals) = DiagnosticAnalysisTests.Problem("process");
        snapshot = snapshot with { Processes = [snapshot.Processes[0] with
        {
            Path = DiagnosticAnalysisTests.V(observation.Path!), WorkingSetBytes = DiagnosticAnalysisTests.V(600d * 1024 * 1024)
        }] };
        signals = signals with { Processes = DiagnosticAnalysisTests.V<IReadOnlyList<ProcessResourceInfo>>([target]) };
        var result = DiagnosticAnalyzer.Analyze(snapshot, signals);
        Assert.Empty(result.Issues);
        Assert.Empty(result.ProposedActions);
    }

    private static string PolicySystemDirectory => Path.GetFullPath(Path.Combine("work", "policy", "Windows", "System32"));
    private static ProcessObservation LauncherObservation(string name) => new()
    {
        ProcessId = 42, Name = name,
        Path = Path.GetFullPath(Path.Combine("work", "policy", "games", name == "Agent.exe" ? "Battle.net" : "Steam", name)),
        StartedAt = DateTimeOffset.UnixEpoch, HasMainWindow = false, MainWindowTitle = "",
        WorkingSetBytes = 600L * 1024 * 1024
    };
    private static ProcessResourceInfo EvaluateLauncher(ProcessObservation observation)
    {
        var decision = new ProcessProtectionPolicy(PolicySystemDirectory, int.MaxValue).Evaluate(observation, 0);
        // Même projection que WindowsDiagnosticSignals.ReadProcesses, sans modifier les protections.
        return new ProcessResourceInfo
        {
            ProcessId = observation.ProcessId, Name = observation.Name, Path = observation.Path, Publisher = observation.Publisher,
            StartedAt = observation.StartedAt, MainWindowTitle = observation.MainWindowTitle, WorkingSetBytes = observation.WorkingSetBytes,
            Status = decision.Status, IsCriticalSystemProcess = decision.IsCritical,
            CanCloseGracefully = decision.CanCloseGracefully, CanForceClose = decision.CanForceClose
        };
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

    [Theory]
    [InlineData("CPU")]
    [InlineData("Mémoire")]
    [InlineData("Stockage")]
    public void Failed_scored_category_is_partial_without_penalty(string category)
    {
        var snapshot = WithoutSensors();
        var error = DiagnosticValue<double>.Unavailable("Accès refusé");
        snapshot = category switch
        {
            "CPU" => snapshot with { Cpu = snapshot.Cpu with { TotalPercent = error } },
            "Mémoire" => snapshot with { Memory = snapshot.Memory with { TotalBytes = error, UsedBytes = error } },
            _ => snapshot with { Disks = [snapshot.Disks[0] with { ReadBytesPerSecond = error }] }
        };
        var result = DiagnosticAnalyzer.Analyze(snapshot, DiagnosticAnalysisTests.Signals());
        Assert.Equal(100, result.Score.Value);
        Assert.True(result.Score.IsPartial);
        Assert.Equal(1, result.Score.FailedReadCount);
    }

    [Fact]
    public void Generic_agent_without_Blizzard_evidence_is_not_reported()
    {
        var (snapshot, signals) = DiagnosticAnalysisTests.Problem("process");
        snapshot = snapshot with { Processes = [snapshot.Processes[0] with { Name = "Agent.exe" }] };
        signals = signals with { Processes = DiagnosticAnalysisTests.V<IReadOnlyList<ProcessResourceInfo>>([DiagnosticAnalysisTests.Target() with { Name = "Agent.exe", CanCloseGracefully = false }]) };
        Assert.Empty(DiagnosticAnalyzer.Analyze(snapshot, signals).Issues);
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
