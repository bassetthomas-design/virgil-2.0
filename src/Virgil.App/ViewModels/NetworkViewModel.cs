using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Core.Network;

namespace Virgil.App.ViewModels;

public sealed class NetworkViewModel : ObservableObject
{
    private readonly INetworkStatusReader _reader;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private NetworkStatus? _snapshot;
    private string _status = "Prêt à observer le réseau.";
    public NetworkViewModel(INetworkStatusReader reader)
    {
        _reader = reader;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenRepairCommand = new RelayCommand(() => RepairRequested?.Invoke(this, EventArgs.Empty));
    }
    public event EventHandler? RepairRequested;
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand OpenRepairCommand { get; }
    public NetworkStatus? Snapshot { get => _snapshot; private set => SetProperty(ref _snapshot, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken)) return;
        try { Snapshot = await _reader.ReadAsync(cancellationToken); Status = $"Mesuré à {DateTime.Now:HH:mm}"; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { Status = "Lecture du réseau indisponible. Vous pouvez réessayer."; }
        finally { _refreshGate.Release(); }
    }
}
