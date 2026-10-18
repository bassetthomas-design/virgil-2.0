using Virgil.Core.Processes;
using Virgil.Infrastructure.Processes;
using Xunit;

namespace Virgil.Tests;

public sealed class ProcessRunnerWindowsTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static string MissingExecutable => Path.Combine(Path.GetTempPath(), "virgil-missing-" + Guid.NewGuid().ToString("N") + ".exe");

    [Fact]
    [Trait("Category", "Windows")]
    public async Task RunAsync_CapturesOutputAndExitCode()
    {
        var result = await new ProcessRunner().RunAsync(new ProcessRunRequest(
            Cmd, new[] { "/c", "echo virgil & echo erreur 1>&2 & exit /b 3" }, TimeSpan.FromSeconds(5)), CancellationToken.None);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("virgil", result.StandardOutput.Trim());
        Assert.Equal("erreur", result.StandardError.Trim());
        Assert.Null(result.LaunchError);
        Assert.False(result.TimedOut);
        Assert.False(result.Cancelled);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task RunAsync_TimeoutIsDistinctFromCancellation()
    {
        var result = await new ProcessRunner().RunAsync(new ProcessRunRequest(
            Cmd, new[] { "/c", "ping -n 10 127.0.0.1 >nul" }, TimeSpan.FromMilliseconds(300)), CancellationToken.None);
        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task RunAsync_CancellationDuringExecution()
    {
        using var source = new CancellationTokenSource();
        var task = new ProcessRunner().RunAsync(new ProcessRunRequest(
            Cmd, new[] { "/c", "ping -n 10 127.0.0.1 >nul" }, TimeSpan.FromSeconds(5)), source.Token);
        source.CancelAfter(TimeSpan.FromMilliseconds(300));
        var result = await task;
        Assert.True(result.Cancelled);
        Assert.False(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task RunAsync_MissingExecutableReturnsLaunchError()
    {
        var result = await new ProcessRunner().RunAsync(new ProcessRunRequest(
            MissingExecutable, Array.Empty<string>(), TimeSpan.FromSeconds(2)), CancellationToken.None);
        Assert.NotNull(result.LaunchError);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task LaunchAsync_MissingExecutableReturnsLaunchError()
    {
        var result = await new ProcessRunner().LaunchAsync(new ProcessLaunchRequest(
            MissingExecutable, Array.Empty<string>(), UseShellExecute: false), CancellationToken.None);
        Assert.False(result.Started);
        Assert.Null(result.ExitCode);
        Assert.NotNull(result.LaunchError);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task LaunchAsync_WaitsForExitAndReturnsCode()
    {
        var result = await new ProcessRunner().LaunchAsync(new ProcessLaunchRequest(
            Cmd, new[] { "/c", "exit /b 5" }, UseShellExecute: false, WaitForExit: true), CancellationToken.None);
        Assert.True(result.Started);
        Assert.Equal(5, result.ExitCode);
        Assert.Null(result.LaunchError);
    }
}
