using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Core.Network;

namespace Virgil.App.ViewModels;

public sealed partial class ConnectionStatusViewModel : ObservableObject, IDisposable
{
    private const string InitialInternet = "Internet : État Internet indéterminé";
    private const string InitialVpn = "VPN : État VPN indéterminé";
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

    public event Action<string>? VirgilMessageRequested;
    public event Action<string>? NavigationRequested;

    [RelayCommand]
    private void OpenNetwork() => NavigationRequested?.Invoke("Réseau");

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
    }

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
