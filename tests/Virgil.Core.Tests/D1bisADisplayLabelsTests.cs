using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class D1bisADisplayLabelsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backend_formats_its_own_labels_without_rewriting_application_names(bool sourceFails)
    {
        var runner = new Runner(sourceFails);
        var backend = new WindowsUpdateSequenceBackend(runner, null!, null!, () => "helper", new WingetAvailabilityService(runner, ["winget.exe"]));
        var app = new UpdateItem { Id = "vendor.tool", Name = "WinGetUI", InstalledVersion = "5.0", AvailableVersion = "6.0" };
        var result = await backend.ExecuteAsync(new(app.Id, app.Name, UpdateComponent.Winget, app), CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal("WinGetUI", result.Step.Title);
        Assert.Equal("WinGetUI", result.Step.Application!.Name);
        Assert.Contains(sourceFails ? "Actualisation des sources des applications impossible" : "Sources des applications actualisées", result.Message);
        Assert.DoesNotContain("WinGet", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Runner(bool sourceFails) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token) =>
            Task.FromResult(new ProcessRunResult(sourceFails && request.Arguments[0] == "source" ? 1 : 0,
                request.Arguments[0] == "--version" ? "1.8.0" : "", ""));
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token) => throw new InvalidOperationException();
    }
}
