using Xunit;
using static Virgil.Core.Tests.LotReseauMoteurCoreTests;

namespace Virgil.Core.Tests;

public sealed class LotReseauMoteurWindowsTests
{
    [Fact]
    [Trait("Category", "Windows")]
    public async Task Windows_read_only_probe_reads_network_state_and_routes_without_elevation()
    {
        var native = LotReseauMoteurCoreTests.Infrastructure("WindowsNetworkNativeProbe");
        var routes = ((bool Available, IReadOnlySet<int> Indices))native.GetMethod("ReadDefaultRouteInterfaces")!.Invoke(null, null)!;
        Assert.True(routes.Available);
        using var client = new HttpClient(new ProbeHandler());
        using var cancellation = new CancellationTokenSource();
        var probe = Activator.CreateInstance(LotReseauMoteurCoreTests.Infrastructure("WindowsConnectionSecurityProbe"), client)!;
        var reading = await ReadAsync(probe, "ReadAsync", cancellation.Token);
        Assert.Equal(true, Property(reading, "RoutesAvailable"));
        Assert.Equal(2, Property(reading, "HttpsTargetsSucceeded"));
        var snapshot = Evaluate(LotReseauMoteurCoreTests.Core("ConnectionSecurityEvaluator"), reading);
        Assert.Equal("Mesure locale en lecture seule uniquement.", Property(snapshot, "ScopeNote"));
    }
}
