using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Interventions;
using Virgil.Core.Resources;
using Xunit;

namespace Virgil.Tests;

public sealed class LotBureauExplorerTests
{
    [Fact]
    public void T3_All_known_findings_never_propose_explorer_restart()
    {
        var output = string.Join('\n', new[] { "SFC", "DISM", "Audiosrv", "AudioEndpointBuilder", "PrintQueue", "Spooler", "DNS", "CHKDSK", "Services", "Gateway", "Reboot" }
            .Select(key => $"VIRGIL|{key}|FINDING|Constat"));
        var repairs = InterventionSafety.RepairsFor(output + "\nfound integrity violations\ncomponent store is repairable");
        Assert.DoesNotContain(InterventionId.RestartExplorer, repairs);
        Assert.Equal(new[] { InterventionId.SfcScan, InterventionId.DismRestoreHealth, InterventionId.RestartAudio,
            InterventionId.ResetPrintQueue, InterventionId.FlushDns }.Order(), repairs.Order());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("other-action")]
    public async Task T4_Intervention_restart_refuses_invalid_plan_before_every_adapter(string scenario)
    {
        var clock = new Clock();
        var authority = new ConfirmationService(clock);
        var diagnostic = new InterventionDiagnostic
        {
            Definition = new InterventionCatalog().Get(InterventionId.RestartExplorer), IsAvailable = true
        };
        var preview = InterventionActionPreviewFactory.Create(new InterventionCatalog().Get(
            scenario == "other-action" ? InterventionId.FlushDns : InterventionId.RestartExplorer));
        var plan = Plan(scenario, authority, clock, preview);
        var adapters = new Adapters();
        var result = await new InterventionExecutionService(adapters, adapters, authority)
            .ExecuteAsync(diagnostic, plan, CancellationToken.None);
        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.StartsWith("Intervention annulée.\n", result.ReadableError);
        Assert.Equal(0, adapters.Restarts);
        Assert.Equal(0, adapters.HelperCalls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("other-action")]
    public async Task T4_Resource_restart_refuses_invalid_plan_before_every_adapter(string scenario)
    {
        var clock = new Clock();
        var authority = new ConfirmationService(clock);
        var preview = scenario == "other-action"
            ? InterventionActionPreviewFactory.Create(new InterventionCatalog().Get(InterventionId.FlushDns))
            : ProcessActionPreviewFactory.Create(ProcessActionKind.RestartExplorer, null);
        var plan = Plan(scenario, authority, clock, preview);
        var adapters = new Adapters();
        var service = new ProcessActionService(adapters,
            new ProcessProtectionPolicy(Path.Combine(Path.GetTempPath(), "virgil-policy", "System32"), -1),
            adapters, new InterventionCatalog(), clock.GetUtcNow, authority);
        var result = await service.ExecuteAsync(ProcessActionKind.RestartExplorer, null, plan, CancellationToken.None);
        Assert.Equal(ProcessActionStatus.Failed, result.Status);
        Assert.StartsWith("Action annulée.\n", result.ReadableError);
        Assert.Equal(0, adapters.Restarts);
        Assert.Equal(0, adapters.HelperCalls);
        Assert.Equal(0, adapters.RuntimeCalls);
    }

    private static ConfirmedPlan? Plan(string scenario, ConfirmationService authority, Clock clock, ActionPreview preview)
    {
        if (scenario == "missing") return null;
        var plan = authority.Issue(preview, new(RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel), null));
        if (scenario == "expired") clock.Advance(TimeSpan.FromMinutes(16));
        return plan;
    }

    private sealed class Clock : TimeProvider
    {
        private TimeSpan _elapsed;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + _elapsed;
        public override long GetTimestamp() => _elapsed.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan value) => _elapsed += value;
    }

    private sealed class Adapters : IExplorerRestarter, IInterventionElevatedHelperClient, IProcessRuntime
    {
        public int Restarts { get; private set; }
        public int HelperCalls { get; private set; }
        public int RuntimeCalls { get; private set; }
        public Task<InterventionExecutionResult> RestartAsync(InterventionDiagnostic diagnostic, CancellationToken cancellationToken)
        { Restarts++; throw new InvalidOperationException(); }
        public Task<ElevatedInterventionResult> ExecuteAsync(InterventionDefinition definition, CancellationToken cancellationToken)
        { HelperCalls++; throw new InvalidOperationException(); }
        private T Unexpected<T>() { RuntimeCalls++; throw new InvalidOperationException(); }
        public ProcessRuntimeIdentity? ReadIdentity(int processId) => Unexpected<ProcessRuntimeIdentity>();
        public bool CloseMainWindow(int processId) => Unexpected<bool>();
        public Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken) => Unexpected<Task<bool>>();
        public void Kill(int processId) => Unexpected<bool>();
        public bool FileExists(string path) => Unexpected<bool>();
        public Task<bool> OpenLocationAsync(string path, CancellationToken cancellationToken) => Unexpected<Task<bool>>();
    }
}
