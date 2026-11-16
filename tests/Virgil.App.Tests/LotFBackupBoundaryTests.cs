using System.Text.RegularExpressions;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Status;
using Xunit;

namespace Virgil.Tests;

public sealed class LotFBackupBoundaryTests
{
    [Fact]
    public async Task Priority_launch_port_only_opens_backup_without_execution_elevation_or_extra_arguments()
    {
        var store = new SystemStatusStore(); var runner = new BackupOnlyRunner();
        using var vm = new HomePrioritiesViewModel(store, runner, TimeProvider.System);
        store.PublishScan(LotFPrioritiesTests.Report(
            new ScanFinding("disk.failure.0", "", "Disque", "", "", ScanSeverity.Critical, null),
            new("memory-usage", "", "Mémoire", "", "", ScanSeverity.Warning, null)));
        var navigation = new List<string>(); vm.NavigationRequested += navigation.Add;
        foreach (var item in vm.Items.ToArray()) await vm.OpenCommand.ExecuteAsync(item);
        Assert.Equal(new[] { "Ressources" }, navigation);
        Assert.Equal(1, runner.Launches);
        await vm.OpenCommand.ExecuteAsync(new HomePriorityItem("inventé", "", "", "ms-settings:backup"));
        Assert.Equal(1, runner.Launches);
        var source = File.ReadAllText(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src/Virgil.App/ViewModels/HomePrioritiesViewModel.cs"));
        Assert.DoesNotContain(".RunAsync(", source);
        foreach (var forbidden in new[] { "ScanAsync", "ReadAsync", "ConfirmedPlan", "runas", "IConfirmationService" })
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        Assert.All(Regex.Matches(source, "ms-settings:[a-z-]+"), match => Assert.Equal("ms-settings:backup", match.Value));
    }
    private sealed class BackupOnlyRunner : IProcessRunner
    {
        public int Launches { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token) =>
            throw new InvalidOperationException("Aucune exécution autorisée.");
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token)
        {
            Assert.Equal("ms-settings:backup", request.FileName); Assert.Empty(request.Arguments);
            Assert.True(request.UseShellExecute); Assert.Null(request.Verb); Assert.Null(request.RawArguments);
            Assert.False(request.WaitForExit); Assert.Equal(ProcessWindowMode.Normal, request.WindowMode);
            Launches++; return Task.FromResult(new ProcessLaunchResult(true, null));
        }
    }
}
