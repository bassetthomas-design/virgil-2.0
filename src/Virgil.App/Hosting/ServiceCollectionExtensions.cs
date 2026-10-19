using Virgil.Core.Actions;
using System.IO;
using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Views;
using Virgil.App.Services;
using Virgil.App.ViewModels;
using Virgil.Core.Applications;
using Virgil.Core.Cleanup;
using Virgil.Core.Interventions;
using Virgil.Core.Monitoring;
using Virgil.Core.Processes;
using Virgil.Core.Reports;
using Virgil.Core.Resources;
using Virgil.Core.Scanning;
using Virgil.Core.Updates;

namespace Virgil.App.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVirgilDiagnostics(this IServiceCollection services,
        Virgil.Infrastructure.Storage.VirgilPaths paths, Virgil.Core.Settings.ISettingsService settings,
        System.Windows.Threading.Dispatcher dispatcher, ExceptionReporter? reporter = null)
    {
        services.AddLogging();
        services.AddSingleton(paths);
        services.AddSingleton<Virgil.Core.Settings.ISettingsService>(settings);
        services.AddSingleton<IUserNotifier>(sp => new MessageBoxUserNotifier(
            sp.GetRequiredService<Virgil.Infrastructure.Storage.VirgilPaths>(), dispatcher));
        if (reporter is null) services.AddSingleton<ExceptionReporter>();
        else services.AddSingleton(reporter);
        return services;
    }

    public static IServiceCollection AddVirgilServices(this IServiceCollection services)
    {
        services.AddVirgilConfirmation();
        services.AddSingleton<IProcessRunner>(_ => new Virgil.Infrastructure.Processes.ProcessRunner());

        services.AddTransient<IInterventionCatalog>(_ => new InterventionCatalog());
        services.AddTransient<IExplorerRestarter>(sp => new ExplorerRestarter(
            Process.GetProcessesByName, sp.GetRequiredService<IProcessRunner>()));
        services.AddTransient<IElevatedProcessLauncher>(sp => new ElevatedProcessLauncher(sp.GetRequiredService<IProcessRunner>()));
        services.AddTransient<IInterventionElevatedHelperClient>(sp => new ElevatedHelperClient(
            new ElevatedHelperRequestStore(), sp.GetRequiredService<IElevatedProcessLauncher>(),
            () => Path.Combine(AppContext.BaseDirectory, "Virgil.ElevatedHelper.exe")));
        services.AddTransient<IInterventionDiagnosticService>(sp => new InterventionDiagnosticService(
            sp.GetRequiredService<IInterventionCatalog>(), File.Exists, () => Environment.SystemDirectory,
            Process.GetProcessesByName, NetworkInterface.GetAllNetworkInterfaces));
        services.AddTransient<IInterventionExecutionService>(sp => new InterventionExecutionService(
            sp.GetRequiredService<IInterventionElevatedHelperClient>(), sp.GetRequiredService<IExplorerRestarter>(),
            sp.GetRequiredService<IConfirmedPlanGate>()));
        services.AddTransient<InterventionReportBuilder>(_ => new InterventionReportBuilder());

        services.AddTransient<WingetAvailabilityService>(sp => new WingetAvailabilityService(sp.GetRequiredService<IProcessRunner>()));
        services.AddTransient<UpdateRiskClassifier>(_ => new UpdateRiskClassifier());
        services.AddTransient<WindowsUpdateStatusService>(_ => new WindowsUpdateStatusService());
        services.AddTransient<DriverInformationService>(sp => new DriverInformationService(sp.GetRequiredService<IProcessRunner>()));
        services.AddTransient<IUpdateScanService>(sp => new WingetUpdateScanService(
            sp.GetRequiredService<WingetAvailabilityService>(), sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<UpdateRiskClassifier>(), sp.GetRequiredService<WindowsUpdateStatusService>(),
            sp.GetRequiredService<DriverInformationService>()));
        services.AddTransient<IUpdateExecutionService>(sp => new WingetUpdateExecutionService(
            sp.GetRequiredService<WingetAvailabilityService>(), sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IConfirmedPlanGate>()));

        services.AddTransient<IApplicationInventoryService>(sp => new ApplicationInventoryService(
            [new ApplicationRegistryReader(), new ApplicationWingetReader(sp.GetRequiredService<IProcessRunner>()),
                new ApplicationStoreReader(sp.GetRequiredService<IProcessRunner>())],
            new ApplicationRiskClassifier(), new ApplicationIconExtractor()));
        services.AddTransient<IApplicationProcessLauncher>(sp => new ShellApplicationProcessLauncher(sp.GetRequiredService<IProcessRunner>()));
        services.AddTransient<ApplicationUninstallService>(sp => new ApplicationUninstallService(
            new ApplicationUninstallCommandValidator(), sp.GetRequiredService<IApplicationProcessLauncher>(),
            new ApplicationRemnantScanner(), new ApplicationUninstallConfirmationPolicy(),
            sp.GetRequiredService<IConfirmedPlanGate>()));

        services.AddTransient<IProcessRuntime>(sp => new ProcessRuntime(sp.GetRequiredService<IProcessRunner>()));
        services.AddTransient<ProcessProtectionPolicy>(_ => new ProcessProtectionPolicy(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"), Environment.ProcessId));
        services.AddTransient<IProcessInspectionService>(sp => new ProcessInspectionService(
            new ProcessSnapshotProvider(), sp.GetRequiredService<ProcessProtectionPolicy>(), Environment.ProcessorCount));
        services.AddTransient<ResourceRecommendationService>(_ => new ResourceRecommendationService());
        services.AddTransient<IResourceMonitoringService>(_ => new ResourceMonitoringService());
        services.AddTransient<IProcessActionService>(sp => new ProcessActionService(
            sp.GetRequiredService<IProcessRuntime>(), sp.GetRequiredService<ProcessProtectionPolicy>(),
            sp.GetRequiredService<IExplorerRestarter>(), sp.GetRequiredService<IInterventionCatalog>(), () => DateTimeOffset.Now,
            sp.GetRequiredService<IConfirmedPlanGate>()));
        services.AddTransient<ResourceReportBuilder>(_ => new ResourceReportBuilder());
        services.AddTransient<IMonitoringService>(_ => new MonitoringService());

        services.AddTransient<CleanupSafetyClassifier>(_ => new CleanupSafetyClassifier());
        services.AddTransient<IRecycleBinService>(_ => new WindowsRecycleBinService());
        services.AddTransient<CleanupPreviewService>(sp => new CleanupPreviewService(
            CleanupZoneCatalog.CreateDefault(), () => DateTimeOffset.Now,
            sp.GetRequiredService<CleanupSafetyClassifier>(), sp.GetRequiredService<IRecycleBinService>()));
        services.AddTransient<ICleanupService>(sp => sp.GetRequiredService<CleanupPreviewService>());
        services.AddTransient<ICleanupPreviewService>(sp => sp.GetRequiredService<CleanupPreviewService>());
        services.AddTransient<ICleanupExecutionService>(sp => new CleanupExecutionService(
            sp.GetRequiredService<IConfirmedPlanGate>(), () => DateTimeOffset.Now, sp.GetRequiredService<IRecycleBinService>(), sp.GetRequiredService<CleanupSafetyClassifier>()));
        services.AddTransient<CleanupStorageAnalyzer>(_ => new CleanupStorageAnalyzer());
        services.AddTransient<CleanupAnalyzer>(sp => new CleanupAnalyzer(
            sp.GetRequiredService<ICleanupPreviewService>(), sp.GetRequiredService<CleanupStorageAnalyzer>()));
        services.AddTransient<CleanupPermissionRepairService>(_ => new CleanupPermissionRepairService());

        services.AddTransient<IReportSanitizer>(_ => new ReportSanitizer(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        services.AddTransient<IReportHistoryService>(sp => new ReportHistoryService(
            new ApplicationDataReportStorageRootProvider(), sp.GetRequiredService<IReportSanitizer>()));
        services.AddTransient<IReportExportService>(sp => new ReportExportService(sp.GetRequiredService<IReportSanitizer>()));
        services.AddTransient<ISystemScanService>(sp => new SystemScanService(
            sp.GetRequiredService<ICleanupService>(), sp.GetRequiredService<IUpdateScanService>(),
            sp.GetRequiredService<IInterventionDiagnosticService>(), sp.GetRequiredService<IResourceMonitoringService>(),
            sp.GetRequiredService<IApplicationInventoryService>()));
        return services;
    }

    public static IServiceCollection AddVirgilViews(this IServiceCollection services)
    {
        services.AddSingleton<CleanupViewModel>(sp => new CleanupViewModel(
            sp.GetRequiredService<ICleanupPreviewService>(), sp.GetRequiredService<ICleanupExecutionService>(),
            sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<CleanupStorageAnalyzer>(),
            sp.GetRequiredService<IReportHistoryService>(), sp.GetRequiredService<IConfirmationService>()));
        services.AddSingleton<CleanupView>(sp => new CleanupView(sp.GetRequiredService<CleanupViewModel>()));
        services.AddSingleton<UpdatesViewModel>(sp => new UpdatesViewModel(
            sp.GetRequiredService<IUpdateScanService>(), sp.GetRequiredService<IUpdateExecutionService>(),
            sp.GetRequiredService<WindowsUpdateStatusService>(), sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IReportHistoryService>(), sp.GetRequiredService<IConfirmationService>()));
        services.AddSingleton<UpdatesView>(sp => new UpdatesView(sp.GetRequiredService<UpdatesViewModel>()));
        services.AddSingleton<InterventionsViewModel>(sp => new InterventionsViewModel(
            sp.GetRequiredService<IInterventionDiagnosticService>(), sp.GetRequiredService<IInterventionExecutionService>(),
            sp.GetRequiredService<InterventionReportBuilder>(), sp.GetRequiredService<IReportHistoryService>(),
            sp.GetRequiredService<IConfirmationService>()));
        services.AddSingleton<InterventionsView>(sp => new InterventionsView(sp.GetRequiredService<InterventionsViewModel>()));
        services.AddSingleton<ResourcesViewModel>(sp => new ResourcesViewModel(
            sp.GetRequiredService<IResourceMonitoringService>(), sp.GetRequiredService<IProcessActionService>(),
            sp.GetRequiredService<ResourceReportBuilder>(), sp.GetRequiredService<IReportHistoryService>(),
            sp.GetRequiredService<IConfirmationService>()));
        services.AddSingleton<ResourcesView>(sp => new ResourcesView(sp.GetRequiredService<ResourcesViewModel>()));
        services.AddSingleton<IApplicationUninstallConfirmationDialog>(_ => new ApplicationUninstallConfirmationDialogService());
        services.AddSingleton<ApplicationsViewModel>(sp => new ApplicationsViewModel(
            sp.GetRequiredService<IApplicationInventoryService>(), sp.GetRequiredService<ApplicationUninstallService>(),
            sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<IReportHistoryService>(),
            sp.GetRequiredService<IApplicationUninstallConfirmationDialog>(), sp.GetRequiredService<IConfirmationService>()));
        services.AddSingleton<ApplicationsView>(sp => new ApplicationsView(sp.GetRequiredService<ApplicationsViewModel>()));
        services.AddSingleton<IFileSaveDialog>(_ => new SaveFileDialogService());
        services.AddSingleton<ReportsHistoryViewModel>(sp => new ReportsHistoryViewModel(
            sp.GetRequiredService<IReportHistoryService>(), sp.GetRequiredService<IReportExportService>(),
            sp.GetRequiredService<IFileSaveDialog>()));
        services.AddSingleton<ReportsHistoryView>(sp => new ReportsHistoryView(sp.GetRequiredService<ReportsHistoryViewModel>()));
        services.AddSingleton<MainWindow>(sp => new MainWindow(sp.GetRequiredService<ISystemScanService>(),
            sp.GetRequiredService<IReportHistoryService>(), sp.GetRequiredService<IReportExportService>(),
            sp.GetRequiredService<CleanupView>(), sp.GetRequiredService<UpdatesView>(), sp.GetRequiredService<InterventionsView>(),
            sp.GetRequiredService<ResourcesView>(), sp.GetRequiredService<ApplicationsView>(), sp.GetRequiredService<ReportsHistoryView>()));
        return services;
    }

}
