using Virgil.Core.Actions;
using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.App.ViewModels;
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
    [Trait("Category", "Windows")]
    public async Task MaintenanceModuleAndEveryExecutableCleanupActionResolveWithValidatedComposition()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var module = Assert.Single(provider.GetServices<Virgil.Core.Actions.IMaintenanceModule>());
        Assert.IsType<CleanupMaintenanceModule>(module);
        Assert.Equal("cleanup", module.Id);
        Assert.Equal("Nettoyage", module.Title);
        Assert.IsType<Virgil.Infrastructure.Windows.CleanupDisk>(provider.GetRequiredService<ICleanupDisk>());
        var expected = CleanupZoneCatalog.CreateDefault().Where(zone => zone.IsExecutable &&
            zone.Classification is Virgil.Contracts.CleanupClassification.Cleanable or Virgil.Contracts.CleanupClassification.AdvancedCleanable)
            .Select(zone => zone.Id).OrderBy(id => id).ToArray();
        var actions = provider.GetServices<Virgil.Core.Actions.IMaintenanceAction>().ToArray();
        Assert.Equal(expected, actions.Cast<CleanupMaintenanceAction>().Select(action => action.ZoneId).OrderBy(id => id));
        Assert.Equal(expected, provider.GetServices<CleanupMaintenanceAction>().Select(action => action.ZoneId).OrderBy(id => id));
        Assert.Equal(actions.Length, actions.Select(action => action.Id).Distinct().Count());
        foreach (var action in actions)
        {
            var preview = await action.PreviewAsync(CancellationToken.None);
            Assert.Equal(action.Id, preview.ActionId);
            Assert.Equal(action.RiskLevel, preview.RiskLevel);
        }
    }

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
    [Trait("Category", "Windows")]
    public void ElevatedRequestStoreUsesInjectedInfrastructurePorts()
    {
        var root = new Virgil.Infrastructure.Windows.ElevatedProtocolRoot();
        var files = new Virgil.Infrastructure.Windows.ElevatedProtocolFiles();
        var services = new ServiceCollection().AddVirgilServices();
        services.AddTransient<IElevatedProtocolRoot>(_ => root);
        services.AddTransient<IElevatedProtocolFiles>(_ => files);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var store = provider.GetRequiredService<ElevatedHelperRequestStore>();
        var fields = typeof(ElevatedHelperRequestStore).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.Same(root, fields.Single(field => field.FieldType == typeof(IElevatedProtocolRoot)).GetValue(store));
        Assert.Same(files, fields.Single(field => field.FieldType == typeof(IElevatedProtocolFiles)).GetValue(store));
        Assert.Same(root.PathGuard, fields.Single(field => field.FieldType == typeof(Virgil.Contracts.Elevated.ElevatedPathGuard)).GetValue(store));
    }

    [Fact]
    public void ApplicationsServiceUsesTheSharedConfirmationGate()
    {
        var services = new ServiceCollection().AddVirgilServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var service = provider.GetRequiredService<ApplicationUninstallService>();
        var field = typeof(ApplicationUninstallService).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(value => value.FieldType == typeof(IConfirmedPlanGate));
        Assert.Same(provider.GetRequiredService<IConfirmationService>(), field.GetValue(service));
        Assert.Same(provider.GetRequiredService<IConfirmedPlanGate>(), field.GetValue(service));
    }

    [Theory]
    [InlineData(typeof(IProcessActionService))]
    [InlineData(typeof(IUpdateExecutionService))]
    public void ResourceAndUpdateServicesUseSharedGate(Type serviceType)
    {
        using var provider = BuildProvider();
        var service = provider.GetRequiredService(serviceType);
        var field = service.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(value => value.FieldType == typeof(IConfirmedPlanGate));
        Assert.Same(provider.GetRequiredService<IConfirmationService>(), field.GetValue(service));
        Assert.Same(provider.GetRequiredService<IConfirmedPlanGate>(), field.GetValue(service));
    }

    [Theory]
    [Trait("Category", "Windows")]
    [InlineData(typeof(ResourcesViewModel))]
    [InlineData(typeof(UpdatesViewModel))]
    public void ResourceAndUpdateViewModelsUseSharedIssuer(Type viewModelType)
    {
        using var provider = new ServiceCollection().AddVirgilServices().AddVirgilViews()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var vm = provider.GetRequiredService(viewModelType);
        var field = viewModelType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(value => value.FieldType == typeof(IConfirmationService));
        Assert.Same(provider.GetRequiredService<IConfirmedPlanGate>(), field.GetValue(vm));
        Assert.Same(provider.GetRequiredService<IConfirmationService>(), field.GetValue(vm));
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
    public void CoreServicesExceptConfirmationAuthorityAreTransientAndHaveExplicitFactories()
    {
        var services = new ServiceCollection().AddVirgilServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var singletonTypes = new[]
        {
            typeof(IProcessRunner), typeof(TimeProvider), typeof(Virgil.Core.Actions.ConfirmationService),
            typeof(Virgil.Core.Actions.IConfirmationService), typeof(Virgil.Core.Actions.IConfirmedPlanGate)
        };
        foreach (var descriptor in services)
        {
            Assert.NotNull(descriptor.ImplementationFactory);
            if (singletonTypes.Contains(descriptor.ServiceType))
            {
                Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
                Assert.Same(provider.GetRequiredService(descriptor.ServiceType), provider.GetRequiredService(descriptor.ServiceType));
            }
            else
            {
                Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
                Assert.NotSame(provider.GetRequiredService(descriptor.ServiceType), provider.GetRequiredService(descriptor.ServiceType));
            }
        }
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void CleanupViewModelResolvesAsExplicitSingletonWithValidatedComposition()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var registration = Assert.Single(services, d => d.ServiceType == typeof(CleanupViewModel));
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        Assert.NotNull(registration.ImplementationFactory);
        var vm = provider.GetRequiredService<CleanupViewModel>();
        Assert.Same(vm, provider.GetRequiredService<CleanupViewModel>());
        var viewRegistration = Assert.Single(services, d => d.ServiceType == typeof(Virgil.App.Views.CleanupView));
        Assert.Equal(ServiceLifetime.Singleton, viewRegistration.Lifetime);
        Assert.NotNull(viewRegistration.ImplementationFactory);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void UpdatesSingletonCompositionUsesExplicitFactories()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(UpdatesViewModel));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Same(provider.GetRequiredService<UpdatesViewModel>(), provider.GetRequiredService<UpdatesViewModel>());
        var view = Assert.Single(services, d => d.ServiceType == typeof(Virgil.App.Views.UpdatesView));
        Assert.Equal(ServiceLifetime.Singleton, view.Lifetime); Assert.NotNull(view.ImplementationFactory);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void ApplicationsViewModelAndDialogResolveAsExplicitSingletons()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        foreach (var type in new[] { typeof(ApplicationsViewModel), typeof(Virgil.App.Services.IApplicationUninstallConfirmationDialog), typeof(Virgil.App.Views.ApplicationsView) })
        {
            var descriptor = Assert.Single(services, d => d.ServiceType == type);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            Assert.NotNull(descriptor.ImplementationFactory);
        }
        var vm = provider.GetRequiredService<ApplicationsViewModel>();
        Assert.Same(vm, provider.GetRequiredService<ApplicationsViewModel>());
        var dialog = provider.GetRequiredService<Virgil.App.Services.IApplicationUninstallConfirmationDialog>();
        Assert.IsType<Virgil.App.Services.ApplicationUninstallConfirmationDialogService>(dialog);
        Assert.Same(dialog, provider.GetRequiredService<Virgil.App.Services.IApplicationUninstallConfirmationDialog>());
    }

    private static ServiceProvider BuildProvider() => new ServiceCollection().AddVirgilServices()
        .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
}
