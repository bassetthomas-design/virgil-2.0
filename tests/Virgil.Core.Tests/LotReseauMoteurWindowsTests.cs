using Xunit;
using static Virgil.Core.Tests.LotReseauMoteurCoreTests;

namespace Virgil.Core.Tests;

public sealed class LotReseauMoteurWindowsTests
{
    [Fact]
    [Trait("Category", "Windows")]
    public async Task Windows_read_only_probe_reads_network_state_and_routes_without_elevation()
    {
        var native = Infrastructure("WindowsNetworkNativeProbe");
        var routes = ((bool Available, IReadOnlySet<int> Indices))native.GetMethod("ReadDefaultRouteInterfaces")!.Invoke(null, null)!;
        Assert.True(routes.Available);
        using var client = new HttpClient(new ProbeHandler());
        using var cancellation = new CancellationTokenSource();
        var probe = Activator.CreateInstance(Infrastructure("WindowsConnectionSecurityProbe"), client)!;
        var reading = await ReadAsync(probe, "ReadAsync", cancellation.Token);
        Assert.Equal(true, Property(reading, "RoutesAvailable"));
        Assert.Equal(2, Property(reading, "HttpsTargetsSucceeded"));
        var snapshot = Evaluate(Core("ConnectionSecurityEvaluator"), reading);
        Assert.Equal("Mesure locale en lecture seule uniquement.", Property(snapshot, "ScopeNote"));
    }
}
