using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Updates;
using Virgil.ElevatedHelper;
using Virgil.Core.Processes;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class UpdateSequenceTests
{
    [Fact]
    public async Task One_plan_binds_selection_and_is_single_use_before_any_effect()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var backend = new Backend();
        var service = new UpdateSequenceService(backend, authority);
        UpdateSequenceStep[] selection = [new("windows", "Windows", UpdateComponent.Windows), new("defender", "Defender", UpdateComponent.Defender)];
        Assert.True((await service.ExecuteAsync(selection, null, null, CancellationToken.None)).WasRefused);
        Assert.Empty(backend.Effects);
        var plan = authority.Issue(service.CreatePreview(selection), new(ConfirmationLevel.Reinforced, null));
        Assert.True((await service.ExecuteAsync(selection[..1], plan, null, CancellationToken.None)).WasRefused);
        Assert.Empty(backend.Effects);
        Assert.False((await service.ExecuteAsync(selection, plan, null, CancellationToken.None)).WasRefused);
        Assert.Equal(2, backend.Effects.Count);
        Assert.True((await service.ExecuteAsync(selection, plan, null, CancellationToken.None)).WasRefused);
        Assert.Equal(2, backend.Effects.Count);
    }

    [Fact]
    public async Task Cancellation_is_observed_between_steps_and_reboot_is_reported()
    {
        using var cancellation = new CancellationTokenSource();
        var authority = new ConfirmationService(TimeProvider.System);
        var backend = new Backend { OnExecute = cancellation.Cancel };
        var service = new UpdateSequenceService(backend, authority);
        UpdateSequenceStep[] selection = [new("windows", "Windows", UpdateComponent.Windows), new("defender", "Defender", UpdateComponent.Defender)];
        var result = await service.ExecuteAsync(selection, authority.Issue(service.CreatePreview(selection), new(ConfirmationLevel.Reinforced, null)), null, cancellation.Token);
        Assert.True(result.WasCancelled);
        Assert.True(result.RebootRequired);
        Assert.Single(backend.Effects);
    }

    [Fact]
    public async Task Expired_plan_has_no_effect()
    {
        var clock = new Clock();
        var authority = new ConfirmationService(clock);
        var backend = new Backend();
        var service = new UpdateSequenceService(backend, authority);
        UpdateSequenceStep[] selected = [new("windows", "Windows", UpdateComponent.Windows)];
        var plan = authority.Issue(service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null));
        clock.Now += TimeSpan.FromMinutes(11);
        var result = await service.ExecuteAsync(selected, plan, null, CancellationToken.None);
        Assert.True(result.WasRefused);
        Assert.Empty(backend.Effects);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(InterventionId.InstallWindowsUpdates, "", true)]
    [InlineData(InterventionId.UpdateDefenderSignatures, "", true)]
    [InlineData(InterventionId.InstallDriverUpdate, "f813d575-a123-4788-b34a-d547c15c5663", true)]
    [InlineData(InterventionId.InstallDriverUpdate, "' or IsInstalled=1", false)]
    [InlineData(InterventionId.InstallWindowsUpdates, "unexpected", false)]
    [InlineData(InterventionId.FlushDns, "", false)]
    public void Maintenance_allowlist_accepts_only_fixed_actions_and_exact_driver_identity(InterventionId id, string identity, bool expected)
        => Assert.Equal(expected, new ElevatedActionAllowlist().IsMaintenanceAllowed(id, identity));

    [Fact]
    public async Task Failed_restore_point_blocks_WUA_before_any_installation()
    {
        var runner = new RejectRestoreRunner();
        var result = await UpdateMaintenanceRunner.ExecuteAsync(new() { ActionId = InterventionId.InstallWindowsUpdates, RestoreSessionId = "f813d575-a123-4788-b34a-d547c15c5663" }, runner);
        Assert.False(result.Success);
        Assert.Contains("Installation bloquée", result.Message);
        Assert.Single(runner.Commands);
        Assert.Contains("Get-ComputerRestorePoint", runner.Commands[0].Arguments.Last());
    }

    [Fact]
    public async Task Unknown_explicit_target_is_upgraded_with_pinned_version_and_capability_flags()
    {
        var runner = new Runner();
        var backend = new WindowsUpdateSequenceBackend(runner, null!, null!, () => "helper", new WingetAvailabilityService(runner, ["winget.exe"]));
        var app = new UpdateItem { Id = "Ubisoft.Connect", Name = "Ubisoft", InstalledVersion = "< 174.2", AvailableVersion = "174.2", IsInstalledVersionUnknown = true, RequiresExplicitTarget = true };
        Assert.True((await backend.ExecuteAsync(new(app.Id, app.Name, UpdateComponent.Winget, app), CancellationToken.None)).Succeeded);
        var upgrade = Assert.Single(runner.Requests, r => r.Arguments[0] == "upgrade");
        Assert.Contains("--include-unknown", upgrade.Arguments);
        Assert.Contains("--include-pinned", upgrade.Arguments);
        Assert.Single(upgrade.Arguments, argument => argument == "--include-unknown");
        Assert.Single(upgrade.Arguments, argument => argument == "--include-pinned");
        Assert.Contains("--exact", upgrade.Arguments);
        Assert.Contains("174.2", upgrade.Arguments);
        Assert.DoesNotContain("--all", upgrade.Arguments);
    }

    [Theory]
    [InlineData("Bad…", "1.2")]
    [InlineData("--all", "1.2")]
    [InlineData("Safe.Id", "--all")]
    public async Task Invalid_application_target_has_no_effect(string id, string version)
    {
        var runner = new Runner();
        var backend = new WindowsUpdateSequenceBackend(runner, null!, null!, () => "helper");
        var app = new UpdateItem { Id = id, AvailableVersion = version };
        Assert.False((await backend.ExecuteAsync(new(id, "Test", UpdateComponent.Winget, app), CancellationToken.None)).Succeeded);
        Assert.Empty(runner.Requests);
    }
    private sealed class Runner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        { Requests.Add(request); return Task.FromResult(new ProcessRunResult(0, "v1.8.0", "")); }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class Backend : IUpdateSequenceBackend
    {
        public List<UpdateSequenceStep> Effects { get; } = [];
        public Action? OnExecute { get; init; }
        public Task<IReadOnlyList<UpdateSequenceStep>> ScanDriversAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UpdateSequenceStep>>([]);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken cancellationToken)
        { Effects.Add(step); OnExecute?.Invoke(); return Task.FromResult(new UpdateSequenceStepResult(step, true, true, "Terminé")); }
    }
    private sealed class RejectRestoreRunner : IElevatedCommandRunner
    {
        public List<ElevatedCommandSpec> Commands { get; } = [];
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command)
        { Commands.Add(command); return Task.FromResult(new ElevatedCommandResult(1, "", "")); }
    }
}
