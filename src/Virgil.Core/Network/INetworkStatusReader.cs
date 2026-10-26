namespace Virgil.Core.Network;

public sealed record NetworkAdapterStatus(string Name, string State, string Addresses, string Gateway, string Dns);
public sealed record NetworkStatus(IReadOnlyList<NetworkAdapterStatus> Adapters, string GatewayLatency, string InternetLatency, string DnsTest, string Connections);
public interface INetworkStatusReader
{
    Task<NetworkStatus> ReadAsync(CancellationToken cancellationToken);
}
