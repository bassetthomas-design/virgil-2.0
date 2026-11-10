using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class D1bisAStoreAdapterTests
{
    [Fact]
    public async Task Store_search_disables_installation_and_forced_restart_before_confirmation()
    {
        var runner = new Runner { Output = "{\"Supported\":false,\"Reason\":\"Droits insuffisants.\",\"Packages\":[]}" };
        var search = await new StoreWindowsClient(runner).SearchAsync(CancellationToken.None);
        Assert.False(search.Supported);
        Assert.Equal("Droits insuffisants.", search.Reason);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("powershell.exe", request.FileName);
        var script = request.Arguments.Last();
        Assert.Contains("SearchForAllUpdatesAsync", script);
        Assert.Contains("AutomaticallyDownloadAndInstallUpdateIfFound=$false", script);
        Assert.Contains("AllowForcedAppRestart=$false", script);
        Assert.DoesNotContain("UpdateAppByPackageFamilyNameAsync", script);
        Assert.DoesNotContain("Start-Process", script);
        Assert.DoesNotContain("ms-windows-store:", script);
    }

    [Fact]
    public async Task Unsafe_family_cannot_enter_installation_script()
    {
        var runner = new Runner();
        var result = await new StoreWindowsClient(runner).StartAsync(new("product", "family'; injected", "Application", "1.0"), CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.Contains("bloquée", result.Reason);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Store_installation_uses_official_family_API_without_opening_Store()
    {
        var runner = new Runner { Output = "{\"Accepted\":true,\"Reason\":\"\",\"WasUnsupported\":false}" };
        var result = await new StoreWindowsClient(runner).StartAsync(new("product", "Family_123", "Application", "1.0"), CancellationToken.None);
        Assert.True(result.Accepted);
        var script = Assert.Single(runner.Requests).Arguments.Last();
        Assert.Contains("UpdateAppByPackageFamilyNameAsync($family)", script);
        Assert.Contains("$item.GetCurrentStatus()", script);
        Assert.Contains("LaunchAfterInstall=$false", script);
        Assert.DoesNotContain("Start-Process", script);
        Assert.DoesNotContain("ms-windows-store:", script);
    }

    [Fact]
    public async Task Registry_inventory_uses_nonwritable_keys_and_never_launches_an_installer()
    {
        var runner = new Runner { Output = "{\"Installed\":[],\"ManagedNames\":[],\"IsComplete\":true}" };
        var snapshot = await new OutsideWingetWindowsReader(runner).ReadAsync(CancellationToken.None);
        Assert.True(snapshot.IsComplete);
        var script = Assert.Single(runner.Requests).Arguments.Last();
        Assert.Contains("$root.OpenSubKey('SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall',$false)", script);
        Assert.Contains("$key.OpenSubKey($name,$false)", script);
        Assert.Contains("RegistryHive]::LocalMachine", script);
        Assert.Contains("RegistryHive]::CurrentUser", script);
        Assert.Contains("RegistryView]::Registry32", script);
        Assert.Contains("RegistryView]::Registry64", script);
        Assert.DoesNotContain("UninstallString", script);
        Assert.DoesNotContain("SetValue", script);
        Assert.DoesNotContain("Start-Process", script);
    }

    private sealed class Runner : IProcessRunner
    {
        public string Output { get; init; } = "";
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token)
        { Requests.Add(request); return Task.FromResult(new ProcessRunResult(0, Output, "")); }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token) =>
            throw new InvalidOperationException("Aucun lancement interactif autorisé.");
    }
}
