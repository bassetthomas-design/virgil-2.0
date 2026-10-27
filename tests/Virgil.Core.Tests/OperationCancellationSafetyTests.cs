using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Interventions;
using Virgil.Core.Processes;
using Virgil.Core.Scanning;
using Virgil.Core.Updates;
using Virgil.ElevatedHelper;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class OperationCancellationSafetyTests
{
    [Fact]
    [Trait("Category", "Safety")]
    [Trait("Category", "Windows")]
    public async Task CancelObservedAtProgressBoundaryPreventsTheProtectedCommandFromStarting()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new ImmediateCommands(() => { });
        var notifications = 0;
        var progress = new BoundaryProgress(() => { if (++notifications == 3) cancellation.Cancel(); });
        var result = await Dispatcher(runner).ExecuteValidatedAsync(new() { ActionId = InterventionId.VerifyHealthDeep }, progress, cancellation.Token);
        Assert.Equal(2, runner.Calls);
        Assert.Equal(InterventionStatus.Cancelled, result.Status);
        Assert.Contains("non lancées", result.SummaryOutput);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public async Task CancelAfterRestorePointPreventsWindowsUpdateSearchFromStarting()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new ImmediateCommands(cancellation.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateMaintenanceRunner.ExecuteAsync(new()
        {
            ActionId = InterventionId.InstallWindowsUpdates, RestoreSessionId = Guid.NewGuid().ToString("D")
        }, runner, cancellation.Token));
        Assert.Equal(1, runner.Calls);
        Assert.True(runner.Last!.NonInterruptible);
    }

    private sealed class BoundaryProgress(Action report) : IProgress<InterventionProgress>
    { public void Report(InterventionProgress value) => report(); }
    private sealed class ImmediateCommands(Action after) : IElevatedCommandRunner
    {
        public int Calls { get; private set; }
        public ElevatedCommandSpec? Last { get; private set; }
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command)
        { Calls++; Last = command; after(); return Task.FromResult(new ElevatedCommandResult(0, "", "")); }
    }

    [Theory]
    [Trait("Category", "Safety")]
    [InlineData(InterventionId.SfcScan)]
    [InlineData(InterventionId.DismRestoreHealth)]
    [InlineData(InterventionId.DismScanHealth)]
    [InlineData(InterventionId.ResetWinsock)]
    [InlineData(InterventionId.ResetTcpIp)]
    [InlineData(InterventionId.CreateRestorePoint)]
    public async Task ElevatedAtomicCommandFinishesAfterCancellationWithoutReceivingKillToken(InterventionId action)
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new Commands();
        var dispatcher = Dispatcher(runner);
        var task = dispatcher.ExecuteValidatedAsync(new() { ActionId = action }, null, cancellation.Token);
        await runner.Started.Task;
        cancellation.Cancel();
        Assert.True(runner.Last!.NonInterruptible);
        Assert.False(runner.Token.CanBeCanceled);
        Assert.False(task.IsCompleted);
        runner.Finish.SetResult(new(0, "Terminé", ""));
        var result = await task;
        Assert.Contains("terminée après annulation", result.SummaryOutput);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public async Task RenewIpAlwaysRenewsWhenCancelledAfterRelease()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new RenewCommands(cancellation);
        var result = await Dispatcher(runner).ExecuteValidatedAsync(new() { ActionId = InterventionId.RenewIp }, null, cancellation.Token);
        Assert.Equal(new[] { "/release", "/renew" }, runner.Arguments);
        Assert.Contains("terminée après annulation", result.SummaryOutput);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public async Task WingetCancellationKeepsInstallationAliveAndSequenceNeverLaunchesStepsTwoAndThree()
    {
        using var cancellation = new CancellationTokenSource();
        var authority = new ConfirmationService(TimeProvider.System);
        var runner = new WingetRunner();
        var executor = new WingetUpdateExecutionService(new WingetAvailabilityService(runner, ["winget.exe"]), runner, authority);
        var backend = new WingetBackend(executor, authority);
        var service = new UpdateSequenceService(backend, authority);
        var selection = Enumerable.Range(1, 3).Select(i => new UpdateSequenceStep("app" + i, "App " + i, UpdateComponent.Winget,
            new UpdateItem { Id = "app" + i, Name = "App " + i, AvailableVersion = "2.0" })).ToArray();
        var plan = authority.Issue(service.CreatePreview(selection), new(ConfirmationLevel.Reinforced, null));
        var task = service.ExecuteAsync(selection, plan, null, cancellation.Token);
        await runner.Started.Task;
        cancellation.Cancel();
        Assert.False(runner.InstallToken.CanBeCanceled);
        Assert.False(task.IsCompleted);
        runner.Finish.SetResult(new ProcessRunResult(0, "", ""));
        var result = await task;
        Assert.True(result.WasCancelled);
        Assert.Equal(1, runner.InstallCalls);
        Assert.Contains("terminée après annulation", result.Steps[0].Message);
        Assert.All(result.Steps.Skip(1), step => Assert.Contains("Non lancée", step.Message));
    }

    [Theory]
    [Trait("Category", "Safety")]
    [InlineData(ScanMode.Quick)]
    [InlineData(ScanMode.Deep)]
    public async Task CancelledScanNeverStartsReadersOrProducesAReport(ScanMode mode)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        // Any reader access would fail: every dependency is deliberately absent.
        var scan = new SystemScanService(null!, null!, null!, null!, null!);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan.RunAsync(mode, null, cancellation.Token));
    }

    [Fact]
    [Trait("Category", "Safety")]
    [Trait("Category", "Windows")]
    public async Task SequenceWritesCancelBeforeHelperExitAndCleansUpOnlyAfterwards()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Virgil-L5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ElevatedHelperRequestStore(() => directory, new ElevatedProtocolRoot(), new ElevatedProtocolFiles(),
                () => new byte[16], () => DateTimeOffset.UtcNow);
            var launcher = new HelperLauncher();
            var backend = new WindowsUpdateSequenceBackend(null!, store, launcher, () => "helper.exe");
            using var cancellation = new CancellationTokenSource();
            var task = backend.ExecuteAsync(new("windows", "Windows", UpdateComponent.Windows), cancellation.Token);
            var request = await launcher.Started.Task;
            cancellation.Cancel();
            var cancel = request.Replace(".request.json", ".cancel", StringComparison.Ordinal);
            Assert.True(File.Exists(cancel));
            Assert.True(File.Exists(request));
            Assert.False(task.IsCompleted);
            Assert.False(launcher.Token.CanBeCanceled);
            launcher.Finish.SetResult(0);
            await task;
            Assert.False(File.Exists(cancel));
            Assert.False(File.Exists(request));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ElevatedActionDispatcher Dispatcher(IElevatedCommandRunner runner) => new(
        new ElevatedRequestValidator(), new ElevatedActionAllowlist(), runner, new Administrator(), () => @"C:\Windows\System32");
    private sealed class Administrator : IElevatedSecurityContext { public bool IsAdministrator => true; }
    private sealed class Commands : IElevatedCommandRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ElevatedCommandResult> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ElevatedCommandSpec? Last { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command) => throw new InvalidOperationException();
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command, IProgress<int?>? progress, CancellationToken token)
        { Last = command; Token = token; Calls++; Started.SetResult(); return Finish.Task; }
    }
    private sealed class RenewCommands(CancellationTokenSource cancellation) : IElevatedCommandRunner
    {
        public List<string> Arguments { get; } = [];
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command) => throw new InvalidOperationException();
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command, IProgress<int?>? progress, CancellationToken token)
        {
            Assert.False(token.CanBeCanceled);
            Arguments.Add(command.Arguments[0]);
            if (Arguments.Count == 1) cancellation.Cancel();
            return Task.FromResult(new ElevatedCommandResult(0, "", ""));
        }
    }
    private sealed class WingetRunner : IProcessRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ProcessRunResult> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken InstallToken { get; private set; }
        public int InstallCalls { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token)
        {
            if (request.Arguments[0] == "--version") return Task.FromResult(new ProcessRunResult(0, "v1.8.0", ""));
            InstallCalls++; InstallToken = token; Started.SetResult(); return Finish.Task;
        }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token) => throw new InvalidOperationException();
    }
    private sealed class WingetBackend(WingetUpdateExecutionService executor, ConfirmationService authority) : IUpdateSequenceBackend
    {
        public Task<IReadOnlyList<UpdateSequenceStep>> ScanDriversAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<UpdateSequenceStep>>([]);
        public async Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token)
        {
            var preview = UpdateActionPreviewFactory.Create(step.Application!);
            var plan = authority.Issue(preview, new(RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel), null));
            var result = await executor.ExecuteAsync(step.Application!, plan, token);
            return new(step, result.Status == UpdateItemStatus.Completed, false, result.UserMessage);
        }
    }
    private sealed class HelperLauncher : IElevatedProcessLauncher
    {
        public TaskCompletionSource<string> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<int> RunElevatedAsync(string helperPath, string requestPath, CancellationToken token)
        { Token = token; Started.SetResult(requestPath); return Finish.Task; }
    }
}
