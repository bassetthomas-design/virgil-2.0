using System.Diagnostics;
using Virgil.Contracts;
using Virgil.Core.Interventions;
using Virgil.Core.Processes;
using Virgil.Core.Resources;
using Xunit;

namespace Virgil.Tests;

public sealed class ProcessLaunchMigrationTests
{
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, null, true)]
    [InlineData(false, "Erreur de lancement", false)]
    public async Task OpenLocation_ForwardsRequestAndMapsOnlyLaunchError(bool started, string? error, bool expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            var runner = new FakeProcessRunner { Result = new(started, null, error) };
            Assert.Equal(expected, await new ProcessRuntime(runner).OpenLocationAsync(path, CancellationToken.None));
            var request = Assert.Single(runner.Requests);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), request.FileName);
            Assert.Equal(new[] { "/select,", path }, request.Arguments);
            Assert.False(request.UseShellExecute);
            Assert.Null(request.RawArguments);
            Assert.False(request.WaitForExit);
            Assert.Equal(CancellationToken.None, Assert.Single(runner.Tokens));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenLocation_MissingFileDoesNotLaunch()
    {
        var runner = new FakeProcessRunner();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.False(await new ProcessRuntime(runner).OpenLocationAsync(path, CancellationToken.None));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task OpenLocation_RunnerExceptionReturnsFalse()
    {
        var path = Path.GetTempFileName();
        try
        {
            var runner = new FakeProcessRunner { Exception = new InvalidOperationException("test") };
            Assert.False(await new ProcessRuntime(runner).OpenLocationAsync(path, CancellationToken.None));
            Assert.Single(runner.Requests);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenLocation_CancellationDoesNotLaunch()
    {
        var runner = new FakeProcessRunner();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProcessRuntime(runner).OpenLocationAsync("unused", source.Token));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task OpenLocation_ForwardsCancellationAndAwaitsAsynchronousRunner()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var source = new CancellationTokenSource();
            var completion = new TaskCompletionSource<ProcessLaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var runner = new FakeProcessRunner { Pending = completion.Task };
            var operation = new ProcessRuntime(runner).OpenLocationAsync(path, source.Token);
            Assert.False(operation.IsCompleted);
            Assert.Equal(source.Token, Assert.Single(runner.Tokens));
            completion.SetResult(new(true, null));
            Assert.True(await operation);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explorer_ForwardsRequestAndPreservesNoErrorResult(bool started)
    {
        var runner = new FakeProcessRunner { Result = new(started, null) };
        using var source = new CancellationTokenSource();
        var result = await Restarter(runner).RestartAsync(Diagnostic(), source.Token);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("explorer.exe", request.FileName);
        Assert.Empty(request.Arguments);
        Assert.False(request.UseShellExecute);
        Assert.Null(request.RawArguments);
        Assert.False(request.WaitForExit);
        Assert.Equal(source.Token, Assert.Single(runner.Tokens));
        // The fake provider still sees no Explorer after launch, as before migration.
        Assert.Equal(InterventionStatus.PartialFailure, result.Status);
        Assert.Equal("Explorer n'a pas ete detecte apres relance.", result.ReadableError);
    }

    [Fact]
    public async Task Explorer_LaunchErrorMapsToExistingFailure()
    {
        var runner = new FakeProcessRunner { Result = new(false, null, "test") };
        var result = await Restarter(runner).RestartAsync(Diagnostic(), CancellationToken.None);
        Assert.Single(runner.Requests);
        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.Equal("Relance Explorer impossible.", result.ReadableError);
    }

    [Fact]
    public async Task Explorer_PreCancelledDoesNotLaunch()
    {
        var runner = new FakeProcessRunner();
        using var source = new CancellationTokenSource();
        source.Cancel();
        var result = await Restarter(runner).RestartAsync(Diagnostic(), source.Token);
        Assert.Equal(InterventionStatus.Cancelled, result.Status);
        Assert.Empty(runner.Requests);
    }

    private static ExplorerRestarter Restarter(IProcessRunner runner) => new(_ => Array.Empty<Process>(), runner);

    private static InterventionDiagnostic Diagnostic() => new()
    {
        Definition = new InterventionCatalog().Get(InterventionId.RestartExplorer),
        IsAvailable = true,
        Status = InterventionStatus.Available,
        StateBefore = "Etat test"
    };

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessLaunchRequest> Requests { get; } = new();
        public List<CancellationToken> Tokens { get; } = new();
        public ProcessLaunchResult Result { get; init; } = new(true, null);
        public Exception? Exception { get; init; }
        public Task<ProcessLaunchResult>? Pending { get; init; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Tokens.Add(cancellationToken);
            return Pending ?? (Exception is null ? Task.FromResult(Result) : Task.FromException<ProcessLaunchResult>(Exception));
        }
    }
}
