using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.Core.Actions;
using Xunit;

namespace Virgil.Tests;

public sealed class ConfirmationCompositionRootTests
{
    [Fact]
    public void ApplicationCompositionResolvesInfrastructureLocationPort()
    {
        var services = new ServiceCollection().AddVirgilServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true
        });
        Assert.IsType<Virgil.Infrastructure.Applications.InstallationLocationService>(
            provider.GetRequiredService<Virgil.Core.Applications.IInstallationLocationService>());
        Assert.NotNull(provider.GetRequiredService<Virgil.App.ViewModels.ApplicationsViewModel>());
    }

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
        foreach (var type in new[] { typeof(Virgil.Core.Interventions.IInterventionExecutionService), typeof(Virgil.Core.Cleanup.ICleanupExecutionService) })
        {
            var service = provider.GetRequiredService(type);
            var gate = service.GetType().GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(gate);
            Assert.Same(authority, gate.GetValue(service));
        }
        foreach (var type in new[] { typeof(Virgil.App.ViewModels.InterventionsViewModel), typeof(Virgil.App.ViewModels.CleanupViewModel) })
        {
            var vm = provider.GetRequiredService(type);
            var issuer = type.GetField("_confirmationService", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(issuer);
            Assert.Same(authority, issuer.GetValue(vm));
            Assert.Same(vm, provider.GetRequiredService(type));
        }
        foreach (var type in new[] { typeof(ConfirmationService), typeof(IConfirmationService), typeof(IConfirmedPlanGate), typeof(TimeProvider) })
        {
            var descriptor = Assert.Single(services, d => d.ServiceType == type);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            Assert.NotNull(descriptor.ImplementationFactory);
        }
    }
}
