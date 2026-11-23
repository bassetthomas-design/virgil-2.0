using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using Virgil.App.ViewModels;
using Virgil.Core.Network;
using Xunit;

namespace Virgil.Tests;

public sealed class LotReseauAccueilTests
{
    private const string App = "src/Virgil.App/";
    private const string Panel = App + "Controls/ConnectionSecurityPanel.xaml";
    private const string Scope = "Mesure locale en lecture seule uniquement.";
    private const string Captive = "Réseau local sans Internet (portail captif probable)";
    private const string InitialAdvice = "Aucune mesure récente : utilisez Actualiser pour lire l'état actuel.";
    private static readonly XName Name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
    private static readonly DateTimeOffset Measurement = new(2026, 11, 23, 9, 5, 0, TimeSpan.Zero);

    [Fact]
    public void ViewModel_formats_panel_texts_advice_and_never_claims_total_security()
    {
        using var fixture = new Fixture();
        AssertTexts(fixture, "État Internet indéterminé", "État VPN indéterminé",
            "Chiffrement Wi-Fi non lisible", "", "",
            "État Internet indéterminé · État VPN indéterminé · Chiffrement Wi-Fi non lisible",
            InitialAdvice, "", ConnectionLevel.Unknown);

        fixture.ViewModel.Start();
        var connected = Connected();
        var inactive = connected with { Vpn = VpnState.InstalledInactive, VpnLabel = "Installé, inactif" };
        var full = connected with
        {
            Vpn = VpnState.ActiveFullTunnel, VpnProvider = "Mullvad", VpnLabel = "Actif (Mullvad, tout le trafic)"
        };
        var split = connected with
        {
            Vpn = VpnState.ActiveSplitTunnel, VpnLabel = "VPN actif (routage partiel, fournisseur non identifié)",
            SecurityLabel = "Wi-Fi chiffré (WPA2) · VPN en routage partiel", SecurityLevel = ConnectionLevel.Attention
        };

        foreach (var (snapshot, advice) in new[]
        {
            (connected, "État mesuré localement, en lecture seule. Ce n'est pas une garantie de sécurité globale."),
            (inactive with { Internet = InternetState.NoNetwork, InternetLabel = "Aucun réseau détecté" },
                "Vérifiez le câble ou le Wi-Fi de ce poste."),
            (inactive, "Un client VPN est installé mais n'est pas actif."),
            (connected with { Internet = InternetState.LocalWithoutInternet, InternetLabel = Captive },
                "Ce réseau demande souvent une ouverture de session dans le navigateur avant d'accéder à Internet."),
            (connected with { Internet = InternetState.LocalWithoutInternet, InternetLabel = "Réseau local sans Internet" },
                "Le poste est sur un réseau local, sans accès Internet mesuré."),
            (connected with { Internet = InternetState.Limited, InternetLabel = "Accès limité (requêtes HTTPS bloquées)" },
                "Les requêtes HTTPS sont bloquées. Le réseau peut filtrer l'accès."),
            (full, "Le VPN actif oriente tout le trafic mesuré. La protection dépend aussi du fournisseur et du réseau."),
            (split, "Une partie du trafic passe hors du VPN."),
            (split with { Wifi = WifiProtection.Open, SecurityLabel = "Wi-Fi ouvert (non chiffré)", SecurityLevel = ConnectionLevel.Alert },
                "Ce Wi-Fi n'est pas chiffré. Évitez d'y transmettre des informations personnelles."),
            (full with { Wifi = WifiProtection.Wep, SecurityLabel = "Wi-Fi WEP (chiffrement obsolète)", SecurityLevel = ConnectionLevel.Alert },
                "Le chiffrement WEP est obsolète. Préférez un réseau WPA2 ou WPA3."),
            (connected with { Wifi = WifiProtection.Wpa, SecurityLabel = "Wi-Fi WPA ancien (préférer WPA2/WPA3)", SecurityLevel = ConnectionLevel.Attention },
                "Préférez un réseau WPA2 ou WPA3 si c'est possible."),
            (connected with { Wifi = WifiProtection.Unknown, SecurityLabel = "Chiffrement Wi-Fi non lisible", SecurityLevel = ConnectionLevel.Unknown },
                "Le chiffrement Wi-Fi n'a pas pu être lu sur ce poste.")
        })
        {
            fixture.Store.Publish(snapshot);
            Assert.Equal(advice, fixture.Read<string>("AdviceText"));
            Assert.Equal(snapshot.InternetLabel, fixture.Read<string>("InternetStatusText"));
            Assert.Equal(snapshot.VpnLabel, fixture.Read<string>("VpnStatusText"));
            Assert.Equal(snapshot.SecurityLabel, fixture.Read<string>("WifiProtectionText"));
            Assert.Equal("Internet : " + snapshot.InternetLabel, fixture.ViewModel.InternetBadgeText);
            Assert.Equal("VPN : " + snapshot.VpnLabel, fixture.ViewModel.VpnBadgeText);
            AssertNoSecurityClaim(fixture);
        }

        fixture.Store.Publish(connected with { Internet = InternetState.LocalWithoutInternet, InternetLabel = Captive });
        Assert.Equal("Réseau local sans Internet (portail captif probable)", fixture.Read<string>("CaptivePortalText"));
        fixture.Store.Publish(full);
        AssertTexts(fixture, "Connecté", "Actif (Mullvad, tout le trafic)", "Wi-Fi chiffré (WPA2)", "", "Mullvad",
            "Connecté · Actif (Mullvad, tout le trafic) · Wi-Fi chiffré (WPA2)",
            "Le VPN actif oriente tout le trafic mesuré. La protection dépend aussi du fournisseur et du réseau.",
            "Mesuré à 11:05", ConnectionLevel.Healthy);
        fixture.Store.Publish(split);
        Assert.Equal("fournisseur non identifié", fixture.Read<string>("VpnProviderText"));
        fixture.Store.Publish(connected with { Vpn = VpnState.Unknown, VpnLabel = "VPN détecté, routage non vérifiable" });
        Assert.Equal("fournisseur non identifié", fixture.Read<string>("VpnProviderText"));
        fixture.Store.Publish(connected with
        {
            Vpn = VpnState.Unknown, VpnLabel = "VPN détecté (Mullvad), routage non vérifiable", VpnProvider = "Mullvad"
        });
        Assert.Equal("Mullvad", fixture.Read<string>("VpnProviderText"));
        fixture.Store.Publish(inactive);
        Assert.Equal("", fixture.Read<string>("VpnProviderText"));
    }

