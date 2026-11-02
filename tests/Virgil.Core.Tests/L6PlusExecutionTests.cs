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
        UpdateSequenceStep[] selected = [new("winget-final", "Passe finale", UpdateComponent.WingetFinal)];
        var plan = authority.Issue(service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null));
        if (cancel) cancellation.Cancel();
        var result = await service.ExecuteAsync(selected, plan, null, cancellation.Token);
        Assert.Equal(1, probe.Calls);
        Assert.True(result.RebootRequired);
        Assert.Contains(result.Steps, s => s.Message.Contains("oui (Maintenance des composants)", StringComparison.Ordinal));
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
        public Fixture()
        {
            Directory.CreateDirectory(directory);
            var store = new ElevatedHelperRequestStore(() => directory, new ElevatedProtocolRoot(), new ElevatedProtocolFiles(), () => Guid.NewGuid().ToByteArray(), () => DateTimeOffset.UtcNow);
            var runner = new Runner("1.8.0");
            Service = new(new WindowsUpdateSequenceBackend(runner, store, Launcher, () => "helper", new WingetAvailabilityService(runner, ["winget.exe"])), Authority);
        }
        public void Dispose() => Directory.Delete(directory, true);
    }
    private sealed class Launcher : IElevatedProcessLauncher
    {
        public List<ElevatedInterventionRequest> Requests { get; } = [];
        public async Task<int> RunElevatedAsync(string helper, string requestPath, CancellationToken token)
        {
            var request = JsonSerializer.Deserialize<ElevatedInterventionRequest>(await File.ReadAllTextAsync(requestPath))!;
            Requests.Add(request);
            var result = new ElevatedInterventionResult { ActionId = request.ActionId, Nonce = request.Nonce, StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow, Status = InterventionStatus.Completed, SummaryOutput = "VIRGIL|restore-point|OK|42" };
            await File.WriteAllTextAsync(request.ResultPath, JsonSerializer.Serialize(result));
            return 0;
        }
    }
}
