using System.Net;
using System.Reflection;
using Virgil.Contracts;
using Virgil.Core.Network;
using Virgil.Core.Resources;
using Virgil.Infrastructure.Windows.Network;
using Xunit;

namespace Virgil.Core.Tests;

public class LotReseauMoteurCoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluator_distinguishes_connected_single_target_failure_captive_portal_limited_and_no_network()
    {
        var evaluator = Core("ConnectionSecurityEvaluator");
        var adapter = Adapter(evaluator, "Ethernet");
        CheckInternet(evaluator, Reading([adapter]), "Connected", "Healthy", "Connecté");
        CheckInternet(evaluator, Reading([adapter], 1), "Connected", "Healthy", "Connecté (1 cible de vérification indisponible)");
        CheckInternet(evaluator, Reading([adapter], 0, captive: true), "LocalWithoutInternet", "Attention",
            "Réseau local sans Internet (portail captif probable)");
        CheckInternet(evaluator, Reading([adapter], 0), "Limited", "Attention", "Accès limité (requêtes HTTPS bloquées)");
        CheckInternet(evaluator, Reading([adapter], 0, dns: false), "LocalWithoutInternet", "Attention", "Réseau local sans Internet");
        CheckInternet(evaluator, Reading([], 0, dns: false), "NoNetwork", "Alert", "Aucun réseau détecté");
    }

    [Fact]
    public void Evaluator_distinguishes_vpn_full_tunnel_split_tunnel_inactive_and_virtual_adapters()
    {
        var evaluator = Core("ConnectionSecurityEvaluator");
        CheckVpn(evaluator, Reading([Adapter(evaluator, "NordLynx", defaultRoute: true)]), "ActiveFullTunnel", "Healthy",
            "Actif (NordVPN, tout le trafic)");
        CheckVpn(evaluator, Reading([Adapter(evaluator, "Wintun")]), "ActiveSplitTunnel", "Attention",
            "VPN actif (routage partiel, fournisseur non identifié)");
        CheckVpn(evaluator, Reading([Adapter(evaluator, "Wintun", defaultRoute: true)]), "ActiveFullTunnel", "Healthy",
            "VPN actif (fournisseur non identifié)");
        CheckVpn(evaluator, Reading([Adapter(evaluator, "Wintun", defaultRoute: true)], routes: false), "Unknown", "Unknown",
            "VPN détecté, routage non vérifiable");
        CheckVpn(evaluator, Reading([Adapter(evaluator, "Wintun", up: false)]), "InstalledInactive", "Attention", "Installé, inactif");
        CheckVpn(evaluator, Reading([Adapter(evaluator, "Ethernet")], clients: ["NordVPN"]), "InstalledInactive", "Attention", "Installé, inactif");
        CheckVpn(evaluator, Reading(new[] { "Hyper-V", "VMware", "vEthernet", "Teredo" }
            .Select(name => Adapter(evaluator, name, defaultRoute: true)).ToArray()), "Absent", "Healthy", "Absent");
    }

    [Fact]
    public void Evaluator_evaluates_wifi_protection_and_never_claims_totally_secure()
    {
        var evaluator = Core("ConnectionSecurityEvaluator");
        foreach (var (wifi, level, label) in new[]
        {
            ("Open", "Alert", "Wi-Fi ouvert (non chiffré)"),
            ("Wep", "Alert", "Wi-Fi WEP (chiffrement obsolète)"),
            ("Wpa", "Attention", "Wi-Fi WPA ancien (préférer WPA2/WPA3)"),
            ("Wpa2", "Healthy", "Wi-Fi chiffré (WPA2)"),
            ("Wpa3", "Healthy", "Wi-Fi chiffré (WPA3)"),
            ("Unknown", "Unknown", "Chiffrement Wi-Fi non lisible"),
            ("NotApplicable", "Healthy", "Connexion filaire (Wi-Fi inactif)")
        })
        {
            var snapshot = Evaluate(evaluator, Reading([Adapter(evaluator, "LAN", wireless: wifi != "NotApplicable")], wifi: wifi));
            Assert.Equal(wifi, Property(snapshot, "Wifi").ToString());
            Assert.Equal(level, Property(snapshot, "SecurityLevel").ToString());
            Assert.Equal(label, Property(snapshot, "SecurityLabel"));
            Assert.Equal("Mesure locale en lecture seule uniquement.", Property(snapshot, "ScopeNote"));
        }
    }

    [Fact]
    public async Task Store_debounces_brief_drops_and_notifies_only_after_confirmation_window()
    {
        var storeType = Core("ConnectionStatusStore");
        var evaluator = Core("ConnectionSecurityEvaluator");
        var clock = new StepClock();
        var reading = Reading([Adapter(evaluator, "Ethernet")]);
        using var store = CreateStore(storeType, clock, _ => Task.FromResult(reading));
        using var cancellation = new CancellationTokenSource();
        var notifications = new List<object>();
        var notificationEvent = Core("IConnectionStatusStore").GetEvent("NotificationRequested")!;
        var callback = new NotificationCollector(notifications);
        notificationEvent.AddEventHandler(store, Delegate.CreateDelegate(notificationEvent.EventHandlerType!, callback,
            typeof(NotificationCollector).GetMethod(nameof(NotificationCollector.Record))!));
        Assert.Equal("Connected", Property(await ReadAsync(store, "RefreshAsync", cancellation.Token), "Internet").ToString());
        Assert.Empty(notifications);

        reading = Reading([], 0, dns: false);
        Assert.Equal("Connected", Property(await ReadAsync(store, "RefreshAsync", cancellation.Token), "Internet").ToString());
        clock.Advance(4);
        reading = Reading([Adapter(evaluator, "Ethernet")]);
        Assert.Equal("Connected", Property(await ReadAsync(store, "RefreshAsync", cancellation.Token), "Internet").ToString());
        Assert.Empty(notifications);

        reading = Reading([], 0, dns: false);
        Assert.Equal("Connected", Property(await ReadAsync(store, "RefreshAsync", cancellation.Token), "Internet").ToString());
        clock.Advance(5);
        Assert.Equal("NoNetwork", Property(await ReadAsync(store, "RefreshAsync", cancellation.Token), "Internet").ToString());
        Assert.Equal("Perte de connexion réseau détectée.", Property(Assert.Single(notifications), "Message"));
    }

    [Fact]
    public async Task Store_serializes_concurrent_refreshes_and_probes_http_captive_portal_without_redirects()
    {
        var storeType = Core("ConnectionStatusStore");
        var evaluator = Core("ConnectionSecurityEvaluator");
        var reading = Reading([Adapter(evaluator, "Ethernet")]);
        var release = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var store = CreateStore(storeType, new StepClock(), _ =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            return release.Task;
        });
        using var cancellation = new CancellationTokenSource();
        var first = ReadAsync(store, "RefreshAsync", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = ReadAsync(store, "RefreshAsync", cancellation.Token);
        var concurrentCalls = Volatile.Read(ref calls);
        release.SetResult(reading);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, concurrentCalls);
        Assert.Equal(2, calls);

        foreach (var (status, expectedSuccesses, captive) in new[]
        {
            (HttpStatusCode.OK, 2, false),
            (HttpStatusCode.ServiceUnavailable, 1, false),
            (HttpStatusCode.Redirect, 1, true)
        })
        {
            using var client = new HttpClient(new ProbeHandler(status));
            var probe = Activator.CreateInstance(Infrastructure("WindowsConnectionSecurityProbe"), client)!;
            var result = await ReadAsync(probe, "ReadAsync", cancellation.Token);
            Assert.Equal(2, Property(result, "HttpsTargetsTotal"));
            Assert.Equal(expectedSuccesses, Property(result, "HttpsTargetsSucceeded"));
            Assert.Equal(captive, Property(result, "CaptivePortalSuspected"));
        }
    }

    [Fact]
    public void Native_wifi_security_mapper_classifies_auth_and_cipher_algorithms()
    {
        var mapper = Infrastructure("WindowsNetworkNativeProbe").GetMethod("MapWifiSecurity")!;
        foreach (var (enabled, auth, cipher, expected) in new[]
        {
            (false, 1u, 0u, "Open"), (true, 2u, 0x101u, "Wep"), (true, 4u, 2u, "Wpa"),
            (true, 7u, 4u, "Wpa2"), (true, 9u, 4u, "Wpa3"), (true, 999u, 4u, "Unknown")
        })
        {
            Assert.Equal(expected, mapper.Invoke(null, [enabled, auth, cipher])!.ToString());
        }
    }

    [Fact]
    public void Existing_network_status_reader_and_vpn_protection_policy_remain_intact()
    {
        Assert.IsAssignableFrom<INetworkStatusReader>(new WindowsNetworkStatusReader());
        Assert.Equal(ProcessResourceStatus.Protected, new ProcessProtectionPolicy().Evaluate(
            new ProcessObservation { ProcessId = 98765, Name = "nordvpn" }, 0).Status);
    }

    [Fact]
    public void Network_read_only_boundary_has_no_system_writes_elevation_or_ui_dependency()
    {
        var references = typeof(INetworkStatusReader).Assembly.GetReferencedAssemblies().Select(name => name.Name);
        Assert.DoesNotContain("Virgil.Infrastructure", references);
        Assert.DoesNotContain("Virgil.App", references);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Virgil.sln")) && root.Parent is { } parent)
            root = parent;
        Assert.True(File.Exists(Path.Combine(root.FullName, "Virgil.sln")));
        foreach (var folder in new[] { "src/Virgil.Core/Network", "src/Virgil.Infrastructure/Windows/Network" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root.FullName, folder), "*.cs"))
        {
            var source = File.ReadAllText(file);
            foreach (var forbidden in new[] { "Process.Start", "CancellationToken.None", "Registry.", "Elevation",
                "SetIpForward", "CreateIpForward", "DeleteIpForward", "WlanSet", "Set-Dns", "netsh", "Firewall" })
                Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static Type Core(string name) => RequireType(typeof(INetworkStatusReader).Assembly, "Virgil.Core.Network." + name);

    internal static Type Infrastructure(string name) => RequireType(typeof(WindowsNetworkStatusReader).Assembly,
        "Virgil.Infrastructure.Windows.Network." + name);

    private static Type RequireType(Assembly assembly, string name)
    {
        var type = assembly.GetType(name);
        Assert.NotNull(type);
        return type;
    }

    internal static object Evaluate(Type evaluator, object reading) => evaluator.GetMethod("Evaluate")!.Invoke(null, [reading, Start])!;

    internal static object Property(object instance, string name) => instance.GetType().GetProperty(name)!.GetValue(instance)!;

    internal static async Task<object> ReadAsync(object source, string method, CancellationToken token)
    {
        var task = (Task)source.GetType().GetMethod(method)!.Invoke(source, [token])!;
        await task.WaitAsync(TimeSpan.FromSeconds(10));
        return Property(task, "Result");
    }

    private static object Adapter(Type evaluator, string name, bool defaultRoute = false, bool up = true, bool wireless = false)
    {
        var identity = ((bool Known, string? Provider))evaluator.GetMethod("IdentifyVpnAdapter")!.Invoke(null, [name, "", true])!;
        return Activator.CreateInstance(Core("ConnectionAdapterObservation"), name, "", up, wireless, true, true, true,
            defaultRoute, identity.Known, identity.Provider)!;
    }

    private static object Reading(object[] adapters, int successful = 2, bool captive = false, bool dns = true,
        bool routes = true, string wifi = "NotApplicable", string[]? clients = null)
    {
        var array = Array.CreateInstance(Core("ConnectionAdapterObservation"), adapters.Length);
        adapters.CopyTo(array, 0);
        var type = Core("ConnectionProbeReading");
        var protection = Enum.Parse(type.GetProperty("WifiProtection")!.PropertyType, wifi);
        return Activator.CreateInstance(type, array, 2, successful, captive, dns, routes, protection, clients ?? [])!;
    }

    private static void CheckInternet(Type evaluator, object reading, string state, string level, string label)
    {
        var snapshot = Evaluate(evaluator, reading);
        Assert.Equal(state, Property(snapshot, "Internet").ToString());
        Assert.Equal(level, Property(snapshot, "InternetLevel").ToString());
        Assert.Equal(label, Property(snapshot, "InternetLabel"));
    }

    private static void CheckVpn(Type evaluator, object reading, string state, string level, string label)
    {
        var snapshot = Evaluate(evaluator, reading);
        Assert.Equal(state, Property(snapshot, "Vpn").ToString());
        Assert.Equal(level, Property(snapshot, "VpnLevel").ToString());
        Assert.Equal(label, Property(snapshot, "VpnLabel"));
    }

    private static IDisposable CreateStore(Type storeType, StepClock clock, Func<CancellationToken, Task<object>> read)
    {
        var proxy = (ProbeProxy)DispatchProxy.Create(Core("IConnectionStatusProbe"), typeof(ProbeProxy));
        proxy.Read = read;
        return (IDisposable)Activator.CreateInstance(storeType, proxy, clock, null, null)!;
    }

    public class ProbeProxy : DispatchProxy
    {
        public Func<CancellationToken, Task<object>> Read { get; set; } = null!;

        protected override object Invoke(MethodInfo? targetMethod, object?[]? args) =>
            typeof(ProbeProxy).GetMethod(nameof(ReadTyped), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(targetMethod!.ReturnType.GenericTypeArguments[0]).Invoke(this, args)!;

        private async Task<T> ReadTyped<T>(CancellationToken token) => (T)await Read(token).WaitAsync(TimeSpan.FromSeconds(10));
    }

    public sealed class NotificationCollector(List<object> notifications)
    {
        public void Record(object? sender, object notification) => notifications.Add(notification);
    }

    private sealed class StepClock : TimeProvider
    {
        private DateTimeOffset _now = Start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }

    internal sealed class ProbeHandler(HttpStatusCode firefoxStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var microsoft = request.RequestUri!.AbsoluteUri == "http://www.msftconnecttest.com/connecttest.txt";
            if (!microsoft)
                Assert.Equal("https://detectportal.firefox.com/success.txt", request.RequestUri.AbsoluteUri);
            var response = new HttpResponseMessage(microsoft ? HttpStatusCode.OK : firefoxStatus)
            {
                Content = new StringContent(microsoft ? "Microsoft Connect Test" : "success")
            };
            if (firefoxStatus == HttpStatusCode.Redirect && !microsoft)
                response.Headers.Location = new Uri("http://example.invalid/login");
            return Task.FromResult(response);
        }
    }
}