    [Fact]
    public void ViewModel_updates_panel_with_store_and_stops_after_dispose()
    {
        using var fixture = new Fixture();
        fixture.ViewModel.Start();
        Assert.Equal("État Internet indéterminé", fixture.Read<string>("InternetStatusText"));
        fixture.Store.Publish(Connected());
        Assert.Equal("Connecté", fixture.Read<string>("InternetStatusText"));
        Assert.Equal("Absent", fixture.Read<string>("VpnStatusText"));
        Assert.Equal("Wi-Fi chiffré (WPA2)", fixture.Read<string>("WifiProtectionText"));
        Assert.Equal(ConnectionLevel.Healthy, fixture.Read<ConnectionLevel>("SecurityLevel"));
        Assert.Equal("Mesuré à 11:05", fixture.Read<string>("CheckedAtText"));

        fixture.ViewModel.Dispose();
        fixture.Store.Publish(Connected() with
        {
            InternetLabel = Captive, VpnLabel = "Installé, inactif", SecurityLabel = "Chiffrement Wi-Fi non lisible",
            SecurityLevel = ConnectionLevel.Unknown, CheckedAtUtc = Measurement.AddHours(1)
        });
        Assert.Equal("Connecté", fixture.Read<string>("InternetStatusText"));
        Assert.Equal("Absent", fixture.Read<string>("VpnStatusText"));
        Assert.Equal("Wi-Fi chiffré (WPA2)", fixture.Read<string>("WifiProtectionText"));
        Assert.Equal(ConnectionLevel.Healthy, fixture.Read<ConnectionLevel>("SecurityLevel"));
        Assert.Equal("Mesuré à 11:05", fixture.Read<string>("CheckedAtText"));
    }

