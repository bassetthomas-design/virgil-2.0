using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class D1bisASequenceTests
{
    [Fact]
    public async Task Real_sequence_discovers_and_updates_Store_package_with_version_proof()
    {
        var client = new Client();
        var backend = new Backend(client);
        var gate = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, gate);
        var scan = await service.ScanAsync([], CancellationToken.None);
        var selected = scan.Where(s => s.Component == UpdateComponent.Store).ToArray();
        Assert.Equal("Photos", Assert.Single(selected).Title);
        var result = await service.ExecuteAsync(selected, gate.Issue(service.CreatePreview(selected),
            new(ConfirmationLevel.Reinforced, null)), null, CancellationToken.None);
        Assert.False(result.WasRefused);
        Assert.Equal("mis à jour", Assert.Single(result.Verifications, v => v.Component == UpdateComponent.Store).Status);
        Assert.Equal(new[] { "product-a" }, client.Started);
        Assert.True(client.Reads >= 2);
        Assert.All(result.Steps.Where(s => s.Step.Component == UpdateComponent.Store), s => Assert.True(s.Succeeded));
    }

    [Fact]
    public async Task Store_already_managed_by_application_source_is_not_scheduled_twice()
    {
        var service = new UpdateSequenceService(new Backend(new Client()), new ConfirmationService(TimeProvider.System));
        var scan = await service.ScanAsync([new UpdateItem { Id = "product-a", Name = "Photos", Source = UpdateSource.MicrosoftStore }], CancellationToken.None);
        Assert.DoesNotContain(scan, s => s.Component == UpdateComponent.Store && s.Application?.Id == "product-a");
        Assert.Single(scan, s => s.Component == UpdateComponent.Winget && s.Id == "product-a");
    }

    [Fact]
    public async Task Missing_confirmation_blocks_all_Store_effects()
    {
        var client = new Client();
        var service = new UpdateSequenceService(new Backend(client), new ConfirmationService(TimeProvider.System));
        var scan = await service.ScanAsync([], CancellationToken.None);
        var result = await service.ExecuteAsync(scan.Where(s => s.Component == UpdateComponent.Store).ToArray(), null, null, CancellationToken.None);
        Assert.True(result.WasRefused);
        Assert.Empty(client.Started);
    }

    [Fact]
    public async Task Outside_inventory_reads_only_and_excludes_known_and_system_entries()
    {
        var runner = new ReaderRunner();
        var backend = new Backend(new Client()) { OutsideWinget = new(new OutsideWingetWindowsReader(runner)) };
        var service = new UpdateSequenceService(backend, new ConfirmationService(TimeProvider.System));
        var scan = await service.ScanAsync([], CancellationToken.None);
        var outside = Assert.Single(scan, s => s.Application?.Name == "Éditeur local");
        Assert.True(outside.IsInformational);
        Assert.Equal("3.2", outside.Application!.InstalledVersion);
        Assert.DoesNotContain(scan, s => s.Application?.Name == "Application connue");
        Assert.DoesNotContain(scan, s => s.Application?.Name == "Composant système");
        Assert.NotEmpty(runner.Requests);
        Assert.All(runner.Requests, request =>
        {
            var script = request.Arguments.Last();
            foreach (var forbidden in new[] { "SetValue", "CreateSubKey", "DeleteSubKey", "Remove-Item", "Set-Item", "Start-Process", "Install-", " upgrade ", " install " })
                Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(runner.Requests, request => request.Arguments.Last().Contains("Registry32", StringComparison.Ordinal));
        Assert.Contains(runner.Requests, request => request.Arguments.Last().Contains("Registry64", StringComparison.Ordinal));
        Assert.Equal(0, runner.Launches);
    }

    private sealed class Client : IStoreUpdateClient
    {
        public List<string> Started { get; } = [];
        public int Reads { get; private set; }
        public Task<StoreSearch> SearchAsync(CancellationToken token) => Task.FromResult(new StoreSearch(true, "", Started.Count == 0 ? [new("product-a", "family-a", "Photos", "1.0")] : []));
        public Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token)
        { Reads++; return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["family-a"] = Started.Count == 0 ? "1.0" : "2.0" }); }
        public Task<StoreStart> StartAsync(StorePackage package, CancellationToken token)
        { Started.Add(package.ProductId); return Task.FromResult(new StoreStart(true, "")); }
    }
    private sealed class Backend(Client client) : IUpdateSequenceBackend, IStoreUpdateBackend, IOutsideWingetBackend
    {
        public StoreUpdateService StoreUpdates { get; } = new(client);
        public OutsideWingetInventory OutsideWinget { get; init; } = new(new EmptyReader());
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) =>
            Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Protection vérifiée.") { RestorePointVerified = true });
    }
    private sealed class EmptyReader : IOutsideWingetReader
    {
        public Task<OutsideWingetSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(new OutsideWingetSnapshot([], []));
    }
    private sealed class ReaderRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];
        public int Launches { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0,
                "{\"Installed\":[{\"Name\":\"Éditeur local\",\"Version\":\"3.2\"},{\"Name\":\"Application connue\",\"Version\":\"1\"},{\"Name\":\"Composant système\",\"Version\":\"1\",\"SystemComponent\":true},{\"Name\":\"\",\"Version\":\"1\"}],\"ManagedNames\":[\"Application connue\"],\"IsComplete\":true}", ""));
        }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token)
        { Launches++; throw new InvalidOperationException("Installation interdite."); }
    }
}
