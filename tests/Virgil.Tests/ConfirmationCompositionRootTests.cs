using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.Core.Actions;
using Xunit;

namespace Virgil.Tests;

public sealed class ConfirmationCompositionRootTests
{
    [Fact]
    public void ApplicationCompositionRegistersOneAuthorityWithValidatedBuild()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true
        });
        var authority = provider.GetRequiredService<ConfirmationService>();
        Assert.Same(authority, provider.GetRequiredService<IConfirmationService>());
        Assert.Same(authority, provider.GetRequiredService<IConfirmedPlanGate>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        foreach (var type in new[] { typeof(ConfirmationService), typeof(IConfirmationService), typeof(IConfirmedPlanGate), typeof(TimeProvider) })
        {
            var descriptor = Assert.Single(services, d => d.ServiceType == type);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            Assert.NotNull(descriptor.ImplementationFactory);
        }
    }
}
