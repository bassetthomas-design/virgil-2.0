using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.Core.Network;
using Virgil.Infrastructure.Windows.Network;
using Xunit;

namespace Virgil.Tests;

public sealed class LotReseauBandeauTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly XName Name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
    private const string Scope = "Mesure locale en lecture seule uniquement.";
    private const string App = "src/Virgil.App/";
    private const string ViewModel = App + "ViewModels/ConnectionStatusViewModel.cs";
    private const string Signal = "src/Virgil.Core/Network/INetworkChangeSignal.cs";
    private const string WindowsSignal = "src/Virgil.Infrastructure/Windows/Network/WindowsNetworkChangeSignal.cs";

    [Fact]
    public void ViewModel_reads_store_snapshot_and_formats_french_badges_and_tooltip()
    {
        using var fixture = new Fixture();
        fixture.Start();
        Assert.Equal("Internet : État Internet indéterminé", fixture.Read<string>("InternetBadgeText"));
        Assert.Equal("VPN : État VPN indéterminé", fixture.Read<string>("VpnBadgeText"));
        Assert.Equal(string.Join(Environment.NewLine, "Internet : État Internet indéterminé",
            "VPN : État VPN indéterminé", Scope), fixture.Read<string>("ExplanationToolTip"));

        fixture.Store.Publish(Connected());
        Assert.Equal("Internet : Connecté", fixture.Read<string>("InternetBadgeText"));
        Assert.Equal("VPN : Absent", fixture.Read<string>("VpnBadgeText"));
        Assert.Equal(string.Join(Environment.NewLine, "Internet : Connecté", "VPN : Absent",
            "Wi-Fi chiffré (WPA2)", Scope), fixture.Read<string>("ExplanationToolTip"));
        Assert.Equal(ConnectionLevel.Healthy, fixture.Read<ConnectionLevel>("InternetLevel"));
        Assert.Equal(ConnectionLevel.Healthy, fixture.Read<ConnectionLevel>("VpnLevel"));
        foreach (var name in new[] { "InternetBadgeText", "VpnBadgeText", "ExplanationToolTip" })
        {
            Assert.DoesNotContain("totalement sécurisé", fixture.Read<string>(name), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("100 %", fixture.Read<string>(name), StringComparison.Ordinal);
        }

        fixture.Store.Publish(Offline());
        Assert.Equal("Internet : Aucun réseau détecté", fixture.Read<string>("InternetBadgeText"));
        Assert.Equal("VPN : Installé, inactif", fixture.Read<string>("VpnBadgeText"));
        Assert.Equal(ConnectionLevel.Alert, fixture.Read<ConnectionLevel>("InternetLevel"));
        Assert.Equal(ConnectionLevel.Attention, fixture.Read<ConnectionLevel>("VpnLevel"));
    }

    [Fact]
    public void ViewModel_subscribes_to_store_changes_and_unsubscribes_on_dispose()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Store.Publish(Connected());
        Assert.Equal("Internet : Connecté", fixture.Read<string>("InternetBadgeText"));

        fixture.Dispose();
        fixture.Store.Publish(Offline());
        fixture.Signal.Raise();
        fixture.Clock.Advance(31);
        Assert.Equal("Internet : Connecté", fixture.Read<string>("InternetBadgeText"));
        Assert.Equal(1, fixture.Store.RefreshCount);
        Assert.True(fixture.Signal.Disposed);
        Assert.False(fixture.Store.Disposed);
    }

    [Fact]
    public void ViewModel_debounces_network_change_refresh_and_polls_at_most_every_30_seconds()
    {
        using var fixture = new Fixture();
        fixture.Start();
        Assert.Equal(1, fixture.Store.RefreshCount);

        fixture.Signal.Raise();
        fixture.Signal.Raise();
        Assert.Equal(1, fixture.Store.RefreshCount);
        fixture.Clock.Advance(1);
        Assert.Equal(2, fixture.Store.RefreshCount);
        fixture.Clock.Advance(28);
        Assert.Equal(2, fixture.Store.RefreshCount);
        fixture.Clock.Advance(1);
        Assert.Equal(3, fixture.Store.RefreshCount);

        fixture.Dispose();
        fixture.Clock.Advance(30);
        Assert.Equal(3, fixture.Store.RefreshCount);
    }

    [Fact]
    public void ViewModel_forwards_store_notifications_and_open_network_command()
    {
        using var fixture = new Fixture();
        string? message = null;
        string? destination = null;
        fixture.Subscribe("VirgilMessageRequested", value => message = value);
        fixture.Subscribe("NavigationRequested", value => destination = value);
        fixture.Start();

        fixture.Store.Notify("Perte de connexion réseau détectée.");
        Assert.Equal("Perte de connexion réseau détectée.", message);
        fixture.Read<ICommand>("OpenNetworkCommand").Execute(null);
        Assert.Equal("Réseau", destination);
    }

    [Fact]
    public void HeaderPanel_keeps_existing_badges_and_hosts_permanent_clickable_connection_badges()
    {
        var window = XElement.Parse(ReadSource(App + "MainWindow.xaml"));
        Assert.Equal("1080", (string?)window.Attribute("MinWidth"));
        var header = Find(window, "HeaderPanel");
        var status = Find(header, "StatusText");
        var session = Find(header, "SessionTimeText");
        Find(header, "ReleasePanelHost");
        var host = Find(header, "ConnectionBadgesHost");

        var row = Assert.IsType<XElement>(status.Parent?.Parent);
        Assert.Same(row, host.Parent);
        Assert.Same(row, session.Parent?.Parent);
        Assert.Null(host.Attribute("Grid.Row"));
        Assert.Equal("Horizontal", (string?)host.Attribute("Orientation"));
        Assert.Equal("Center", (string?)host.Attribute("VerticalAlignment"));
        Assert.Equal(new[] { "SYSTÈME PRÊT", "SESSION LOCALE", "LOCAL", "PREVIEW" },
            row.Elements().Where(element => element != host).Select(element => (string?)element.Elements().Single().Attribute("Text")));
        Assert.Equal(new[] { "StatusText", "SessionTimeText", "ConnectionBadgesHost", "LOCAL", "PREVIEW" },
            row.Elements().Select(element => (string?)element.Attribute(Name)
                ?? (string?)element.Elements().Single().Attribute(Name)
                ?? (string?)element.Elements().Single().Attribute("Text")));

        AssertBadge(host, "Internet", "Internet : État Internet indéterminé");
        AssertBadge(host, "Vpn", "VPN : État VPN indéterminé");
        Assert.DoesNotContain(window.Descendants(), element => element.Name.LocalName == "ConnectionSecurityPanel");
    }

    [Fact]
    public void Di_registers_connection_status_viewmodel_as_singleton_and_wires_main_window()
    {
        var type = GetViewModelType();
        var signalType = GetSignalType();
        var windowsType = typeof(WindowsNetworkStatusReader).Assembly.GetType(
            "Virgil.Infrastructure.Windows.Network.WindowsNetworkChangeSignal");
        Assert.NotNull(windowsType);
        Assert.Contains(signalType, windowsType.GetInterfaces());

        var services = new ServiceCollection().AddVirgilViews();
        var descriptor = Assert.Single(services, item => item.ServiceType == type);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
        var composition = ReadSource(App + "Hosting/ServiceCollectionExtensions.cs");
        Assert.Matches(@"AddSingleton<ConnectionStatusViewModel>\([^;]*new WindowsNetworkChangeSignal\(\)", composition);
        Assert.Matches(@"new MainWindow\([^;]*GetRequiredService<ConnectionStatusViewModel>\(\)", composition);

        var main = ReadSource(App + "MainWindow.xaml.cs");
        Assert.Contains("ConnectionBadgesHost.DataContext = _connectionStatus;", main, StringComparison.Ordinal);
        Assert.Contains("VirgilMessageRequested += AppendVirgilMessage", main, StringComparison.Ordinal);
        Assert.Contains("_connectionStatus.Start()", main, StringComparison.Ordinal);
        Assert.Contains("_connectionStatus.Dispose()", main, StringComparison.Ordinal);
        var source = ReadSource(ViewModel);
        Assert.DoesNotContain("NetworkChange", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NetworkInterface", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_header_contracts_and_network_view_remain_intact()
    {
        AssertSourceBlob(App + "ViewModels/NetworkViewModel.cs", "031E4AFCFE5B9B8D09B1B073415D6866DFDA64A0");
        AssertSourceBlob(App + "Views/NetworkView.xaml", "03FE2E6642370AE41F5B97F962837412609E50D6");
        AssertSourceBlob(App + "Views/NetworkView.xaml.cs", "6AC957104BFFEDF37C7D554C27BABDEB054B272E");
        AssertSourceBlob("tests/Virgil.App.Tests/NetworkViewModelTests.cs", "74C335C2343CAB0D69E14CA33CFF472B76CFC10B");

        var main = ReadSource(App + "MainWindow.xaml.cs");
        Assert.Contains("AddOpacityAnimation(storyboard, HeaderPanel", main, StringComparison.Ordinal);
        Assert.Contains("StatusText.Text = \"RÉSEAU — LECTURE SEULE\"", main, StringComparison.Ordinal);
        Assert.Matches(@"ReleasePanelHost\.Children\.Add\(releaseView\);\s*ReleasePanelHost\.Children\.Add\(bugReportPanel\);", main);

        var xaml = ReadSource(App + "MainWindow.xaml");
        Assert.DoesNotContain("ConnectionSecurityPanel", xaml, StringComparison.Ordinal);
        var metrics = Find(XElement.Parse(xaml), "MetricsContainer");
        Assert.Equal("4", (string?)metrics.Attribute("Columns"));
        Assert.Equal(new[] { "MemoryMetricCard", "DiskMetricCard", "CleanupMetricCard", "PriorityMetricCard" },
            metrics.Elements().Where(element => !element.Name.LocalName.Contains('.'))
                .Select(element => (string?)element.Attribute(Name)));
    }

    [Fact]
    public void Bandeau_remains_read_only_without_system_writes()
    {
        foreach (var file in new[] { Signal, WindowsSignal, ViewModel })
        {
            var absolutePath = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), file);
            if (!File.Exists(absolutePath))
                continue;

            var source = File.ReadAllText(absolutePath);
            foreach (var forbidden in new[] { "netsh", "powershell", "Registry", "Firewall", "Dns", "ServiceController",
                         "SetRoute", "CreateIpForwardEntry", "DeleteIpForwardEntry", "ElevatedHelper", "runas",
                         "Process.Start", "CancellationToken.None", ".Wait()", ".Result", "GetAwaiter().GetResult()", "Thread.Sleep" })
                Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);

            if (file == Signal)
            {
                Assert.DoesNotContain("Virgil.Infrastructure", source, StringComparison.Ordinal);
                Assert.DoesNotContain("Virgil.App", source, StringComparison.Ordinal);
            }
        }
    }

    private static void AssertBadge(XElement host, string prefix, string fallback)
    {
        var badge = Find(host, prefix + "StatusBadge");
        Assert.Same(host, badge.Parent);
        Assert.Equal("{StaticResource VirgilBadge}", (string?)badge.Attribute("Style"));
        Assert.Null(badge.Attribute("Visibility"));
        var button = Find(badge, prefix + "StatusButton");
        Assert.Equal("{Binding OpenNetworkCommand}", (string?)button.Attribute("Command"));
        Assert.Equal("{Binding ExplanationToolTip}", (string?)button.Attribute("ToolTip"));
        Assert.Equal("True", (string?)button.Attribute("ToolTipService.ShowOnDisabled"));
        Assert.Equal("{Binding " + prefix + "BadgeText}", (string?)button.Attribute("AutomationProperties.Name"));
        var text = Assert.Single(button.Elements(), element => element.Name.LocalName == "TextBlock");
        Assert.Equal("{Binding " + prefix + "BadgeText, FallbackValue='" + fallback + "'}", (string?)text.Attribute("Text"));

        var triggers = text.Descendants().Where(element => element.Name.LocalName == "DataTrigger").ToArray();
        Assert.Equal(new[] { "Healthy", "Attention", "Alert", "Unknown" }, triggers.Select(trigger => (string?)trigger.Attribute("Value")));
        Assert.All(triggers, trigger => Assert.Equal("{Binding " + prefix + "Level}", (string?)trigger.Attribute("Binding")));
        Assert.Equal(new[] { "{DynamicResource App.SuccessBrush}", "{DynamicResource App.AccentBrush}",
            "{DynamicResource App.AlertBrush}", "{DynamicResource App.TextSecondaryBrush}" },
            triggers.Select(trigger => (string?)Assert.Single(trigger.Elements()).Attribute("Value")));
    }

    private static XElement Find(XElement root, string name) =>
        Assert.Single(root.Descendants(), element => (string?)element.Attribute(Name) == name);

    private static string ReadSource(string path) => File.ReadAllText(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), path));

    private static void AssertSourceBlob(string path, string expected)
    {
        var bytes = File.ReadAllBytes(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), path));
        var content = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal));
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(header);
        hash.AppendData(content);
        Assert.Equal(expected, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static Type GetViewModelType()
    {
        var type = typeof(ServiceCollectionExtensions).Assembly.GetType("Virgil.App.ViewModels.ConnectionStatusViewModel");
        Assert.NotNull(type);
        return type;
    }

    private static Type GetSignalType()
    {
        var type = typeof(IConnectionStatusStore).Assembly.GetType("Virgil.Core.Network.INetworkChangeSignal");
        Assert.NotNull(type);
        return type;
    }

    private static ConnectionSecuritySnapshot Connected() => new(InternetState.Connected, ConnectionLevel.Healthy,
        "Connecté", VpnState.Absent, ConnectionLevel.Healthy, "Absent", null, WifiProtection.Wpa2,
        ConnectionLevel.Healthy, "Wi-Fi chiffré (WPA2)", Scope, Start);

    private static ConnectionSecuritySnapshot Offline() => Connected() with
    {
        Internet = InternetState.NoNetwork, InternetLevel = ConnectionLevel.Alert, InternetLabel = "Aucun réseau détecté",
        Vpn = VpnState.InstalledInactive, VpnLevel = ConnectionLevel.Attention, VpnLabel = "Installé, inactif"
    };

    private sealed class Fixture : IDisposable
    {
        private readonly Type _type;
        private readonly object _viewModel;
        private bool _disposed;

        public Fixture()
        {
            _type = GetViewModelType();
            var signalType = GetSignalType();
            var signal = DispatchProxy.Create(signalType, typeof(SignalProxy));
            Signal = (SignalProxy)signal;
            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                _viewModel = Assert.IsAssignableFrom<IDisposable>(
                    Activator.CreateInstance(_type, Store, Clock, signal, null, null));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        public FakeStore Store { get; } = new();
        public StepClock Clock { get; } = new();
        public SignalProxy Signal { get; }

        public void Start() => _type.GetMethod("Start")!.Invoke(_viewModel, null);
        public T Read<T>(string name) => Assert.IsAssignableFrom<T>(_type.GetProperty(name)!.GetValue(_viewModel));
        public void Subscribe(string name, Action<string> handler) => _type.GetEvent(name)!.AddEventHandler(_viewModel, handler);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ((IDisposable)_viewModel).Dispose();
        }
    }

    public class SignalProxy : DispatchProxy
    {
        private EventHandler? _changed;
        public bool Disposed { get; private set; }

        public void Raise() => _changed?.Invoke(this, EventArgs.Empty);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "add_Changed":
                    _changed += Assert.IsType<EventHandler>(args![0]);
                    break;
                case "remove_Changed":
                    _changed -= Assert.IsType<EventHandler>(args![0]);
                    break;
                case "Dispose":
                    Disposed = true;
                    break;
                default:
                    throw new InvalidOperationException(targetMethod?.Name);
            }

            return null;
        }
    }

    private sealed class FakeStore : IConnectionStatusStore
    {
        public ConnectionSecuritySnapshot? Current { get; private set; }
        public int RefreshCount { get; private set; }
        public bool Disposed { get; private set; }
        public event EventHandler? Changed;
        public event EventHandler<ConnectionNotification>? NotificationRequested;

        public Task<ConnectionSecuritySnapshot> RefreshAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            return Task.FromResult(Current ?? Connected());
        }

        public void Publish(ConnectionSecuritySnapshot snapshot)
        {
            Current = snapshot;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Notify(string message) =>
            NotificationRequested?.Invoke(this, new ConnectionNotification("test", message, ConnectionLevel.Alert, Start));

        public void Dispose() => Disposed = true;
    }

    private sealed class StepClock : TimeProvider
    {
        private readonly List<StepTimer> _timers = [];
        private DateTimeOffset _now = Start;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new StepTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(int seconds)
        {
            _now = _now.AddSeconds(seconds);
            foreach (var timer in _timers.ToArray())
                timer.FireIfDue();
        }

        private sealed class StepTimer(StepClock clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _dueAt;
            private TimeSpan _period;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed)
                    return false;

                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                _period = period;
                return true;
            }

            public void FireIfDue()
            {
                if (_dueAt is not { } dueAt || dueAt > clock._now)
                    return;

                Change(_period == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : _period, _period);
                callback(state);
            }

            public void Dispose()
            {
                _disposed = true;
                _dueAt = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
