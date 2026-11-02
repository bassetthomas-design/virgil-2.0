using System.Text.Json;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Interventions;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class L6PlusExecutionTests
{
    [Theory, Trait("Category", "Safety")]
    [InlineData("1.4.0", false)]
    [InlineData("1.5.0", true)]
    public async Task Final_winget_pass_uses_exact_script_command(string version, bool pinned)
    {
        var runner = new Runner(version);
        var backend = new WindowsUpdateSequenceBackend(runner, null!, null!, () => "helper", new WingetAvailabilityService(runner, ["winget.exe"]));
        var result = await backend.ExecuteAsync(new("winget-final", "Passe finale", UpdateComponent.WingetFinal), default);
        var expected = "upgrade --all --include-unknown " + (pinned ? "--include-pinned " : "") + "--force --accept-source-agreements --accept-package-agreements --silent --disable-interactivity";
        Assert.Equal(expected, string.Join(" ", Assert.Single(runner.Requests, r => r.Arguments[0] == "upgrade").Arguments));
        Assert.True(result.Succeeded);
        if (!pinned) Assert.Contains("--include-pinned retirée", result.Message);
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Contains("--ignore-security-hash"));
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Winget_source_update_runs_before_first_and_final_pass()
    {
        var runner = new Runner("1.8.0");
        var backend = new WindowsUpdateSequenceBackend(runner, null!, null!, () => "helper", new WingetAvailabilityService(runner, ["winget.exe"]));
        var app = new UpdateItem { Id = "Safe.Package", AvailableVersion = "1.2" };
        await backend.ExecuteAsync(new(app.Id, "Paquet", UpdateComponent.Winget, app), default);
        await backend.ExecuteAsync(new(app.Id, "Paquet", UpdateComponent.Winget, app), default);
        await backend.ExecuteAsync(new("winget-final", "Passe finale", UpdateComponent.WingetFinal), default);
        Assert.Equal(new[] { "source", "upgrade", "upgrade", "source", "upgrade" }, runner.Requests.Where(r => r.Arguments[0] != "--version").Select(r => r.Arguments[0]));
    }

    [Theory, Trait("Category", "Safety"), Trait("Category", "Windows")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_sequence_never_requests_more_than_two_elevations(bool wingetOnly)
    {
        using var fixture = new Fixture();
        var selected = wingetOnly ? new[] { new UpdateSequenceStep("winget-final", "Passe finale", UpdateComponent.WingetFinal) } : (await fixture.Service.ScanAsync([], default)).ToArray();
        var plan = fixture.Authority.Issue(fixture.Service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null));
        var result = await fixture.Service.ExecuteAsync(selected, plan, null, default);
        Assert.False(result.HasFailures);
        Assert.Equal(wingetOnly ? Array.Empty<InterventionId>() : new[] { InterventionId.PrepareMicrosoftUpdates, InterventionId.InstallMicrosoftUpdates }, fixture.Launcher.Requests.Select(r => r.ActionId));
        if (!wingetOnly)
        {
            Assert.Equal(42, fixture.Launcher.Requests[1].RestorePointSequenceNumber);
            Assert.Equal(fixture.Launcher.Requests[0].RestoreSessionId, fixture.Launcher.Requests[1].RestoreSessionId);
            Assert.Equal("software,drivers,store,windows-feature-upgrade", fixture.Launcher.Requests[1].UpdateIdentity);
        }
    }

    [Theory, Trait("Category", "Safety"), Trait("Category", "Windows")]
    [InlineData("drivers")]
    [InlineData("windows-feature-upgrade")]
    public async Task Excluding_category_changes_plan_hash_and_removes_helper_token(string excluded)
    {
        using var f = new Fixture();
        var all = await f.Service.ScanAsync([], default);
        var selected = all.Where(s => s.Id != excluded).ToArray();
        var original = f.Authority.Issue(f.Service.CreatePreview(all), new(ConfirmationLevel.Reinforced, null));
        Assert.True((await f.Service.ExecuteAsync(selected, original, null, default)).WasRefused);
        Assert.Empty(f.Launcher.Requests);
        var plan = f.Authority.Issue(f.Service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null));
        await f.Service.ExecuteAsync(selected, plan, null, default);
        Assert.DoesNotContain(excluded, f.Launcher.Requests.Last().UpdateIdentity.Split(','));
    }

    [Fact, Trait("Category", "Safety")]
    public Task Pending_reboot_is_read_after_sequence_even_when_steps_report_none() => VerifyReboot(false);
    [Fact, Trait("Category", "Safety")]
    public Task Pending_reboot_is_read_after_cancellation() => VerifyReboot(true);
    private static async Task VerifyReboot(bool cancel)
    {
        var probe = new Probe();
        var authority = new ConfirmationService(TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        var service = new UpdateSequenceService(new Backend(), authority, probe);
        UpdateSequenceStep[] selected = [new("defender", "Signatures Microsoft Defender", UpdateComponent.Defender), new("winget-final", "Passe finale", UpdateComponent.WingetFinal)];
        var plan = authority.Issue(service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null));
        if (cancel) cancellation.Cancel();
        var result = await service.ExecuteAsync(selected, plan, null, cancellation.Token);
        Assert.Equal(1, probe.Calls);
        if (cancel) Assert.All(result.Steps.Where(s => s.Step.Component != UpdateComponent.PendingReboot), s => Assert.Contains("Non lancée", s.Message));
        Assert.True(result.RebootRequired);
        Assert.Contains(result.Steps, s => s.Message.Contains("oui (Maintenance des composants)", StringComparison.Ordinal));
    }
    [Fact, Trait("Category", "Safety")]
    public async Task Preparation_launch_failure_is_not_reported_as_success_and_still_probes_reboot()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var probe = new Probe();
        var service = new UpdateSequenceService(new PreparationFailure(), authority, probe);
        UpdateSequenceStep[] selected = [new("defender", "Signatures Microsoft Defender", UpdateComponent.Defender)];
        var plan = authority.Issue(service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null));
        var result = await service.ExecuteAsync(selected, plan, null, default);
        Assert.True(result.HasFailures);
        Assert.Equal(0, result.SuccessfulCount);
        Assert.Contains(result.Steps, s => s.Step.Component == UpdateComponent.Defender && !s.Succeeded);
        Assert.Equal(1, probe.Calls);
    }
    [Fact, Trait("Category", "Safety"), Trait("Category", "Windows")]
    public async Task Defender_failure_does_not_block_windows_update_or_winget()
    {
        using var f = new Fixture();
        f.Launcher.FailDefender = true;
        var result = await RunReviewedSequence(f);
        Assert.False(Assert.Single(result.Steps, s => s.Step.Component == UpdateComponent.Defender).Succeeded);
        Assert.True(Assert.Single(result.Steps, s => s.Step.Component == UpdateComponent.MicrosoftUpdate).Succeeded);
        Assert.True(Assert.Single(result.Steps, s => s.Step.Component == UpdateComponent.Windows).Succeeded);
        Assert.Equal(new[] { InterventionId.PrepareMicrosoftUpdates, InterventionId.InstallMicrosoftUpdates }, f.Launcher.Requests.Select(r => r.ActionId));
        Assert.Equal(2, f.Runner.Requests.Count(r => r.Arguments[0] == "upgrade"));
        Assert.Equal(1, f.Shutdown.Reads);
    }

    [Theory, Trait("Category", "Safety"), Trait("Category", "Windows")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refused_UAC_or_failed_restore_blocks_only_microsoft_steps(bool refused)
    {
        using var f = new Fixture();
        f.Launcher.RefusePrepare = refused;
        f.Launcher.FailRestore = !refused;
        var result = await RunReviewedSequence(f);
        Assert.Single(f.Launcher.Requests);
        Assert.DoesNotContain(f.Launcher.Requests, r => r.ActionId == InterventionId.InstallMicrosoftUpdates);
        Assert.False(Assert.Single(result.Steps, s => s.Step.Component == UpdateComponent.Windows).Succeeded);
        Assert.True(Assert.Single(result.Steps, s => s.Step.Component == UpdateComponent.Winget).Succeeded);
        Assert.True(Assert.Single(result.Steps, s => s.Step.Component == UpdateComponent.WingetFinal).Succeeded);
        Assert.Equal(2, f.Runner.Requests.Count(r => r.Arguments[0] == "upgrade"));
        Assert.Equal(1, f.Shutdown.Reads);
    }

    [Fact, Trait("Category", "Safety"), Trait("Category", "Windows")]
    public async Task Sequence_report_has_separate_step_results_without_raw_helper_markers()
    {
        using var f = new Fixture();
        f.Launcher.FailDefender = true;
        var result = await RunReviewedSequence(f);
        var report = string.Join("\n", result.Steps.Select(s => s.Step.Title + " : " + s.Message));
        Assert.DoesNotContain("VIRGIL|", report);
        var defender = Assert.Single(result.Steps, s => s.Step.Component == UpdateComponent.Defender);
        var windows = Assert.Single(result.Steps, s => s.Step.Component == UpdateComponent.Windows);
        Assert.Contains("Defender", defender.Message);
        Assert.DoesNotContain("Windows logiciel", defender.Message);
        Assert.Contains("Windows logiciel", windows.Message);
        Assert.DoesNotContain("Defender", windows.Message);
        Assert.DoesNotContain("42", windows.Message);
        Assert.Contains("point existant du", Assert.Single(result.Steps, s => s.Step.Component == UpdateComponent.Restore).Message);
    }
    private static Task<UpdateSequenceResult> RunReviewedSequence(Fixture f)
    {
        UpdateSequenceStep[] selected = [
            new("defender", "Signatures Microsoft Defender", UpdateComponent.Defender),
            new("microsoft-update", "Microsoft Update", UpdateComponent.MicrosoftUpdate),
            new("launchers", "Virgil fermera : Steam", UpdateComponent.Launcher),
            new("Safe.Package", "Application", UpdateComponent.Winget, new UpdateItem { Id = "Safe.Package", AvailableVersion = "1.2" }),
            new("software", "Windows Update", UpdateComponent.Windows),
            new("winget-final", "Passe finale", UpdateComponent.WingetFinal)
        ];
        return f.Service.ExecuteAsync(selected, f.Authority.Issue(f.Service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null)), null, default);
    }

    private sealed class PreparationFailure : IUpdateSequenceBackend
    {
        public Task<IReadOnlyList<UpdateSequenceStepResult>> ExecuteGroupAsync(IReadOnlyList<UpdateSequenceStep> steps, CancellationToken token) => throw new InvalidOperationException();
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) => throw new InvalidOperationException();
    }

    private sealed class Probe : IPendingRebootProbe
    {
        public int Calls { get; private set; }
        public PendingRebootStatus Read() { Calls++; return new(true, ["Maintenance des composants"]); }
    }
    private sealed class Backend : IUpdateSequenceBackend
    {
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) => Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Terminée"));
    }
    private sealed class Runner(string version) : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token)
        { Requests.Add(request); return Task.FromResult(new ProcessRunResult(0, version, "")); }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token) => throw new InvalidOperationException();
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "Virgil-L6b-" + Guid.NewGuid().ToString("N"));
        public ConfirmationService Authority { get; } = new(TimeProvider.System);
        public Launcher Launcher { get; } = new();
        public UpdateSequenceService Service { get; }
        public Runner Runner { get; } = new("1.8.0");
        public Shutdown Shutdown { get; } = new();
        public Fixture()
        {
            Directory.CreateDirectory(directory);
            var store = new ElevatedHelperRequestStore(() => directory, new ElevatedProtocolRoot(), new ElevatedProtocolFiles(), () => Guid.NewGuid().ToByteArray(), () => DateTimeOffset.UtcNow);
            Service = new(new WindowsUpdateSequenceBackend(Runner, store, Launcher, () => "helper", new WingetAvailabilityService(Runner, ["winget.exe"])), Authority, launcherShutdown: new(Shutdown, TimeProvider.System));
        }
        public void Dispose() => Directory.Delete(directory, true);
    }
    private sealed class Launcher : IElevatedProcessLauncher
    {
        public List<ElevatedInterventionRequest> Requests { get; } = [];
        public bool FailDefender { get; set; }
        public bool RefusePrepare { get; set; }
        public bool FailRestore { get; set; }
        public async Task<int> RunElevatedAsync(string helper, string requestPath, CancellationToken token)
        {
            var request = JsonSerializer.Deserialize<ElevatedInterventionRequest>(await File.ReadAllTextAsync(requestPath))!;
            Requests.Add(request);
            var prepare = request.ActionId == InterventionId.PrepareMicrosoftUpdates;
            if (prepare && RefusePrepare) throw new InvalidOperationException("Élévation refusée.");
            var lines = new List<string>();
            if (prepare)
            {
                if (!FailRestore)
                {
                    lines.Add("VIRGIL|restore-point|OK|42");
                    lines.Add("VIRGIL|restore-point|INFO|point existant du 01/11/2026 (Point vérifié) réutilisé");
                }
            }
            foreach (var tokenName in request.UpdateIdentity.Split(',', StringSplitOptions.RemoveEmptyEntries))
                lines.Add("VIRGIL|" + tokenName + "|" + (tokenName == "defender" && FailDefender ? "FAILED" : "OK") + "|" +
                    (tokenName == "defender" ? "Résultat Defender." : tokenName == "software" ? "Windows logiciel installé." : "Résultat " + tokenName + "."));
            var failed = prepare && (FailRestore || FailDefender);
            var result = new ElevatedInterventionResult { ActionId = request.ActionId, Nonce = request.Nonce, StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow, Status = failed ? InterventionStatus.Failed : InterventionStatus.Completed, SummaryOutput = FailRestore ? "Point de restauration non vérifié." : string.Join("\n", lines) };
            await File.WriteAllTextAsync(request.ResultPath, JsonSerializer.Serialize(result));
            return 0;
        }
    }
    private sealed class Shutdown : ILauncherShutdownBackend
    {
        public int Reads { get; private set; }
        public Task<LauncherSnapshot> ReadAsync(string name, CancellationToken token) { Reads++; return Task.FromResult(new LauncherSnapshot(true, [], ["C:/Games"])); }
        public Task CloseAsync(string executable, bool force, CancellationToken token) => throw new InvalidOperationException();
    }

}
