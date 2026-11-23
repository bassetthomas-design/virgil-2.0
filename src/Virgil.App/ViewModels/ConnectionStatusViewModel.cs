using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Core.Network;

namespace Virgil.App.ViewModels;

public sealed partial class ConnectionStatusViewModel : ObservableObject, IDisposable
{
    private const string InternetUnknown = "État Internet indéterminé";
    private const string VpnUnknown = "État VPN indéterminé";
    private const string WifiUnknown = "Chiffrement Wi-Fi non lisible";
    private const string InitialInternet = "Internet : " + InternetUnknown;
    private const string InitialVpn = "VPN : " + VpnUnknown;
    private const string CaptivePortal = "Réseau local sans Internet (portail captif probable)";
    private const string InitialAdvice = "Aucune mesure récente : utilisez Actualiser pour lire l'état actuel.";
    private const string Scope = "Mesure locale en lecture seule uniquement.";
    private readonly IConnectionStatusStore _store;
    private readonly TimeProvider _clock;
    private readonly ConnectionChangeSignal _signal;
    private readonly TimeSpan _networkDebounce;
    private readonly TimeSpan _pollInterval;
    private readonly SynchronizationContext? _context;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private ITimer? _debounceTimer;
    private ITimer? _pollTimer;
    private DateTimeOffset? _refreshDue;
    private Task _refresh = Task.CompletedTask;
    private bool _started;
    private volatile bool _disposed;

    public ConnectionStatusViewModel(IConnectionStatusStore store, TimeProvider clock, ConnectionChangeSignal networkChange,
        TimeSpan? networkDebounce = null, TimeSpan? pollInterval = null)
    {
        _store = store;
        _clock = clock;
        _signal = networkChange;
        _networkDebounce = networkDebounce ?? TimeSpan.FromSeconds(1);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(30);
        _context = SynchronizationContext.Current;
    }

    [ObservableProperty] private string internetBadgeText = InitialInternet;
    [ObservableProperty] private string vpnBadgeText = InitialVpn;
    [ObservableProperty] private string explanationToolTip = string.Join(Environment.NewLine, InitialInternet, InitialVpn, Scope);
    [ObservableProperty] private ConnectionLevel internetLevel;
    [ObservableProperty] private ConnectionLevel vpnLevel;
    [ObservableProperty] private string internetStatusText = InternetUnknown;
    [ObservableProperty] private string vpnStatusText = VpnUnknown;
    [ObservableProperty] private string wifiProtectionText = WifiUnknown;
    [ObservableProperty] private string captivePortalText = string.Empty;
    [ObservableProperty] private string explanationText = string.Join(" · ", InternetUnknown, VpnUnknown, WifiUnknown);
    [ObservableProperty] private string adviceText = InitialAdvice;
    [ObservableProperty] private string checkedAtText = string.Empty;
    [ObservableProperty] private string vpnProviderText = string.Empty;
    [ObservableProperty] private ConnectionLevel securityLevel = ConnectionLevel.Unknown;

    public string ScopeNoteText => Scope;

    public event Action<string>? VirgilMessageRequested;
    public event Action<string>? NavigationRequested;

    [RelayCommand]
    private void OpenNetwork() => NavigationRequested?.Invoke("Réseau");

