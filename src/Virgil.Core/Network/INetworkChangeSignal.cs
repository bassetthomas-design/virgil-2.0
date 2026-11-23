namespace Virgil.Core.Network;

public interface INetworkChangeSignal : IDisposable
{
    event EventHandler? Changed;
}
