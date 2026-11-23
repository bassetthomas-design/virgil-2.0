using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.Core.Network;
using Virgil.Infrastructure.Windows.Network;
using Xunit;

namespace Virgil.Tests;

public sealed class LotReseauMoteurDiTests
{
    [Fact]
    public void Di_registers_connection_status_store_as_singleton_and_probe_as_transient()
    {
        var storeType = typeof(INetworkStatusReader).Assembly.GetType("Virgil.Core.Network.IConnectionStatusStore");
        Assert.NotNull(storeType);
        var probeType = typeof(INetworkStatusReader).Assembly.GetType("Virgil.Core.Network.IConnectionStatusProbe");
        Assert.NotNull(probeType);
        var windowsProbe = typeof(WindowsNetworkStatusReader).Assembly.GetType("Virgil.Infrastructure.Windows.Network.WindowsConnectionSecurityProbe");
        Assert.NotNull(windowsProbe);

        var services = new ServiceCollection().AddVirgilServices();
        var store = Assert.Single(services, descriptor => descriptor.ServiceType == storeType);
        var probe = Assert.Single(services, descriptor => descriptor.ServiceType == probeType);
        Assert.Equal(ServiceLifetime.Singleton, store.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, probe.Lifetime);
        Assert.NotNull(store.ImplementationFactory);
        Assert.NotNull(probe.ImplementationFactory);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.Same(provider.GetRequiredService(storeType), provider.GetRequiredService(storeType));
        var firstProbe = provider.GetRequiredService(probeType);
        Assert.IsType(windowsProbe, firstProbe);
        Assert.NotSame(firstProbe, provider.GetRequiredService(probeType));
    }
}