    [RelayCommand]
    private Task ActualiserAsync()
    {
        lock (_gate)
        {
            if (_disposed)
                return Task.CompletedTask;

            var refresh = RefreshAsync(_lifetime.Token);
            _refresh = Task.WhenAll(_refresh, refresh);
            return refresh;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
                return;

            _started = true;
            _debounceTimer = _clock.CreateTimer(_ => RefreshAfterSignal(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _pollTimer = _clock.CreateTimer(_ => RequestRefresh(), null, _pollInterval, _pollInterval);
            _store.Changed += StoreChanged;
            _store.NotificationRequested += Notify;
            _signal.Changed += SignalChanged;
            Dispatch(ApplySnapshot);
            RequestRefresh();
        }
    }

    private void StoreChanged(object? sender, EventArgs args) => Dispatch(ApplySnapshot);

    private void Notify(object? sender, ConnectionNotification notification) =>
        Dispatch(() => VirgilMessageRequested?.Invoke(notification.Message));

    private void ApplySnapshot()
    {
        var snapshot = _store.Current;
        InternetBadgeText = snapshot is null ? InitialInternet : $"Internet : {snapshot.InternetLabel}";
        VpnBadgeText = snapshot is null ? InitialVpn : $"VPN : {snapshot.VpnLabel}";
        InternetLevel = snapshot?.InternetLevel ?? ConnectionLevel.Unknown;
        VpnLevel = snapshot?.VpnLevel ?? ConnectionLevel.Unknown;
        ExplanationToolTip = snapshot is null
            ? string.Join(Environment.NewLine, InternetBadgeText, VpnBadgeText, Scope)
            : string.Join(Environment.NewLine, InternetBadgeText, VpnBadgeText, snapshot.SecurityLabel, Scope);

        InternetStatusText = snapshot?.InternetLabel ?? InternetUnknown;
        VpnStatusText = snapshot?.VpnLabel ?? VpnUnknown;
        WifiProtectionText = snapshot?.SecurityLabel ?? WifiUnknown;
        CaptivePortalText = snapshot?.InternetLabel == CaptivePortal ? CaptivePortal : string.Empty;
        ExplanationText = string.Join(" · ", InternetStatusText, VpnStatusText, WifiProtectionText);
        AdviceText = GetAdvice(snapshot);
        CheckedAtText = snapshot is null ? string.Empty : "Mesuré à " +
            TimeZoneInfo.ConvertTime(snapshot.CheckedAtUtc, _clock.LocalTimeZone)
                .ToString("HH:mm", CultureInfo.GetCultureInfo("fr-FR"));
        SecurityLevel = snapshot?.SecurityLevel ?? ConnectionLevel.Unknown;
        VpnProviderText = snapshot switch
        {
            { Vpn: VpnState.ActiveFullTunnel or VpnState.ActiveSplitTunnel } =>
                snapshot.VpnProvider ?? "fournisseur non identifié",
            { Vpn: VpnState.Unknown } when snapshot.VpnLabel.EndsWith("routage non vérifiable", StringComparison.Ordinal) =>
                snapshot.VpnProvider ?? "fournisseur non identifié",
            _ => string.Empty
        };
    }

    private static string GetAdvice(ConnectionSecuritySnapshot? snapshot) => snapshot switch
    {
        null => InitialAdvice,
        { Internet: InternetState.NoNetwork } => "Vérifiez le câble ou le Wi-Fi de ce poste.",
        { InternetLabel: CaptivePortal } =>
            "Ce réseau demande souvent une ouverture de session dans le navigateur avant d'accéder à Internet.",
        { Internet: InternetState.LocalWithoutInternet } => "Le poste est sur un réseau local, sans accès Internet mesuré.",
        { Internet: InternetState.Limited } => "Les requêtes HTTPS sont bloquées. Le réseau peut filtrer l'accès.",
        { Wifi: WifiProtection.Open } => "Ce Wi-Fi n'est pas chiffré. Évitez d'y transmettre des informations personnelles.",
        { Wifi: WifiProtection.Wep } => "Le chiffrement WEP est obsolète. Préférez un réseau WPA2 ou WPA3.",
        { Wifi: WifiProtection.Wpa } => "Préférez un réseau WPA2 ou WPA3 si c'est possible.",
        { Wifi: WifiProtection.Unknown } => "Le chiffrement Wi-Fi n'a pas pu être lu sur ce poste.",
        { Vpn: VpnState.ActiveSplitTunnel } => "Une partie du trafic passe hors du VPN.",
        { Vpn: VpnState.InstalledInactive } => "Un client VPN est installé mais n'est pas actif.",
        { Vpn: VpnState.ActiveFullTunnel } =>
            "Le VPN actif oriente tout le trafic mesuré. La protection dépend aussi du fournisseur et du réseau.",
        _ => "État mesuré localement, en lecture seule. Ce n'est pas une garantie de sécurité globale."
    };

    private void Dispatch(Action action)
    {
        void Apply()
        {
            if (!_disposed)
                action();
        }

        if (_context is null || SynchronizationContext.Current == _context)
            Apply();
        else
            _context.Post(_ => Apply(), null);
    }

    private void SignalChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_disposed || _refreshDue.HasValue)
                return;

            _refreshDue = _clock.GetUtcNow() + _networkDebounce;
            _debounceTimer?.Change(_networkDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void RefreshAfterSignal()
    {
        lock (_gate)
        {
            if (_disposed || _refreshDue is not { } due)
                return;

            var remaining = due - _clock.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                _debounceTimer?.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            _refreshDue = null;
            RequestRefresh();
        }
    }

    private void RequestRefresh()
    {
        lock (_gate)
            if (!_disposed && _refresh.IsCompleted)
                _refresh = RefreshAsync(_lifetime.Token);
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        try
        {
            await _store.RefreshAsync(token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Keep the last snapshot until the next successful refresh.
        }
    }

    public void Dispose()
    {
        Task pending;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _store.Changed -= StoreChanged;
            _store.NotificationRequested -= Notify;
            _signal.Changed -= SignalChanged;
            _debounceTimer?.Dispose();
            _pollTimer?.Dispose();
            pending = _refresh;
        }

        _signal.Dispose();
        _lifetime.Cancel();
        _ = ReleaseLifetimeAsync(pending);
    }

    private async Task ReleaseLifetimeAsync(Task pending)
    {
        await pending.ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
