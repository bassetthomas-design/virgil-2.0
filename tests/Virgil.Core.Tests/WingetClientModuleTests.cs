using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class WingetClientModuleTests
{
    [Fact]
    public async Task Module_installation_requires_a_matching_single_use_plan()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var installer = new Installer();
        var service = new WingetClientModuleService(installer, authority);
        Assert.False(await service.InstallAsync(null, CancellationToken.None));
        Assert.Equal(0, installer.Count);
        var wrongPreview = WingetClientModuleService.CreatePreview() with { ActionId = "other" };
        var wrong = authority.Issue(wrongPreview, new(ConfirmationLevel.Reinforced, null));
        Assert.False(await service.InstallAsync(wrong, CancellationToken.None));
        Assert.Equal(0, installer.Count);
        var plan = authority.Issue(WingetClientModuleService.CreatePreview(), new(ConfirmationLevel.Reinforced, null));
        Assert.True(await service.InstallAsync(plan, CancellationToken.None));
        Assert.False(await service.InstallAsync(plan, CancellationToken.None));
        Assert.Equal(1, installer.Count);
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
    public async Task Empty_module_inventory_never_installs_or_runs_cli_upgrade_during_scan()
    {
        var runner = new ScanRunner { Json = "[]" };
        var service = new WingetUpdateScanService(new WingetAvailabilityService(runner, ["winget.exe"]), runner,
            new UpdateRiskClassifier(), new WindowsUpdateStatusService(), new DriverInformationService(runner));
        var report = await service.ScanAsync(new UpdateScanRequest { Scope = UpdateScanScope.DeepPreview, IncludeApplicationUpdates = true }, null, CancellationToken.None);
        Assert.Empty(report.Items);
        Assert.False(report.IsWingetClientModuleMissing);
        Assert.Equal(2, runner.Requests.Count);
        Assert.DoesNotContain(runner.Requests, request => request.Arguments.Contains("upgrade"));
        Assert.All(runner.Requests, request => Assert.DoesNotContain("Install-Module", string.Join(" ", request.Arguments)));
    }

    [Fact]
    public async Task Catalog_maximum_is_not_pinned_when_cli_selects_an_applicable_version()
    {
        var runner = new ScanRunner();
        var availability = new WingetAvailabilityService(runner, ["winget.exe"]);
        var service = new WingetUpdateScanService(availability, runner,
            new UpdateRiskClassifier(), new WindowsUpdateStatusService(), new DriverInformationService(runner));
        var report = await service.ScanAsync(new UpdateScanRequest { IncludeApplicationUpdates = true }, null, CancellationToken.None);
        var item = Assert.Single(report.Items);
        Assert.Equal("2.0", item.AvailableVersion);
        var query = Assert.Single(runner.Requests, r => r.FileName == "powershell.exe");
        Assert.DoesNotContain("AvailableVersions[0]", query.Arguments.Last());
        var authority = new ConfirmationService(TimeProvider.System);
        var plan = authority.Issue(UpdateActionPreviewFactory.Create(item), new(ConfirmationLevel.Reinforced, null));
        var execution = new WingetUpdateExecutionService(availability, runner, authority);
        Assert.Equal(UpdateItemStatus.Completed, (await execution.ExecuteAsync(item, plan, CancellationToken.None)).Status);
        var upgrade = Assert.Single(runner.Requests, r => r.Arguments.Contains("--id"));
        var arguments = upgrade.Arguments.ToList();
        Assert.Equal("2.0", arguments[arguments.IndexOf("--version") + 1]);
        Assert.DoesNotContain("99.0", arguments);
    }

    [Fact]
    public async Task Infrastructure_adapter_preserves_cancellation_and_fixed_install_command()
    {
        var runner = new Runner();
        var installer = new WingetClientModuleInstaller(runner);
        Assert.True(await installer.InstallAsync(CancellationToken.None));
        Assert.Equal(1, runner.Count);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(stop.Token));
        Assert.Equal(1, runner.Count);
        runner.Cancel = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(CancellationToken.None));
    }

    private sealed class Installer : IWingetClientModuleInstaller
    {
        public int Count { get; private set; }
        public Task<bool> InstallAsync(CancellationToken cancellationToken)
        { Count++; return Task.FromResult(true); }
    }

    private sealed class ScanRunner : IProcessRunner
    {
        public string Json { get; init; } = "[{\"Id\":\"Discord.Discord\",\"Name\":\"Discord\",\"InstalledVersion\":\"1.0\",\"AvailableVersions\":[\"99.0\",\"2.0\"],\"Source\":\"winget\"}]";
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, request.FileName == "powershell.exe" ? Json
                : request.Arguments.Count == 1 && request.Arguments[0] == "--version" ? "1.8.0"
                : "Name    Id              Version Available Source\n-------------------------------------------------\nDiscord Discord.Discord 1.0     2.0       winget", string.Empty));
        }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Runner : IProcessRunner
    {
        public bool Cancel { get; set; }
        public int Count { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Count++;
            Assert.Equal("powershell.exe", request.FileName);
            Assert.Contains("-NonInteractive", request.Arguments);
            Assert.Contains("-Scope CurrentUser", request.Arguments[^1]);
            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty, Cancelled: Cancel));
        }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
