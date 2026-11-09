using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class D1UpdateVerificationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_service_verifies_each_selected_source_after_execution_even_after_cancellation(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new Backend(cancel ? cancellation.Cancel : null);
        var gate = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, gate, new Probe());
        UpdateSequenceStep[] selected = [new("defender", "Defender", UpdateComponent.Defender),
            new("software", "Windows", UpdateComponent.Windows), new("drivers", "Pilotes", UpdateComponent.Driver),
            new("store", "Store", UpdateComponent.Store)];
        var result = await service.ExecuteAsync(selected, gate.Issue(service.CreatePreview(selected),
            new(ConfirmationLevel.Reinforced, null)), null, cancellation.Token);
        Assert.Equal(cancel, result.WasCancelled);
        Assert.Equal(selected.Select(step => step.Component), backend.Verified);
        Assert.All(backend.VerificationTokens, token => Assert.False(token.IsCancellationRequested));
        Assert.DoesNotContain(backend.Events.SkipWhile(value => value != "verify"), value => value == "install");
        Assert.Equal(selected.Select(step => step.Component), result.Verifications.Select(check => check.Component));
        Assert.False(Assert.Single(result.Verifications, check => check.Component == UpdateComponent.Store).IsCurrent);
        if (cancel) Assert.Single(backend.Executed);
        else Assert.Equal(selected, backend.Executed);
    }

    [Theory]
    [InlineData(UpdateComponent.Windows)]
    [InlineData(UpdateComponent.Driver)]
    [InlineData(UpdateComponent.Defender)]
    [InlineData(UpdateComponent.MicrosoftUpdate)]
    [InlineData(UpdateComponent.WindowsFeatureUpgrade)]
    public async Task Backend_final_checks_only_read_and_require_an_exact_success_marker(UpdateComponent component)
    {
        var runner = new Runner();
        var backend = new WindowsUpdateSequenceBackend(runner, null!, null!, () => throw new InvalidOperationException());
        foreach (var marker in new[] { "VIRGIL_CURRENT", "VIRGIL_REMAINING", "", "unexpected" })
        {
            runner.Result = new(0, marker, "");
            var check = await backend.VerifyAsync(component, CancellationToken.None);
            Assert.Equal(marker == "VIRGIL_CURRENT", check.IsCurrent);
        }
        runner.Result = new(1, "VIRGIL_CURRENT", "échec");
        Assert.Equal("indisponible", (await backend.VerifyAsync(component, CancellationToken.None)).Status);
        foreach (var result in new[] { new ProcessRunResult(1, "", "VIRGIL_WUA_001"),
            new ProcessRunResult(0, "VIRGIL_CURRENT", "VIRGIL_WUA_001"),
            new ProcessRunResult(0, "VIRGIL_WUA_001", "") })
        {
            runner.Result = result;
            var incomplete = await backend.VerifyAsync(component, CancellationToken.None);
            Assert.Equal("indisponible", incomplete.Status);
            Assert.Contains("incomplète", incomplete.Detail);
        }
        Assert.All(runner.Requests, request =>
        {
            Assert.Equal("powershell.exe", request.FileName);
            var script = request.Arguments.Last();
            foreach (var forbidden in new[] { "Install(", "Download(", "AcceptEula", "AddService", "Update-MpSignature", "Start-Process" })
                Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
        });
        runner.Requests.Clear();
        Assert.False((await backend.VerifyAsync(UpdateComponent.Store, CancellationToken.None)).IsCurrent);
        Assert.Empty(runner.Requests);
    }

    private sealed class Backend(Action? cancel) : IUpdateSequenceBackend
    {
        public List<UpdateSequenceStep> Executed { get; } = [];
        public List<UpdateComponent> Verified { get; } = [];
        public List<CancellationToken> VerificationTokens { get; } = [];
        public List<string> Events { get; } = [];
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token)
        {
            Events.Add("install"); Executed.Add(step); cancel?.Invoke();
            return Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Terminée.") { RestorePointVerified = true });
        }
        public Task<UpdateSourceVerification> VerifyAsync(UpdateComponent component, CancellationToken token)
        {
            Events.Add("verify"); Verified.Add(component); VerificationTokens.Add(token);
            return Task.FromResult(new UpdateSourceVerification(component, "à jour", "Lecture factice vide."));
        }
    }
    private sealed class Probe : IPendingRebootProbe
    {
        public PendingRebootStatus Read() => new(true, []);
    }
    private sealed class Runner : IProcessRunner
    {
        public ProcessRunResult Result { get; set; } = new(0, "", "");
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token)
        { Requests.Add(request); return Task.FromResult(Result); }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token) => throw new InvalidOperationException();
    }
}
