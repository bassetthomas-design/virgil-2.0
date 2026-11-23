using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Virgil.Core.Network;

namespace Virgil.Infrastructure.Windows.Network;

public sealed class WindowsConnectionSecurityProbe(HttpClient httpClient) : IConnectionStatusProbe
{
    public async Task<ConnectionProbeReading> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (routesAvailable, defaults) = WindowsNetworkNativeProbe.ReadDefaultRouteInterfaces();
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(adapter =>
            {
                var properties = adapter.GetIPProperties();
                var tunnel = adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel;
                var vpn = ConnectionSecurityEvaluator.IdentifyVpnAdapter(adapter.Name, adapter.Description, tunnel);
                var defaultRoute = routesAvailable &&
                    (properties.GetIPv4Properties() is { } ipv4 && defaults.Contains(ipv4.Index) ||
                     properties.GetIPv6Properties() is { } ipv6 && defaults.Contains(ipv6.Index));
                return new ConnectionAdapterObservation(adapter.Name, adapter.Description, adapter.OperationalStatus == OperationalStatus.Up,
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211, tunnel,
                    properties.GatewayAddresses.Count > 0, properties.UnicastAddresses.Count > 0, defaultRoute, vpn.IsKnownVpnAdapter, vpn.ProviderHint);
            }).ToArray();
        var wifi = adapters.Any(adapter => adapter.IsUp && adapter.IsWireless)
            ? WindowsNetworkNativeProbe.ReadWifiProtection() : WifiProtection.NotApplicable;
        var clients = ReadVpnClients();

        (bool Success, bool Captive)[] results;
        using (var httpDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            httpDeadline.CancelAfter(TimeSpan.FromSeconds(3));
            results = await Task.WhenAll(
                CheckTargetAsync("http://www.msftconnecttest.com/connecttest.txt", "Microsoft Connect Test", httpDeadline.Token),
                CheckTargetAsync("https://detectportal.firefox.com/success.txt", "success", httpDeadline.Token)).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var successes = results.Count(result => result.Success);
        var captive = results.Any(result => result.Captive);
        var dns = successes == 0 && !captive && await CheckDnsAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(adapters, results.Length, successes, captive, dns, routesAvailable, wifi, clients);
    }

    private async Task<(bool Success, bool Captive)> CheckTargetAsync(string url, string expected, CancellationToken token)
    {
        try
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400) return (false, true);
            if (response.StatusCode != HttpStatusCode.OK) return (false, response.IsSuccessStatusCode);

            // Both probes return short ASCII bodies, optionally terminated by a newline.
            using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[expected.Length + 3];
            var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, false, token).ConfigureAwait(false);
            var body = System.Text.Encoding.ASCII.GetString(buffer, 0, length);
            var success = body == expected || body == expected + "\n" || body == expected + "\r\n";
            return (success, !success);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
        {
            return (false, false);
        }
    }

    private static async Task<bool> CheckDnsAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            return (await Dns.GetHostAddressesAsync("www.msftconnecttest.com", deadline.Token).ConfigureAwait(false)).Length > 0;
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ReadVpnClients()
    {
        var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var provider = process.ProcessName.ToLowerInvariant() switch
                    {
                        "nordvpn" or "nordvpn-service" => "NordVPN",
                        "protonvpn" or "protonvpn.client" => "Proton VPN",
                        "mullvad vpn" or "mullvad-daemon" => "Mullvad",
                        "tailscale" or "tailscaled" or "tailscale-ipn" => "Tailscale",
                        "forticlient" or "fortitray" => "FortiClient",
                        "vpnui" or "vpnagent" or "csc_ui" => "Cisco AnyConnect",
                        "expressvpn" or "expressvpnservice" => "ExpressVPN",
                        "surfshark" or "surfshark.service" => "Surfshark",
                        "cyberghost" => "CyberGhost",
                        "windscribe" => "Windscribe",
                        "pangpa" or "pangps" => "GlobalProtect",
                        "wireguard" => "WireGuard",
                        "openvpn" or "openvpn-gui" => "OpenVPN",
                        "vpnclient" => "SoftEther",
                        _ => null
                    };
                    if (provider is not null) providers.Add(provider);
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception)
                {
                    // The process may exit before its name can be read.
                }
            }
        }
        return providers.ToArray();
    }
}
