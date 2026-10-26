using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class WingetClientModuleTests
{
    [Fact]
    public async Task Module_installation_requires_a_matching_single_use_plan()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var runner = new Runner();
        var service = new WingetClientModuleService(runner, authority);
        Assert.False(await service.InstallAsync(null, CancellationToken.None));
        Assert.Equal(0, runner.Count);
        var wrongPreview = WingetClientModuleService.CreatePreview() with { ActionId = "other" };
        var wrong = authority.Issue(wrongPreview, new(ConfirmationLevel.Reinforced, null));
        Assert.False(await service.InstallAsync(wrong, CancellationToken.None));
        Assert.Equal(0, runner.Count);
        var plan = authority.Issue(WingetClientModuleService.CreatePreview(), new(ConfirmationLevel.Reinforced, null));
        Assert.True(await service.InstallAsync(plan, CancellationToken.None));
        Assert.False(await service.InstallAsync(plan, CancellationToken.None));
        Assert.Equal(1, runner.Count);
    }

    [Fact]
    public void Scan_options_follow_capabilities_and_never_install()
    {
        var arguments = WingetUpdateScanService.BuildScanArguments(WingetAvailabilityService.GetCapabilities("1.8.0"));
        Assert.Equal("upgrade", arguments[0]);
        Assert.Contains("--include-unknown", arguments);
        Assert.Contains("--include-pinned", arguments);
        Assert.Contains("--disable-interactivity", arguments);
        Assert.DoesNotContain("--all", arguments);
        Assert.DoesNotContain("--id", arguments);
        var conservative = WingetUpdateScanService.BuildScanArguments(WingetCapabilities.Conservative);
        Assert.Single(conservative);
    }

    [Fact]
    public async Task Scan_prefers_module_json_and_never_installs_during_scan()
    {
        var runner = new ScanRunner();
        var service = new WingetUpdateScanService(new WingetAvailabilityService(runner, ["winget.exe"]), runner,
            new UpdateRiskClassifier(), new WindowsUpdateStatusService(), new DriverInformationService(runner));
        var report = await service.ScanAsync(new UpdateScanRequest { Scope = UpdateScanScope.DeepPreview, IncludeApplicationUpdates = true }, null, CancellationToken.None);
        Assert.Single(report.Items);
        Assert.Equal("Discord.Discord", report.Items[0].Id);
        Assert.False(report.IsWingetClientModuleMissing);
        Assert.Equal(2, runner.Requests.Count);
        Assert.DoesNotContain(runner.Requests, request => request.Arguments.Contains("upgrade"));
        Assert.All(runner.Requests, request => Assert.DoesNotContain("Install-Module", string.Join(" ", request.Arguments)));
    }

    private sealed class ScanRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, request.FileName == "powershell.exe"
                ? "[{\"Id\":\"Discord.Discord\",\"Name\":\"Discord\",\"InstalledVersion\":\"1\",\"Available\":\"2\",\"Source\":\"winget\"}]"
                : "1.8.0", string.Empty));
        }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Runner : IProcessRunner
    {
        public int Count { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Count++;
            Assert.Equal("powershell.exe", request.FileName);
            Assert.Contains("-NonInteractive", request.Arguments);
            Assert.Contains("-Scope CurrentUser", request.Arguments[^1]);
            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty));
        }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
