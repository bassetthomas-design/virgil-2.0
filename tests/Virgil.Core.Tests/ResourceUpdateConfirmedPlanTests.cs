using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Interventions;
using Virgil.Core.Processes;
using Virgil.Core.Resources;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class ResourceUpdateConfirmedPlanTests
{
    private static ProcessResourceInfo Target() => new()
    {
        ProcessId = 1234, Name = "ExampleApp", Path = @"C:\Apps\ExampleApp.exe",
        StartedAt = DateTimeOffset.UnixEpoch, MainWindowTitle = "Example",
        CanCloseGracefully = true, CanForceClose = true
    };
    private static UpdateItem Item() => new()
    {
        Id = "Example.App", Name = "Example", Source = UpdateSource.Winget,
        InstalledVersion = "1", AvailableVersion = "2", CommandPreview = new()
        { ExecutablePath = "winget", Arguments = ["upgrade", "--id", "Example.App", "--exact"] }
    };
    private static ConfirmedPlan Issue(ConfirmationService authority, ActionPreview preview) =>
        authority.Issue(preview, new ConfirmationConsent(RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel), null));
    private static ProcessActionService Service(Runtime runtime, Restarter restarter, IConfirmedPlanGate gate) =>
        new(runtime, new ProcessProtectionPolicy(@"C:\Windows\System32", 99999), restarter,
            new InterventionCatalog(), () => DateTimeOffset.UnixEpoch, gate);

    [Theory]
    [Trait("Category", "Windows")]
    [InlineData("missing")]
    [InlineData("other-authority")]
    [InlineData("expired")]
    [InlineData("pid")]
    [InlineData("name")]
    [InlineData("path")]
    [InlineData("started")]
    [InlineData("runtime-identity")]
    [InlineData("close-for-kill")]
    [InlineData("simple-kill")]
    [InlineData("reused")]
    public async Task ResourceRefusalHasNoRuntimeEffect(string scenario)
    {
        var clock = new Clock();
        var authority = new ConfirmationService(clock);
        var target = Target();
        var runtime = new Runtime { Target = target };
        var restarter = new Restarter();
        var service = Service(runtime, restarter, authority);
        var action = scenario is "close-for-kill" or "simple-kill" ? ProcessActionKind.KillProcess : ProcessActionKind.CloseMainWindow;
        var preview = ProcessActionPreviewFactory.Create(scenario == "close-for-kill" ? ProcessActionKind.CloseMainWindow : action, target);
        if (scenario == "simple-kill") preview = preview with { RiskLevel = RiskLevel.Low };
        var plan = scenario == "missing" ? null : Issue(scenario == "other-authority" ? new ConfirmationService(clock) : authority, preview);
        if (scenario == "expired") clock.Advance(TimeSpan.FromMinutes(10));
        target = scenario switch
        {
            "pid" => target with { ProcessId = 1235 },
            "name" => target with { Name = "OtherApp" },
            "path" => target with { Path = @"C:\Apps\Other.exe" },
            "started" => target with { StartedAt = target.StartedAt!.Value.AddSeconds(1) },
            _ => target
        };
        runtime.Target = scenario == "runtime-identity" ? target with { StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(1) } : target;
        if (scenario == "reused")
        {
            Assert.Equal(ProcessActionStatus.Completed, (await service.ExecuteAsync(action, target, plan, CancellationToken.None)).Status);
            runtime.Effects = 0;
        }
        var result = await service.ExecuteAsync(action, target, plan, CancellationToken.None);
        Assert.Equal(ProcessActionStatus.Failed, result.Status);
        if (scenario != "runtime-identity") Assert.StartsWith("Action annulee.\n", result.ReadableError);
        Assert.Equal(0, runtime.Effects);
        Assert.Equal(0, restarter.Calls);
    }

    [Theory]
    [Trait("Category", "Windows")]
    [InlineData(ProcessActionKind.CloseMainWindow)]
    [InlineData(ProcessActionKind.KillProcess)]
    [InlineData(ProcessActionKind.RestartExplorer)]
    public async Task ResourcesConsumeOnceImmediatelyBeforeEffect(ProcessActionKind action)
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var target = action == ProcessActionKind.RestartExplorer ? null : Target();
        var runtime = new Runtime(); var restarter = new Restarter();
        var service = Service(runtime, restarter, authority);
        var plan = Issue(authority, ProcessActionPreviewFactory.Create(action, target));
        Assert.Equal(ProcessActionStatus.Completed, (await service.ExecuteAsync(action, target, plan, CancellationToken.None)).Status);
        Assert.Equal(ProcessActionStatus.Failed, (await service.ExecuteAsync(action, target, plan, CancellationToken.None)).Status);
        Assert.Equal(1, runtime.Effects + restarter.Calls);
    }

    [Theory]
    [Trait("Category", "Windows")]
    [InlineData("critical")]
    [InlineData("identity")]
    [InlineData("policy")]
    [InlineData("cannot-close")]
    [InlineData("cannot-kill")]
    public async Task ResourceGuardsPrecedeGate(string scenario)
    {
        var target = Target(); var runtime = new Runtime(); var gate = new RejectGate();
        target = scenario switch
        {
            "critical" => target with { IsCriticalSystemProcess = true },
            "policy" => target with { Name = "winlogon" },
            "cannot-close" => target with { CanCloseGracefully = false },
            "cannot-kill" => target with { CanForceClose = false },
            _ => target
        };
        runtime.Target = scenario == "identity" ? target with { StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(1) } : target;
        var result = await Service(runtime, new Restarter(), gate).ExecuteAsync(
            scenario == "cannot-kill" ? ProcessActionKind.KillProcess : ProcessActionKind.CloseMainWindow,
            target, null, CancellationToken.None);
        Assert.Equal(ProcessActionStatus.Failed, result.Status);
        Assert.Equal(0, gate.Calls); Assert.Equal(0, runtime.Effects);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("other-authority")]
    [InlineData("other-item")]
    [InlineData("available-version")]
    [InlineData("installed-version")]
    [InlineData("command")]
    [InlineData("simple-sensitive")]
    [InlineData("reused")]
    public async Task UpdateRefusalNeverRunsUpgrade(string scenario)
    {
        var clock = new Clock(); var authority = new ConfirmationService(clock); var runner = new Runner();
        var service = new WingetUpdateExecutionService(new WingetAvailabilityService(runner, ["winget"]), runner, authority);
        var item = Item();
        if (scenario == "simple-sensitive") item = item with { RiskLevel = UpdateRiskLevel.Sensitive };
        var preview = UpdateActionPreviewFactory.Create(item);
        if (scenario == "simple-sensitive") preview = preview with { RiskLevel = RiskLevel.Low };
        var plan = scenario == "missing" ? null : Issue(scenario == "other-authority" ? new ConfirmationService(clock) : authority, preview);
        if (scenario == "expired") clock.Advance(TimeSpan.FromMinutes(10));
        item = scenario switch
        {
            "other-item" => item with { Id = "Other.App" },
            "available-version" => item with { AvailableVersion = "3" },
            "installed-version" => item with { InstalledVersion = "0" },
            "command" => item with { CommandPreview = new() { ExecutablePath = "other" } },
            _ => item
        };
        if (scenario == "reused")
        {
            Assert.Equal(UpdateItemStatus.Completed, (await service.ExecuteAsync(item, plan, CancellationToken.None)).Status);
            runner.Upgrades = 0;
        }
        var result = await service.ExecuteAsync(item, plan, CancellationToken.None);
        Assert.Equal(UpdateItemStatus.Cancelled, result.Status);
        Assert.StartsWith("Installation annulee.\n", result.UserMessage);
        Assert.Equal(0, runner.Upgrades);
    }

    [Theory]
    [InlineData(UpdateRiskLevel.Safe, "v1.0.0")]
    [InlineData(UpdateRiskLevel.ValidationRequired, "v1.8.0")]
    [InlineData(UpdateRiskLevel.Sensitive, "v1.8.0")]
    public async Task UpdatePlanIsIndependentOfDetectedWinGetVersion(UpdateRiskLevel risk, string version)
    {
        var authority = new ConfirmationService(TimeProvider.System); var runner = new Runner { Version = version };
        var item = Item() with { RiskLevel = risk };
        var service = new WingetUpdateExecutionService(new WingetAvailabilityService(runner, ["winget"]), runner, authority);
        Assert.Equal(UpdateItemStatus.Completed, (await service.ExecuteAsync(item, Issue(authority, UpdateActionPreviewFactory.Create(item)), CancellationToken.None)).Status);
        Assert.Equal(1, runner.Upgrades);
        var arguments = Assert.Single(runner.UpgradeRequests).Arguments.ToList();
        Assert.Equal(item.AvailableVersion, arguments[arguments.IndexOf("--version") + 1]);
        Assert.Equal(item.Id, arguments[arguments.IndexOf("--id") + 1]);
        Assert.Contains("--exact", arguments);
        if (version == "v1.8.0")
        {
            Assert.Contains("--accept-package-agreements", arguments);
            Assert.Contains("--accept-source-agreements", arguments);
            Assert.Contains("--disable-interactivity", arguments);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Unknown")]
    [InlineData("--all")]
    [InlineData("1.2 --force")]
    [InlineData("1.2\"")]
    [InlineData("1.2\n3")]
    public async Task MissingOrInvalidConfirmedVersionNeverRunsUpgrade(string? version)
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var runner = new Runner();
        var item = Item() with { AvailableVersion = version! };
        var service = new WingetUpdateExecutionService(new WingetAvailabilityService(runner, ["winget"]), runner, authority);
        var result = await service.ExecuteAsync(item, Issue(authority, UpdateActionPreviewFactory.Create(Item())), CancellationToken.None);
        Assert.Equal(UpdateItemStatus.Failed, result.Status);
        Assert.Equal("Mise a jour bloquee : version confirmee manquante ou invalide. Aucune installation effectuee.", result.UserMessage);
        Assert.Equal(0, runner.Upgrades);
        Assert.Empty(runner.UpgradeRequests);
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("driver")]
    [InlineData("firmware")]
    [InlineData("id")]
    public async Task UpdateGuardsPrecedeDetectionAndGate(string scenario)
    {
        var gate = new RejectGate(); var runner = new Runner();
        var item = scenario switch
        {
            "critical" => Item() with { RiskLevel = UpdateRiskLevel.CriticalInformationOnly },
            "driver" => Item() with { Source = UpdateSource.Driver },
            "firmware" => Item() with { Source = UpdateSource.FirmwareInformation },
            _ => Item() with { Id = "" }
        };
        var service = new WingetUpdateExecutionService(new WingetAvailabilityService(runner, ["winget"]), runner, gate);
        var result = await service.ExecuteAsync(item, null, CancellationToken.None);
        Assert.Equal(scenario == "id" ? UpdateItemStatus.Failed : UpdateItemStatus.InformationOnly, result.Status);
        Assert.Equal(0, runner.Calls); Assert.Equal(0, gate.Calls);
    }

    [Fact]
    public void ResourcePreviewExcludesVolatileMeasurementsAndIncludesStartTime()
    {
        var target = Target(); var preview = ProcessActionPreviewFactory.Create(ProcessActionKind.CloseMainWindow, target);
        Assert.Equal(ActionPreviewHasher.Hash(preview), ActionPreviewHasher.Hash(ProcessActionPreviewFactory.Create(
            ProcessActionKind.CloseMainWindow, target with { CpuPercent = 99, WorkingSetBytes = long.MaxValue })));
        Assert.NotEqual(ActionPreviewHasher.Hash(preview), ActionPreviewHasher.Hash(ProcessActionPreviewFactory.Create(
            ProcessActionKind.CloseMainWindow, target with { StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(1) })));
    }

    [Fact]
    public void DestructiveModuleEntryPointsRequirePlanAndExposeNoConfirmationBoolean()
    {
        foreach (var type in new[] { typeof(IProcessActionService), typeof(ProcessActionService), typeof(IUpdateExecutionService), typeof(WingetUpdateExecutionService) })
        {
            var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            Assert.All(methods, method => Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(bool)));
            var executions = methods.Where(m => m.Name == "ExecuteAsync").ToArray();
            Assert.Single(executions);
            Assert.All(executions, method => Assert.Contains(method.GetParameters(), p => p.ParameterType == typeof(ConfirmedPlan)));
        }
    }

    private sealed class Clock : TimeProvider
    {
        private TimeSpan _elapsed;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + _elapsed;
        public override long GetTimestamp() => _elapsed.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan elapsed) => _elapsed += elapsed;
    }
    private sealed class Runtime : IProcessRuntime
    {
        public ProcessResourceInfo Target { get; set; } = ResourceUpdateConfirmedPlanTests.Target();
        public int Effects { get; set; }
        public ProcessRuntimeIdentity? ReadIdentity(int processId) => new(Target.ProcessId, Target.Name, Target.Path, Target.StartedAt, true, false);
        public bool CloseMainWindow(int processId) { Effects++; return true; }
        public void Kill(int processId) => Effects++;
        public Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
        public bool FileExists(string path) => true;
        public Task<bool> OpenLocationAsync(string path, CancellationToken cancellationToken) { Effects++; return Task.FromResult(true); }
    }
    private sealed class Restarter : IExplorerRestarter
    {
        public int Calls { get; private set; }
        public Task<InterventionExecutionResult> RestartAsync(InterventionDiagnostic diagnostic, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new InterventionExecutionResult { Action = diagnostic.Definition, Status = InterventionStatus.Completed }); }
    }
    private sealed class Runner : IProcessRunner
    {
        public int Calls { get; private set; }
        public int Upgrades { get; set; }
        public List<ProcessRunRequest> UpgradeRequests { get; } = new();
        public string Version { get; init; } = "v1.8.0";
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        { Calls++; if (request.Arguments[0] == "upgrade") { Upgrades++; UpgradeRequests.Add(request); } return Task.FromResult(new ProcessRunResult(0, Version, "")); }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
    private sealed class RejectGate : IConfirmedPlanGate
    {
        public int Calls { get; private set; }
        public PlanValidationResult Validate(ConfirmedPlan? plan, ActionPreview preview) => throw new InvalidOperationException();
        public PlanValidationResult TryConsume(ConfirmedPlan? plan, ActionPreview preview) { Calls++; return new(false, PlanRejectionReason.Missing, "Refus"); }
    }
}
