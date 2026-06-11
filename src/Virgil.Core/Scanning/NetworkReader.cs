using System.Net.NetworkInformation;
using System.Net.Sockets;
using Virgil.Domain;

namespace Virgil.Core.Scanning;

internal static class NetworkReader
{
    private const string NotAvailable = "N/A";

    public static ScanReaderResult<NetworkScanInfo> ReadPrimaryInterface()
    {
        try
        {
            var active = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(IsCandidate)
                .Select(CreateCandidate)
                .Where(candidate => candidate is not null)
                .Cast<NetworkCandidate>()
                .OrderByDescending(candidate => candidate.HasGateway)
                .ThenByDescending(candidate => candidate.SpeedBitsPerSecond)
                .FirstOrDefault();

            if (active is null)
            {
                return new ScanReaderResult<NetworkScanInfo>(
                    Empty("Aucun reseau actif"),
                    ["Aucune interface reseau active detectee."]);
            }

            return ScanReaderResult<NetworkScanInfo>.Success(active.Info);
        }
        catch
        {
            return new ScanReaderResult<NetworkScanInfo>(
                Empty(NotAvailable),
                ["Lecture reseau indisponible."]);
        }
    }

    private static bool IsCandidate(NetworkInterface networkInterface)
    {
        return networkInterface.OperationalStatus == OperationalStatus.Up
            && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback
            && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel;
    }

    private static NetworkCandidate? CreateCandidate(NetworkInterface networkInterface)
    {
        try
        {
            var properties = networkInterface.GetIPProperties();
            var ipv4 = properties.UnicastAddresses
                .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                ?.Address
                .ToString() ?? NotAvailable;

            if (ipv4 == NotAvailable)
            {
                return null;
            }

            var gateway = properties.GatewayAddresses
                .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                ?.Address
                .ToString() ?? NotAvailable;

            var dnsServers = properties.DnsAddresses
                .Select(address => address.ToString())
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .ToList();

            var info = new NetworkScanInfo(
                networkInterface.Name,
                MapType(networkInterface.NetworkInterfaceType),
                networkInterface.OperationalStatus.ToString(),
                networkInterface.Speed,
                ipv4,
                gateway,
                dnsServers);

            return new NetworkCandidate(info, gateway != NotAvailable, networkInterface.Speed);
        }
        catch
        {
            return null;
        }
    }

    private static string MapType(NetworkInterfaceType type)
    {
        return type switch
        {
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT => "Ethernet",
            _ => "Autre"
        };
    }

    private static NetworkScanInfo Empty(string status)
    {
        return new NetworkScanInfo(NotAvailable, NotAvailable, status, 0, NotAvailable, NotAvailable, Array.Empty<string>());
    }

    private sealed record NetworkCandidate(NetworkScanInfo Info, bool HasGateway, long SpeedBitsPerSecond);
}
