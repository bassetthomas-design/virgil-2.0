using Virgil.App.ViewModels;
using Virgil.Core.Network;
using Xunit;

namespace Virgil.Tests;

public sealed class NetworkViewModelTests
{
    private static NetworkStatus Sample => new([], "1 ms", "10 ms", "Résolution réussie", "0 connexions");

    [Fact]
    public async Task RefreshPublishesSnapshotAndFailurePreservesLastMeasurement()
    {
        var reader = new Reader { Result = Task.FromResult(Sample) };
        var model = new NetworkViewModel(reader);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(Sample, model.Snapshot);
        Assert.StartsWith("Mesuré à ", model.Status);
        reader.Result = Task.FromException<NetworkStatus>(new InvalidOperationException());
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(Sample, model.Snapshot);
        Assert.Contains("indisponible", model.Status);
    }

    [Fact]
    public async Task CancellationKeepsSnapshot()
    {
        var pending = new TaskCompletionSource<NetworkStatus>();
        var reader = new Reader { Result = pending.Task };
        var model = new NetworkViewModel(reader);
        var first = model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, reader.Calls);
        model.RefreshCommand.Cancel();
        pending.SetCanceled();
        await first;
        Assert.Null(model.Snapshot);
        Assert.Equal("Prêt à observer le réseau.", model.Status);
    }

    [Fact]
    public async Task OverlappingRefreshDoesNotStartAnotherRead()
    {
        var pending = new TaskCompletionSource<NetworkStatus>();
        var reader = new Reader { Result = pending.Task };
        var model = new NetworkViewModel(reader);
        var first = model.RefreshCommand.ExecuteAsync(null);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, reader.Calls);
        pending.SetResult(Sample);
        await first;
        Assert.Equal(Sample, model.Snapshot);
    }

    [Fact]
    public void RepairNavigationRequestsNavigationWithoutReadingNetwork()
    {
        var reader = new Reader { Result = Task.FromResult(Sample) };
        var model = new NetworkViewModel(reader);
        var calls = 0;
        model.RepairRequested += (_, _) => calls++;
        model.OpenRepairCommand.Execute(null);
        Assert.Equal(1, calls);
        Assert.Equal(0, reader.Calls);
    }

    private sealed class Reader : INetworkStatusReader
    {
        public required Task<NetworkStatus> Result { get; set; }
        public int Calls { get; private set; }
        public Task<NetworkStatus> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Result.WaitAsync(cancellationToken);
        }
    }
}
