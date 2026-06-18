using Virgil.Core.Interventions;
using Virgil.Core.Resources;
using Virgil.Domain;
using Xunit;

namespace Virgil.Tests;

public sealed class ResourcesModuleTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 6, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Memory_calculation_uses_total_minus_available()
    {
        var memory = new MemoryStatus(16_000, 4_000);

        Assert.Equal((ulong)12_000, memory.UsedBytes);
        Assert.Equal(75, memory.UsedPercent);
    }

    [Theory]
    [InlineData(69, ResourceHealthLevel.Stable)]
    [InlineData(70, ResourceHealthLevel.Watch)]
    [InlineData(85, ResourceHealthLevel.InterventionRecommended)]
    [InlineData(95, ResourceHealthLevel.Critical)]
    public void Memory_thresholds_are_conservative(double percent, ResourceHealthLevel expected)
    {
        Assert.Equal(expected, new ResourceRecommendationService().ClassifyMemory(percent));
    }

    [Fact]
    public void Isolated_cpu_peak_is_not_critical()
    {
        var health = new ResourceRecommendationService().ClassifyCpu(new[] { 20d, 25d, 99d, 22d, 24d });

        Assert.Equal(ResourceHealthLevel.Watch, health);
    }

    [Fact]
    public void Sustained_high_cpu_recommends_intervention()
    {
        var health = new ResourceRecommendationService().ClassifyCpu(new[] { 87d, 89d, 91d, 88d });

        Assert.Equal(ResourceHealthLevel.InterventionRecommended, health);
    }

    [Fact]
    public void Long_uptime_recommends_manual_restart()
    {
        var service = new ResourceRecommendationService();

        Assert.Equal(ResourceHealthLevel.InterventionRecommended, service.ClassifyUptime(TimeSpan.FromDays(8)));
        Assert.Contains("redemarrage manuel", service.BuildRecommendations(
            ResourceHealthLevel.Stable,
            ResourceHealthLevel.Stable,
            TimeSpan.FromDays(8),
            Array.Empty<ProcessResourceInfo>())[0], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("csrss")]
    [InlineData("winlogon")]
    [InlineData("lsass")]
    [InlineData("svchost")]
    [InlineData("dwm")]
    public void Critical_system_processes_are_protected(string name)
    {
        var decision = Policy().Evaluate(Observation(name, path: $@"C:\Windows\System32\{name}.exe"), 10);

        Assert.True(decision.IsCritical);
        Assert.False(decision.CanCloseGracefully);
        Assert.False(decision.CanForceClose);
    }

    [Theory]
    [InlineData("MsMpEng", @"C:\Program Files\Windows Defender\MsMpEng.exe")]
    [InlineData("openvpn", @"C:\Program Files\OpenVPN\openvpn.exe")]
    [InlineData("WireGuard", @"C:\Program Files\WireGuard\wireguard.exe")]
    public void Security_and_vpn_processes_are_protected(string name, string path)
    {
        var decision = Policy().Evaluate(Observation(name, path), 5);

        Assert.True(decision.IsCritical);
        Assert.Equal(ProcessResourceStatus.Protected, decision.Status);
    }

    [Fact]
    public void User_application_with_window_is_closable()
    {
        var decision = Policy().Evaluate(Observation("ExampleApp", @"C:\Apps\Example\ExampleApp.exe"), 4);

        Assert.False(decision.IsCritical);
        Assert.True(decision.CanCloseGracefully);
        Assert.True(decision.CanForceClose);
    }

    [Fact]
    public async Task Graceful_close_never_kills_when_application_stays_open()
    {
        var runtime = new FakeProcessRuntime { ExitAfterWait = false, CloseResult = true };
        var service = CreateActionService(runtime);

        var result = await service.ExecuteAsync(
            ProcessActionKind.CloseMainWindow,
            Target(),
            confirmed: true,
            reinforcedConfirmation: false,
            CancellationToken.None);

        Assert.Equal(ProcessActionStatus.PartialFailure, result.Status);
        Assert.Equal(0, runtime.KillCalls);
        Assert.Equal(1, runtime.CloseCalls);
    }

    [Fact]
    public async Task Forced_close_requires_reinforced_confirmation()
    {
        var runtime = new FakeProcessRuntime();
        var service = CreateActionService(runtime);

        var result = await service.ExecuteAsync(
            ProcessActionKind.KillProcess,
            Target(),
            confirmed: true,
            reinforcedConfirmation: false,
            CancellationToken.None);

        Assert.Equal(ProcessActionStatus.Failed, result.Status);
        Assert.Equal(0, runtime.KillCalls);
    }

    [Fact]
    public async Task Changed_process_identity_is_refused()
    {
        var runtime = new FakeProcessRuntime
        {
            Identity = Identity() with { StartedAt = StartedAt.AddMinutes(1) }
        };
        var service = CreateActionService(runtime);

        var result = await service.ExecuteAsync(
            ProcessActionKind.KillProcess,
            Target(),
            confirmed: true,
            reinforcedConfirmation: true,
            CancellationToken.None);

        Assert.Equal(ProcessActionStatus.Failed, result.Status);
        Assert.Equal(0, runtime.KillCalls);
        Assert.Contains("Identite", result.ReadableError);
    }

    [Fact]
    public void Inaccessible_process_path_is_protected()
    {
        var observation = Observation("UnknownApp", path: null) with { AccessDenied = true };
        var decision = Policy().Evaluate(observation, 2);

        Assert.Equal(ProcessResourceStatus.Protected, decision.Status);
        Assert.False(decision.CanCloseGracefully);
    }

    [Fact]
    public async Task Open_location_is_refused_without_accessible_path()
    {
        var runtime = new FakeProcessRuntime();
        var service = CreateActionService(runtime);
        var target = Target() with { Path = null };

        var result = await service.ExecuteAsync(
            ProcessActionKind.OpenLocation,
            target,
            confirmed: true,
            reinforcedConfirmation: false,
            CancellationToken.None);

        Assert.Equal(ProcessActionStatus.Failed, result.Status);
        Assert.Equal(0, runtime.OpenLocationCalls);
    }

    [Fact]
    public async Task Inactive_memory_release_is_information_only()
    {
        var service = CreateActionService(new FakeProcessRuntime());

        var result = await service.ExecuteAsync(
            ProcessActionKind.ReleaseInactiveMemory,
            null,
            confirmed: true,
            reinforcedConfirmation: false,
            CancellationToken.None);

        Assert.False(service.CanReleaseInactiveMemory);
        Assert.Equal(ProcessActionStatus.InformationOnly, result.Status);
    }

    [Fact]
    public async Task Restart_explorer_uses_existing_restarter()
    {
        var restarter = new FakeExplorerRestarter();
        var service = CreateActionService(new FakeProcessRuntime(), restarter);

        var result = await service.ExecuteAsync(
            ProcessActionKind.RestartExplorer,
            null,
            confirmed: true,
            reinforcedConfirmation: false,
            CancellationToken.None);

        Assert.Equal(ProcessActionStatus.Completed, result.Status);
        Assert.Equal(1, restarter.Calls);
    }

    [Fact]
    public async Task Monitoring_aggregates_short_cpu_and_memory_observation()
    {
        var cpuValues = new Queue<double>(new[] { 20d, 40d, 60d });
        var monitoring = new ResourceMonitoringService(
            new FakeProcessInspectionService(),
            new ResourceRecommendationService(),
            (_, _) => Task.FromResult(cpuValues.Dequeue()),
            () => new MemoryStatus(1000, 250),
            () => TimeSpan.FromDays(2),
            () => 42,
            () => StartedAt);

        var report = await monitoring.AnalyzeAsync(new ResourceAnalysisRequest
        {
            ObservationDuration = TimeSpan.FromMilliseconds(3),
            SampleCount = 3,
            MaximumProcesses = 5
        }, null, CancellationToken.None);

        Assert.Equal(40, report.AverageCpuPercent);
        Assert.Equal(60, report.MaximumCpuPercent);
        Assert.Equal(75, report.AverageMemoryPercent);
        Assert.Equal(3, report.Samples.Count);
        Assert.Equal(42, report.ProcessCount);
    }

    [Fact]
    public void Resource_report_aggregates_cpu_ram_processes_and_actions()
    {
        var analysis = new ResourceAnalysisReport
        {
            CapturedAt = StartedAt,
            Duration = TimeSpan.FromSeconds(5),
            AverageCpuPercent = 35,
            MaximumCpuPercent = 70,
            AverageMemoryPercent = 72,
            MaximumMemoryPercent = 75,
            Samples = new[] { new ResourceSample { Uptime = TimeSpan.FromDays(8) } },
            TopMemoryProcesses = new[] { Target() },
            TopCpuProcesses = new[] { Target() }
        };
        var report = new ResourceSessionReport
        {
            Analyses = new[] { analysis },
            ProposedActions = new[] { "Examiner ExampleApp" },
            ExecutedActions = new[]
            {
                new ProcessActionResult
                {
                    Action = ProcessActionKind.CloseMainWindow,
                    Target = "ExampleApp",
                    Status = ProcessActionStatus.Completed,
                    Summary = "Fermee"
                }
            },
            RestartRecommended = true
        };

        var text = new ResourceReportBuilder().Build(report);

        Assert.Contains("CPU moyen : 35", text);
        Assert.Contains("RAM moyenne : 72", text);
        Assert.Contains("ExampleApp", text);
        Assert.Contains("Redemarrage conseille : oui", text);
    }

    private static ProcessProtectionPolicy Policy()
    {
        return new ProcessProtectionPolicy(@"C:\Windows\System32", currentProcessId: 99999);
    }

    private static ProcessObservation Observation(string name, string? path)
    {
        return new ProcessObservation
        {
            ProcessId = 1234,
            Name = name,
            Path = path,
            MainWindowTitle = name,
            HasMainWindow = true,
            StartedAt = StartedAt,
            WorkingSetBytes = 200 * 1024 * 1024
        };
    }

    private static ProcessResourceInfo Target()
    {
        return new ProcessResourceInfo
        {
            ProcessId = 1234,
            Name = "ExampleApp",
            MainWindowTitle = "Example App",
            Path = @"C:\Apps\Example\ExampleApp.exe",
            Publisher = "Example Publisher",
            WorkingSetBytes = 600 * 1024 * 1024,
            CpuPercent = 12,
            Status = ProcessResourceStatus.Heavy,
            CanCloseGracefully = true,
            CanForceClose = true,
            StartedAt = StartedAt
        };
    }

    private static ProcessRuntimeIdentity Identity()
    {
        return new ProcessRuntimeIdentity(
            1234,
            "ExampleApp",
            @"C:\Apps\Example\ExampleApp.exe",
            StartedAt,
            HasMainWindow: true,
            AccessDenied: false);
    }

    private static ProcessActionService CreateActionService(
        FakeProcessRuntime runtime,
        FakeExplorerRestarter? restarter = null)
    {
        return new ProcessActionService(
            runtime,
            Policy(),
            restarter ?? new FakeExplorerRestarter(),
            new InterventionCatalog(),
            () => StartedAt);
    }

    private sealed class FakeProcessRuntime : IProcessRuntime
    {
        public ProcessRuntimeIdentity? Identity { get; set; } = ResourcesModuleTests.Identity();

        public bool CloseResult { get; set; } = true;

        public bool ExitAfterWait { get; set; } = true;

        public int CloseCalls { get; private set; }

        public int KillCalls { get; private set; }

        public int OpenLocationCalls { get; private set; }

        public ProcessRuntimeIdentity? ReadIdentity(int processId) => Identity;

        public bool CloseMainWindow(int processId)
        {
            CloseCalls++;
            return CloseResult;
        }

        public Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
        {
            return Task.FromResult(ExitAfterWait);
        }

        public void Kill(int processId)
        {
            KillCalls++;
        }

        public bool FileExists(string path) => !string.IsNullOrWhiteSpace(path);

        public bool OpenLocation(string path)
        {
            OpenLocationCalls++;
            return true;
        }
    }

    private sealed class FakeExplorerRestarter : IExplorerRestarter
    {
        public int Calls { get; private set; }

        public Task<InterventionExecutionResult> RestartAsync(
            InterventionDiagnostic diagnostic,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new InterventionExecutionResult
            {
                Action = diagnostic.Definition,
                Status = InterventionStatus.Completed,
                SummaryOutput = "Explorer relance."
            });
        }
    }

    private sealed class FakeProcessInspectionService : IProcessInspectionService
    {
        public Task<ProcessInspectionResult> InspectAsync(
            TimeSpan observationDuration,
            int maximumProcesses,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProcessInspectionResult
            {
                Processes = new[] { Target() }
            });
        }
    }
}
