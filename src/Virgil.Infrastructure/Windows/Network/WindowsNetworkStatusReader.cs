using System.Net;
using System.Net.NetworkInformation;
using Virgil.Core.Network;
using Virgil.Infrastructure.Windows.Diagnostic;

namespace Virgil.Infrastructure.Windows.Network;

public sealed class WindowsNetworkStatusReader : INetworkStatusReader
{
    public async Task<NetworkStatus> ReadAsync(CancellationToken cancellationToken)
    {
        var adapters = await Task.Run(() => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n =>
            {
                var p = n.GetIPProperties();
                return new NetworkAdapterStatus(n.Name, n.OperationalStatus == OperationalStatus.Up ? "Connecté" : "Déconnecté",
                    string.Join(", ", p.UnicastAddresses.Select(a => a.Address)),
                    string.Join(", ", p.GatewayAddresses.Select(a => a.Address)),
                    string.Join(", ", p.DnsAddresses));
            }).ToArray(), cancellationToken);
        var gateway = adapters.FirstOrDefault(a => a.State == "Connecté" && a.Gateway.Length > 0)?.Gateway.Split(',')[0];
        var gatewayTest = PingAsync(gateway, cancellationToken);
        var internetTest = PingAsync("1.1.1.1", cancellationToken);
        string dns;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync("example.com", cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            dns = addresses.Length > 0 ? "Résolution DNS réussie (example.com)" : "Aucune adresse DNS reçue";
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException) { dns = "Résolution DNS indisponible"; }
        var connections = await Task.Run(() => ConnectionReader.Read(cancellationToken), cancellationToken);
        return new NetworkStatus(adapters, await gatewayTest, await internetTest, dns,
            connections is null ? "Connexions indisponibles" : $"{connections.Values.Sum()} connexions TCP établies et sockets UDP liés");
    }

    private static async Task<string> PingAsync(string? address, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(address)) return "Aucune passerelle détectée";
        using var ping = new Ping();
        try
        {
            var result = await ping.SendPingAsync(address, 1500).WaitAsync(token);
            return result.Status == IPStatus.Success ? $"{result.RoundtripTime} ms" : "Sans réponse (ICMP peut être bloqué)";
        }
        catch (Exception ex) when (ex is PingException or System.Net.Sockets.SocketException) { return "Mesure indisponible"; }
    }
}
