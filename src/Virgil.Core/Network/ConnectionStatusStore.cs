namespace Virgil.Core.Network;

public sealed class ConnectionStatusStore(IConnectionStatusProbe probe, TimeProvider clock,
    TimeSpan? debounceWindow = null, TimeSpan? notificationCooldown = null) : IConnectionStatusStore
{
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _lastNotifications = new();
    private ConnectionSecuritySnapshot? _current;
    private (InternetState Internet, VpnState Vpn, WifiProtection Wifi)? _pending;
    private DateTimeOffset _pendingSince;
    private int _refreshCount;
    private bool _disposed;

    public ConnectionSecuritySnapshot? Current { get { lock (_sync) return _current; } }
    public event EventHandler? Changed;
    public event EventHandler<ConnectionNotification>? NotificationRequested;

    public async Task<ConnectionSecuritySnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _refreshCount++;
        }

        try
        {
            await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_sync) ObjectDisposedException.ThrowIf(_disposed, this);
                var reading = await probe.ReadAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var now = clock.GetUtcNow();
                var next = ConnectionSecurityEvaluator.Evaluate(reading, now);
                var state = (next.Internet, next.Vpn, next.Wifi);
                List<ConnectionNotification> notifications = [];
                bool changed;
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    var previous = _current;
                    if (previous is not null && state != (previous.Internet, previous.Vpn, previous.Wifi))
                    {
                        if (_pending != state)
                        {
                            _pending = state;
                            _pendingSince = now;
                        }
                        if (now - _pendingSince < (debounceWindow ?? TimeSpan.FromSeconds(5)))
                            return previous;
                        foreach (var notification in GetNotifications(previous, next))
                        {
                            if (_lastNotifications.TryGetValue(notification.Key, out var last) &&
                                now - last < (notificationCooldown ?? TimeSpan.FromSeconds(30)))
                                continue;
                            _lastNotifications[notification.Key] = now;
                            notifications.Add(notification);
                        }
                    }

                    _pending = null;
                    changed = previous is null || next != (previous with { CheckedAtUtc = now });
                    _current = next;
                }

                if (changed) Changed?.Invoke(this, EventArgs.Empty);
                foreach (var notification in notifications) NotificationRequested?.Invoke(this, notification);
                return next;
            }
            finally { _refresh.Release(); }
        }
        finally
        {
            lock (_sync)
                if (--_refreshCount == 0 && _disposed) _refresh.Dispose();
        }
    }

    private static IEnumerable<ConnectionNotification> GetNotifications(ConnectionSecuritySnapshot previous, ConnectionSecuritySnapshot next)
    {
        if (previous.Internet != next.Internet)
        {
            var message = next.Internet switch
            {
                InternetState.NoNetwork => "Perte de connexion réseau détectée.",
                InternetState.Connected => "Connexion Internet rétablie.",
                InternetState.LocalWithoutInternet => "Réseau local actif sans accès Internet.",
                _ => null
            };
            if (message is not null) yield return new($"Internet.{next.Internet}", message, next.InternetLevel, next.CheckedAtUtc);
        }

        if (previous.Vpn != next.Vpn)
        {
            var message = next.Vpn switch
            {
                VpnState.ActiveFullTunnel => "Tunnel VPN actif (tout le trafic).",
                VpnState.ActiveSplitTunnel => "Tunnel VPN actif avec routage partiel : une partie du trafic sort hors VPN.",
                VpnState.InstalledInactive => "Le VPN détecté est inactif.",
                _ => null
            };
            if (message is not null) yield return new($"Vpn.{next.Vpn}", message, next.VpnLevel, next.CheckedAtUtc);
        }

        if (previous.Wifi != next.Wifi && next.Wifi is WifiProtection.Open or WifiProtection.Wep)
            yield return new("Wifi.Insecure", "Attention : réseau Wi-Fi ouvert ou chiffrement WEP obsolète.", ConnectionLevel.Alert, next.CheckedAtUtc);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_refreshCount == 0) _refresh.Dispose();
        }
    }
}
