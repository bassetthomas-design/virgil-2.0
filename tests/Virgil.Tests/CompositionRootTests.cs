using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.Core.Applications;
using Virgil.Core.Cleanup;
using Virgil.Core.Interventions;
using Virgil.Core.Processes;
using Virgil.Core.Reports;
using Virgil.Core.Resources;
using Virgil.Core.Scanning;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Processes;
using Xunit;

namespace Virgil.Tests;

public sealed class CompositionRootTests
{
    [Fact]
    public void ServicesResolveWithoutConstructingWpfViews()
    {
        using var provider = BuildProvider();
        Assert.IsType<SystemScanService>(provider.GetRequiredService<ISystemScanService>());
        Assert.IsType<WingetUpdateScanService>(provider.GetRequiredService<IUpdateScanService>());
        Assert.IsType<WingetUpdateExecutionService>(provider.GetRequiredService<IUpdateExecutionService>());
        Assert.IsType<ApplicationInventoryService>(provider.GetRequiredService<IApplicationInventoryService>());
        Assert.NotNull(provider.GetRequiredService<ApplicationUninstallService>());
        Assert.IsType<InterventionDiagnosticService>(provider.GetRequiredService<IInterventionDiagnosticService>());
        Assert.IsType<InterventionExecutionService>(provider.GetRequiredService<IInterventionExecutionService>());
        Assert.IsType<ResourceMonitoringService>(provider.GetRequiredService<IResourceMonitoringService>());
        Assert.IsType<ProcessActionService>(provider.GetRequiredService<IProcessActionService>());
        Assert.IsType<CleanupPreviewService>(provider.GetRequiredService<ICleanupPreviewService>());
        Assert.IsType<CleanupExecutionService>(provider.GetRequiredService<ICleanupExecutionService>());
        Assert.IsType<ReportHistoryService>(provider.GetRequiredService<IReportHistoryService>());
        Assert.IsType<ReportExportService>(provider.GetRequiredService<IReportExportService>());
    }

    [Fact]
    public void ProcessRunnerIsTheSingleInfrastructureSingleton()
    {
        var services = new ServiceCollection().AddVirgilServices();
        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProcessRunner));
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var runner = provider.GetRequiredService<IProcessRunner>();
        Assert.IsType<ProcessRunner>(runner);
        Assert.Same(runner, provider.GetRequiredService<IProcessRunner>());
    }

    [Fact]
    public void EveryCoreServiceIsTransientAndHasAnExplicitFactory()
    {
        var services = new ServiceCollection().AddVirgilServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType != typeof(IProcessRunner)))
        {
            Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
            Assert.NotNull(descriptor.ImplementationFactory);
            Assert.NotSame(provider.GetRequiredService(descriptor.ServiceType), provider.GetRequiredService(descriptor.ServiceType));
        }
    }

    private static ServiceProvider BuildProvider() => new ServiceCollection().AddVirgilServices()
        .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
}