    [Fact]
    public async Task Actualiser_command_refreshes_the_store_and_open_network_command_stays_shared()
    {
        using var fixture = new Fixture();
        fixture.ViewModel.Start();
        Assert.Equal(1, fixture.Store.RefreshCount);
        var command = fixture.Read<IAsyncRelayCommand>("ActualiserCommand");
        command.Execute(null);
        var refresh = command.ExecutionTask;
        Assert.NotNull(refresh);
        await refresh.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, fixture.Store.RefreshCount);

        var navigation = new List<string>();
        fixture.ViewModel.NavigationRequested += navigation.Add;
        fixture.ViewModel.OpenNetworkCommand.Execute(null);
        Assert.Equal(new[] { "Réseau" }, navigation);

        fixture.ViewModel.Dispose();
        await command.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, fixture.Store.RefreshCount);
    }

    [Fact]
    public void Home_dashboard_hosts_connection_security_panel_beside_priorities_without_a_fifth_tile()
    {
        var window = XElement.Parse(ReadSource(App + "MainWindow.xaml"));
        var home = Find(window, "HomeContentGrid");
        var panel = Find(home, "ConnectionSecurityPanel");
        var priorities = Find(home, "PrioritiesPanel");
        var row = panel.Parent;
        Assert.NotNull(row);
        Assert.Same(row, priorities.Parent);
        Assert.Equal("ConnectionSecurityPanel", panel.Name.LocalName);
        Assert.Equal("Grid", row.Name.LocalName);
        Assert.Equal("3", (string?)row.Attribute("Grid.Row"));
        Assert.Equal("0", (string?)priorities.Attribute("Grid.Column"));
        Assert.Equal("1", (string?)panel.Attribute("Grid.Column"));
        Assert.Equal(new[] { "*", "*" }, row.Descendants().Where(element => element.Name.LocalName == "ColumnDefinition")
            .Select(element => (string?)element.Attribute("Width")));
        Assert.Null(panel.Attribute("Visibility"));

        var metrics = Find(window, "MetricsContainer");
        Assert.Equal("4", (string?)metrics.Attribute("Columns"));
        Assert.Equal(new[] { "MemoryMetricCard", "DiskMetricCard", "CleanupMetricCard", "PriorityMetricCard" },
            metrics.Elements().Where(element => !element.Name.LocalName.Contains('.'))
                .Select(element => (string?)element.Attribute(Name)));

        var control = XElement.Parse(ReadSource(Panel));
        Assert.Null(control.Attribute("Visibility"));
        var root = Assert.Single(control.Elements(), element => element.Name.LocalName == "StackPanel");
        Assert.Equal("ConnectionSecurityPanel", (string?)root.Attribute("AutomationProperties.AutomationId"));
        Assert.Equal("18,16,0,0", (string?)root.Attribute("Margin"));
        var text = root.Descendants().Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text")).ToArray();
        Assert.Contains("CONNEXION ET SÉCURITÉ RÉSEAU", text);
        Assert.Contains("Internet", text);
        Assert.Contains("VPN", text);
        Assert.Contains("Protection Wi-Fi", text);
        foreach (var (property, fallback) in new[]
        {
            ("InternetStatusText", "État Internet indéterminé"),
            ("VpnStatusText", "État VPN indéterminé"),
            ("WifiProtectionText", "Chiffrement Wi-Fi non lisible"),
            ("ExplanationText", "État Internet indéterminé · État VPN indéterminé · Chiffrement Wi-Fi non lisible"),
            ("AdviceText", @"Aucune mesure récente : utilisez Actualiser pour lire l\'état actuel."),
            ("ScopeNoteText", Scope)
        })
            Assert.Contains("{Binding " + property + ", FallbackValue='" + fallback + "'}", text);
        Assert.Contains("{Binding CaptivePortalText}", text);

        var buttons = root.Descendants().Where(element => element.Name.LocalName == "Button").ToArray();
        Assert.Equal(new[] { "Actualiser", "Voir l'écran Réseau" }, buttons.Select(button => (string?)button.Attribute("Content")));
        Assert.Equal(new[] { "Actualiser", "Voir l'écran Réseau" },
            buttons.Select(button => (string?)button.Attribute("AutomationProperties.Name")));
        Assert.Equal(new[] { "{Binding ActualiserCommand}", "{Binding OpenNetworkCommand}" },
            buttons.Select(button => (string?)button.Attribute("Command")));
        Assert.All(buttons, button => Assert.Null(button.Attribute("Click")));
        var details = Assert.Single(root.Descendants(), element => element.Name.LocalName == "Expander");
        Assert.Equal("Détails", (string?)details.Attribute("Header"));
        Assert.False(bool.TryParse((string?)details.Attribute("IsExpanded"), out var expanded) && expanded);
        var style = Assert.Single(details.Descendants(), element => element.Name.LocalName == "Style");
        Assert.Equal("{StaticResource {x:Type Expander}}", (string?)style.Attribute("BasedOn"));
        var detailsText = details.Descendants().Select(element => (string?)element.Attribute("Text"));
        Assert.Contains("{Binding CheckedAtText}", detailsText);
        Assert.Contains("{Binding ScopeNoteText, FallbackValue='Mesure locale en lecture seule uniquement.'}", detailsText);
        Assert.Contains("{Binding VpnProviderText}", detailsText);
    }

    [Fact]
    public void Main_window_binds_the_existing_connection_status_viewmodel_to_the_panel()
    {
        Assert.Contains("ConnectionSecurityPanel.DataContext = _connectionStatus;",
            ReadSource(App + "MainWindow.xaml.cs"), StringComparison.Ordinal);
        var composition = ReadSource(App + "Hosting/ServiceCollectionExtensions.cs");
        Assert.DoesNotContain("ConnectionSecurityPanel", composition, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ConnectionStatusViewModel>", composition, StringComparison.Ordinal);
        Assert.NotNull(typeof(ConnectionStatusViewModel).Assembly.GetType("Virgil.App.Controls.ConnectionSecurityPanel"));
    }

    [Fact]
    public void Existing_network_view_priorities_and_header_contracts_remain_intact()
    {
        AssertSourceBlob(App + "ViewModels/NetworkViewModel.cs", "031E4AFCFE5B9B8D09B1B073415D6866DFDA64A0");
        AssertSourceBlob(App + "Views/NetworkView.xaml", "03FE2E6642370AE41F5B97F962837412609E50D6");
        AssertSourceBlob(App + "Views/NetworkView.xaml.cs", "6AC957104BFFEDF37C7D554C27BABDEB054B272E");
        AssertSourceBlob("tests/Virgil.App.Tests/NetworkViewModelTests.cs", "74C335C2343CAB0D69E14CA33CFF472B76CFC10B");

        var window = XElement.Parse(ReadSource(App + "MainWindow.xaml"));
        Assert.Equal("1080", (string?)window.Attribute("MinWidth"));
        Assert.Equal("4", (string?)Find(window, "MetricsContainer").Attribute("Columns"));
        Find(window, "ConnectionBadgesHost");
        Find(window, "PrioritiesPanel");
        var main = ReadSource(App + "MainWindow.xaml.cs");
        Assert.Contains("PriorityTile.DataContext = HomePriorities;", main, StringComparison.Ordinal);
        Assert.Contains("PrioritiesPanel.BringIntoView()", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Panel_remains_read_only_without_system_writes()
    {
        AssertReadOnly(ReadSource(App + "ViewModels/ConnectionStatusViewModel.cs"));
        foreach (var file in new[] { Panel, Panel + ".cs" })
        {
            var path = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), file);
            if (File.Exists(path))
                AssertReadOnly(File.ReadAllText(path));
        }
    }

    private static void AssertReadOnly(string source)
    {
        foreach (var forbidden in new[] { "netsh", "powershell", "Registry", "Firewall", "Dns", "ServiceController",
                     "SetRoute", "CreateIpForwardEntry", "DeleteIpForwardEntry", "ElevatedHelper", "runas",
                     "Process.Start", "CancellationToken.None", ".Wait()", ".Result", "GetAwaiter().GetResult()", "Thread.Sleep" })
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertTexts(Fixture fixture, string internet, string vpn, string wifi, string captive,
        string provider, string explanation, string advice, string checkedAt, ConnectionLevel level)
    {
        Assert.Equal(internet, fixture.Read<string>("InternetStatusText"));
        Assert.Equal(vpn, fixture.Read<string>("VpnStatusText"));
        Assert.Equal(wifi, fixture.Read<string>("WifiProtectionText"));
        Assert.Equal(captive, fixture.Read<string>("CaptivePortalText"));
        Assert.Equal(provider, fixture.Read<string>("VpnProviderText"));
        Assert.Equal(explanation, fixture.Read<string>("ExplanationText"));
        Assert.Equal(advice, fixture.Read<string>("AdviceText"));
        Assert.Equal(Scope, fixture.Read<string>("ScopeNoteText"));
        Assert.Equal(checkedAt, fixture.Read<string>("CheckedAtText"));
        Assert.Equal(level, fixture.Read<ConnectionLevel>("SecurityLevel"));
        AssertNoSecurityClaim(fixture);
    }

    private static void AssertNoSecurityClaim(Fixture fixture)
    {
        foreach (var property in new[] { "InternetStatusText", "VpnStatusText", "WifiProtectionText",
                     "CaptivePortalText", "ExplanationText", "AdviceText", "ScopeNoteText", "CheckedAtText", "VpnProviderText" })
        {
            var value = fixture.Read<string>(property);
            Assert.DoesNotContain("totalement sécurisé", value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("100 %", value, StringComparison.Ordinal);
        }
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

    private static ConnectionSecuritySnapshot Connected() => new(InternetState.Connected, ConnectionLevel.Healthy,
        "Connecté", VpnState.Absent, ConnectionLevel.Healthy, "Absent", null, WifiProtection.Wpa2,
        ConnectionLevel.Healthy, "Wi-Fi chiffré (WPA2)", Scope, Measurement);

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                ViewModel = new ConnectionStatusViewModel(Store, new LocalClock(), new FakeSignal());
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        public FakeStore Store { get; } = new();
        public ConnectionStatusViewModel ViewModel { get; }

        public T Read<T>(string name)
        {
            var property = typeof(ConnectionStatusViewModel).GetProperty(name);
            Assert.NotNull(property);
            return Assert.IsAssignableFrom<T>(property.GetValue(ViewModel));
        }

        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class LocalClock : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Lot7c", TimeSpan.FromHours(2), "Lot7c", "Lot7c");

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            base.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private sealed class FakeSignal : INetworkChangeSignal
    {
        public event EventHandler? Changed { add { } remove { } }
        public void Dispose() { }
    }

    private sealed class FakeStore : IConnectionStatusStore
    {
        public ConnectionSecuritySnapshot? Current { get; private set; }
        public int RefreshCount { get; private set; }
        public event EventHandler? Changed;
        public event EventHandler<ConnectionNotification>? NotificationRequested { add { } remove { } }

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

        public void Dispose() { }
    }
}
