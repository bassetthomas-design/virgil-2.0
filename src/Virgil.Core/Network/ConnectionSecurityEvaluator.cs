namespace Virgil.Core.Network;

public static class ConnectionSecurityEvaluator
{
    public static (bool IsKnownVpnAdapter, string? ProviderHint) IdentifyVpnAdapter(string name, string description, bool isTunnel)
    {
        var identity = $"{name} {description}";
        bool Contains(string pattern) => identity.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        if (new[] { "hyper-v", "vethernet", "vmware", "virtualbox", "vbox", "wsl", "docker", "teredo", "isatap", "6to4", "ip-https" }.Any(Contains))
            return (false, null);

        foreach (var (pattern, provider) in new[]
        {
            ("nordlynx", "NordVPN"), ("nordvpn", "NordVPN"), ("protonvpn", "Proton VPN"), ("proton vpn", "Proton VPN"),
            ("mullvad", "Mullvad"), ("tailscale", "Tailscale"), ("fortinet", "FortiClient"), ("forticlient", "FortiClient"),
            ("anyconnect", "Cisco AnyConnect"), ("expressvpn", "ExpressVPN"), ("surfshark", "Surfshark"),
            ("cyberghost", "CyberGhost"), ("windscribe", "Windscribe"), ("globalprotect", "GlobalProtect"), ("palo alto", "GlobalProtect")
        })
            if (Contains(pattern))
                return (true, provider);

        return (new[] { "wintun", "wireguard", "tap-windows", "openvpn", "softether" }.Any(Contains), null);
    }

    public static ConnectionSecuritySnapshot Evaluate(ConnectionProbeReading reading, DateTimeOffset checkedAtUtc)
    {
        var active = reading.Adapters.Where(adapter => adapter.IsUp && adapter.HasUnicastIp).ToArray();
        var (internet, internetLevel, internetLabel) = reading switch
        {
            _ when active.Length == 0 => (InternetState.NoNetwork, ConnectionLevel.Alert, "Aucun réseau détecté"),
            { CaptivePortalSuspected: true } => (InternetState.LocalWithoutInternet, ConnectionLevel.Attention, "Réseau local sans Internet (portail captif probable)"),
            { HttpsTargetsSucceeded: > 0 } when reading.HttpsTargetsSucceeded == reading.HttpsTargetsTotal =>
                (InternetState.Connected, ConnectionLevel.Healthy, "Connecté"),
            { HttpsTargetsSucceeded: > 0 } => (InternetState.Connected, ConnectionLevel.Healthy, "Connecté (1 cible de vérification indisponible)"),
            { DnsSucceeded: true } => (InternetState.Limited, ConnectionLevel.Attention, "Accès limité (requêtes HTTPS bloquées)"),
            { HttpsTargetsTotal: > 0 } => (InternetState.LocalWithoutInternet, ConnectionLevel.Attention, "Réseau local sans Internet"),
            _ => (InternetState.Unknown, ConnectionLevel.Unknown, "État Internet indéterminé")
        };

        var vpnAdapters = active.Where(adapter => adapter.IsKnownVpnAdapter).ToArray();
        var defaultAdapters = vpnAdapters.Where(adapter => adapter.HasDefaultRoute).ToArray();
        var providerAdapters = reading.RoutesAvailable && defaultAdapters.Length > 0 ? defaultAdapters : vpnAdapters;
        var hints = providerAdapters.Select(adapter => adapter.VpnProviderHint).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var clients = reading.RunningVpnClients.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var provider = hints.Length == 1 ? hints[0] : hints.Length == 0 && clients.Length == 1 ? clients[0] : null;
        var (vpn, vpnLevel, vpnLabel) = reading switch
        {
            _ when vpnAdapters.Length == 0 && (clients.Length > 0 || reading.Adapters.Any(adapter => adapter.IsKnownVpnAdapter)) =>
                (VpnState.InstalledInactive, ConnectionLevel.Attention, "Installé, inactif"),
            _ when vpnAdapters.Length == 0 => (VpnState.Absent, ConnectionLevel.Healthy, "Absent"),
            { RoutesAvailable: false } => (VpnState.Unknown, ConnectionLevel.Unknown,
                provider is null ? "VPN détecté, routage non vérifiable" : $"VPN détecté ({provider}), routage non vérifiable"),
            _ when defaultAdapters.Length > 0 => (VpnState.ActiveFullTunnel, ConnectionLevel.Healthy,
                provider is null ? "VPN actif (fournisseur non identifié)" : $"Actif ({provider}, tout le trafic)"),
            _ => (VpnState.ActiveSplitTunnel, ConnectionLevel.Attention,
                provider is null ? "VPN actif (routage partiel, fournisseur non identifié)" : $"Actif ({provider}, routage partiel)")
        };

        var wifi = reading.Adapters.Any(adapter => adapter.IsUp && adapter.IsWireless)
            ? reading.WifiProtection : WifiProtection.NotApplicable;
        var (securityLevel, securityLabel) = (wifi, vpn) switch
        {
            (WifiProtection.Open, _) => (ConnectionLevel.Alert, "Wi-Fi ouvert (non chiffré)"),
            (WifiProtection.Wep, _) => (ConnectionLevel.Alert, "Wi-Fi WEP (chiffrement obsolète)"),
            (WifiProtection.Wpa, _) => (ConnectionLevel.Attention, "Wi-Fi WPA ancien (préférer WPA2/WPA3)"),
            (WifiProtection.Wpa2, VpnState.ActiveSplitTunnel) => (ConnectionLevel.Attention, "Wi-Fi chiffré (WPA2) · VPN en routage partiel"),
            (WifiProtection.Wpa3, VpnState.ActiveSplitTunnel) => (ConnectionLevel.Attention, "Wi-Fi chiffré (WPA3) · VPN en routage partiel"),
            (WifiProtection.Wpa2, _) => (ConnectionLevel.Healthy, "Wi-Fi chiffré (WPA2)"),
            (WifiProtection.Wpa3, _) => (ConnectionLevel.Healthy, "Wi-Fi chiffré (WPA3)"),
            (WifiProtection.NotApplicable, VpnState.ActiveSplitTunnel) => (ConnectionLevel.Attention, "VPN en routage partiel"),
            (WifiProtection.NotApplicable, _) => (ConnectionLevel.Healthy, "Connexion filaire (Wi-Fi inactif)"),
            _ => (ConnectionLevel.Unknown, "Chiffrement Wi-Fi non lisible")
        };

        return new(internet, internetLevel, internetLabel, vpn, vpnLevel, vpnLabel, provider, wifi, securityLevel,
            securityLabel, "Mesure locale en lecture seule uniquement.", checkedAtUtc);
    }
}
