using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Core.Network;

namespace Virgil.App.ViewModels;

public sealed class NetworkViewModel : ObservableObject
{
    private readonly INetworkStatusReader _reader;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _refreshCancellation;
    private NetworkStatus? _snapshot;
    private string _status = "Prêt à observer le réseau.";
    public NetworkViewModel(INetworkStatusReader reader)
    {
        _reader = reader;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _refreshCancellation is null);
        OpenRepairCommand = new RelayCommand(() => RepairRequested?.Invoke(this, EventArgs.Empty));
    }
    public event EventHandler? RepairRequested;
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand OpenRepairCommand { get; }
    public NetworkStatus? Snapshot { get => _snapshot; private set => SetProperty(ref _snapshot, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public void CancelRefresh() => _refreshCancellation?.Cancel();

    private async Task RefreshAsync()
    {
        // Keep cancellation separate from AsyncRelayCommand: a duplicate programmatic
        // invocation must not cancel the active measurement and start another read.
        if (!await _refreshGate.WaitAsync(0)) return;
        using var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        RefreshCommand.NotifyCanExecuteChanged();
        var cancellationToken = cancellation.Token;
        try { Snapshot = await _reader.ReadAsync(cancellationToken); Status = $"Mesuré à {DateTime.Now:HH:mm}"; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { Status = "Lecture du réseau indisponible. Vous pouvez réessayer."; }
        finally
        {
            _refreshCancellation = null;
            _refreshGate.Release();
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }
}
