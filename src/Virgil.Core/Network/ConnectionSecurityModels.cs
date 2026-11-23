namespace Virgil.Core.Network;

public enum InternetState { Unknown, Connected, Limited, LocalWithoutInternet, NoNetwork }
public enum VpnState { Unknown, Absent, InstalledInactive, ActiveFullTunnel, ActiveSplitTunnel }
public enum WifiProtection { NotApplicable, Unknown, Open, Wep, Wpa, Wpa2, Wpa3 }
public enum ConnectionLevel { Unknown, Healthy, Attention, Alert }

public sealed record ConnectionAdapterObservation(string Name, string Description, bool IsUp, bool IsWireless,
    bool IsTunnel, bool HasGateway, bool HasUnicastIp, bool HasDefaultRoute, bool IsKnownVpnAdapter, string? VpnProviderHint);

public sealed record ConnectionProbeReading(IReadOnlyList<ConnectionAdapterObservation> Adapters, int HttpsTargetsTotal,
    int HttpsTargetsSucceeded, bool CaptivePortalSuspected, bool DnsSucceeded, bool RoutesAvailable,
    WifiProtection WifiProtection, IReadOnlyList<string> RunningVpnClients);

public sealed record ConnectionSecuritySnapshot(InternetState Internet, ConnectionLevel InternetLevel, string InternetLabel,
    VpnState Vpn, ConnectionLevel VpnLevel, string VpnLabel, string? VpnProvider, WifiProtection Wifi,
    ConnectionLevel SecurityLevel, string SecurityLabel, string ScopeNote, DateTimeOffset CheckedAtUtc);

public sealed record ConnectionNotification(string Key, string Message, ConnectionLevel Level, DateTimeOffset OccurredAtUtc);

public interface IConnectionStatusProbe
{
    Task<ConnectionProbeReading> ReadAsync(CancellationToken cancellationToken);
}

public interface IConnectionStatusStore : IDisposable
{
    ConnectionSecuritySnapshot? Current { get; }
    event EventHandler? Changed;
    event EventHandler<ConnectionNotification>? NotificationRequested;
    Task<ConnectionSecuritySnapshot> RefreshAsync(CancellationToken cancellationToken);
}
