using System.Net.NetworkInformation;
using Virgil.Core.Network;

namespace Virgil.Infrastructure.Windows.Network;

public sealed class WindowsNetworkChangeSignal : INetworkChangeSignal
{
    private int _disposed;

    public WindowsNetworkChangeSignal()
    {
        NetworkChange.NetworkAddressChanged += AddressChanged;
        NetworkChange.NetworkAvailabilityChanged += AvailabilityChanged;
    }

    public event EventHandler? Changed;

    private void AddressChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    private void AvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        NetworkChange.NetworkAddressChanged -= AddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= AvailabilityChanged;
    }
}
