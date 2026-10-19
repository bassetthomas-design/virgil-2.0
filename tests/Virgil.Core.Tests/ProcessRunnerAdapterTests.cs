using Virgil.Infrastructure.Windows;
using Virgil.Core.Applications;
using Virgil.Core.Interventions;
using Virgil.Core.Processes;
using Virgil.Contracts;
using Xunit;

namespace Virgil.Tests;

public sealed class ProcessRunnerAdapterTests
{
    [Theory]
    [InlineData(true, 7)]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public async Task Shell_ForwardsRequestAndMapsResult(bool started, int? exitCode)
    {
        var runner = new FakeProcessRunner { Result = new(started, exitCode) };
        using var source = new CancellationTokenSource();
        var result = await new ShellApplicationProcessLauncher(runner).LaunchAsync(
            "uninstall.exe", new[] { "/first", "two words" }, false, source.Token);

        Assert.Equal(started, result.Started);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Null(result.ReadableError);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("uninstall.exe", request.FileName);
        Assert.Equal(new[] { "/first", "two words" }, request.Arguments);
        Assert.False(request.UseShellExecute);
        Assert.Null(request.Verb);
        Assert.Equal(ProcessWindowMode.Normal, request.WindowMode);
        Assert.Null(request.RawArguments);
        Assert.False(request.WaitForExit);
        Assert.Equal(source.Token, runner.Token);
    }

    [Fact]
    public async Task Shell_ForwardsShellExecution()
    {
        var runner = new FakeProcessRunner();
        await new ShellApplicationProcessLauncher(runner).LaunchAsync("uninstall.exe", Array.Empty<string>(), true, CancellationToken.None);
        Assert.True(Assert.Single(runner.Requests).UseShellExecute);
    }

    [Fact]
    public async Task Shell_MapsLaunchErrorToExistingMessage()
    {
        var runner = new FakeProcessRunner { Result = new(false, null, "detail") };
        var result = await new ShellApplicationProcessLauncher(runner).LaunchAsync("uninstall.exe", Array.Empty<string>(), true, CancellationToken.None);
        Assert.False(result.Started);
        Assert.Null(result.ExitCode);
        Assert.Equal("Desinstalleur officiel impossible a lancer.", result.ReadableError);
    }

    [Fact]
    public async Task Shell_CancelledTokenDoesNotCallRunner()
    {
        var runner = new FakeProcessRunner();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ShellApplicationProcessLauncher(runner)
            .LaunchAsync("uninstall.exe", Array.Empty<string>(), true, source.Token));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Elevated_ForwardsExactOptionsAndReturnsExitCode()
    {
        var runner = new FakeProcessRunner { Result = new(true, 5) };
        using var source = new CancellationTokenSource();
        var exitCode = await new ElevatedProcessLauncher(runner).RunElevatedAsync(
            "helper.exe", "C:\\some folder\\a\"b.json", source.Token);
        Assert.Equal(5, exitCode);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("helper.exe", request.FileName);
        Assert.Empty(request.Arguments);
        Assert.True(request.UseShellExecute);
        Assert.Equal("runas", request.Verb);
        Assert.Equal(ProcessWindowMode.Hidden, request.WindowMode);
        Assert.True(request.WaitForExit);
        Assert.Equal("\"C:\\some folder\\ab.json\"", request.RawArguments);
        Assert.Equal(source.Token, runner.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Elevated_MissingProcessOrExitCodeReturnsMinusOne(bool started)
    {
        var runner = new FakeProcessRunner { Result = new(started, null) };
        Assert.Equal(-1, await new ElevatedProcessLauncher(runner).RunElevatedAsync("helper.exe", "request.json", CancellationToken.None));
    }

    [Fact]
    public async Task Elevated_LaunchErrorThrows()
    {
        var runner = new FakeProcessRunner { Result = new(false, null, "UAC refused") };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new ElevatedProcessLauncher(runner)
            .RunElevatedAsync("helper.exe", "request.json", CancellationToken.None));
        Assert.Equal("UAC refused", error.Message);
    }

    [Fact]
    public async Task Elevated_CancelledTokenDoesNotCallRunner()
    {
        var runner = new FakeProcessRunner();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ElevatedProcessLauncher(runner)
            .RunElevatedAsync("helper.exe", "request.json", source.Token));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task HelperClient_MapsAdapterFailureToExistingMessage()
    {
        // The real request store secures its temporary directory using Windows ACLs.
        var directory = Path.Combine(Path.GetTempPath(), "VirgilAdapterTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var helperPath = Path.Combine(directory, "helper.exe");
            await File.WriteAllTextAsync(helperPath, string.Empty);
            var runner = new FakeProcessRunner { Result = new(false, null, "UAC refused") };
            var store = new ElevatedHelperRequestStore(() => directory, new ElevatedProtocolRoot(), new ElevatedProtocolFiles(),
                () => System.Security.Cryptography.RandomNumberGenerator.GetBytes(16), () => DateTimeOffset.UtcNow);
            var client = new ElevatedHelperClient(store, new ElevatedProcessLauncher(runner), () => helperPath);
            var result = await client.ExecuteAsync(new InterventionCatalog().Get(InterventionId.FlushDns), CancellationToken.None);
            Assert.Single(runner.Requests);
            Assert.Equal(InterventionStatus.Failed, result.Status);
            Assert.Equal("Assistant eleve indisponible ou refuse.", result.ReadableError);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessLaunchRequest> Requests { get; } = new();
        public ProcessLaunchResult Result { get; init; } = new(true, null);
        public CancellationToken Token { get; private set; }

        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Token = cancellationToken;
            return Task.FromResult(Result);
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
