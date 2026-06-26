using Virgil.Core.Applications;
using Virgil.Core.Cleanup;
using Virgil.Core.Interventions;
using Virgil.Core.Resources;
using Virgil.Core.Scanning;
using Virgil.Core.Updates;
using Virgil.Domain;
using Virgil.Domain.Applications;
using Xunit;

namespace Virgil.Tests;

public sealed class SystemScanServiceTests
{
    [Fact]
    public async Task QuickScan_does_not_preview_cleanup()
    {
        var cleanup = new CountingCleanupService();
        var updates = new CountingUpdateScanService();
        var service = CreateService(cleanup, updates);

        var report = await service.RunAsync(ScanMode.Quick, null, CancellationToken.None);

        Assert.False(report.Cleanup.WasAnalyzed);
        Assert.Equal(0, cleanup.PreviewCalls);
        Assert.Single(updates.Requests);
        Assert.False(updates.Requests[0].IncludeApplicationUpdates);
        Assert.True(report.Updates.WasAnalyzed);
    }

    [Fact]
    public async Task DeepScan_previews_cleanup_without_execution()
    {
        var cleanup = new CountingCleanupService();
        var updates = new CountingUpdateScanService(new UpdateScanReport
        {
            Scope = UpdateScanScope.DeepPreview,
            OverallStatus = "Mises a jour disponibles",
            Winget = new WingetAvailability
            {
                IsAvailable = true,
                ExecutablePath = "winget.exe",
                Version = "1.8.0",
                Message = "WinGet detecte."
            },
            Items = new[]
            {
                new UpdateItem
                {
                    Id = "VideoLAN.VLC",
                    Name = "VLC",
                    InstalledVersion = "3.0",
                    AvailableVersion = "3.1",
                    Source = UpdateSource.Winget,
                    RiskLevel = UpdateRiskLevel.Safe
                }
            }
        });
        var service = CreateService(cleanup, updates);

        var report = await service.RunAsync(ScanMode.Deep, null, CancellationToken.None);

        Assert.True(report.Cleanup.WasAnalyzed);
        Assert.Equal(1, cleanup.PreviewCalls);
        Assert.Equal(42, report.Cleanup.PotentialBytes);
        Assert.Equal(1, report.Cleanup.FileCount);
        Assert.Contains("Zone test lecture seule", report.Cleanup.Zones);
        Assert.Single(updates.Requests);
        Assert.True(updates.Requests[0].IncludeApplicationUpdates);
        Assert.True(report.Updates.WasAnalyzed);
        Assert.Equal(1, report.Updates.ApplicationUpdates);
        Assert.True(report.Applications.WasAnalyzed);
        Assert.Equal(1, report.Applications.DetectedCount);
    }

    [Fact]
    public async Task RunAsync_KeepsReportWhenCleanupReaderFails()
    {
        var service = CreateService(new ThrowingCleanupService(), new CountingUpdateScanService());

        var report = await service.RunAsync(ScanMode.Deep, null, CancellationToken.None);

        Assert.Equal(ScanMode.Deep, report.Mode);
        Assert.True(report.Cleanup.WasAnalyzed);
        Assert.Contains("Analyse nettoyage indisponible.", report.Errors);
        Assert.NotEqual(default, report.CapturedAt);
        Assert.NotNull(report.Windows);
        Assert.NotNull(report.Processor);
        Assert.NotNull(report.Memory);
        Assert.NotNull(report.Network);
    }

    [Fact]
    public async Task QuickScan_does_not_run_resource_observation()
    {
        var resources = new CountingResourceMonitoringService();
        var service = CreateService(
            new CountingCleanupService(),
            new CountingUpdateScanService(),
            new EmptyInterventionDiagnosticService(),
            resources);

        var report = await service.RunAsync(ScanMode.Quick, null, CancellationToken.None);

        Assert.False(report.Resources.WasAnalyzed);
        Assert.Equal(0, resources.Calls);
    }

    [Fact]
    public async Task DeepScan_includes_read_only_resource_observation()
    {
        var process = new ProcessResourceInfo
        {
            ProcessId = 42,
            Name = "HeavyApp",
            WorkingSetBytes = 800L * 1024 * 1024,
            CpuPercent = 30,
            Status = ProcessResourceStatus.Heavy,
            CanCloseGracefully = true,
            CanForceClose = true
        };
        var resources = new CountingResourceMonitoringService(new ResourceAnalysisReport
        {
            AverageCpuPercent = 36,
            AverageMemoryPercent = 88,
            Samples = new[]
            {
                new ResourceSample
                {
                    Uptime = TimeSpan.FromDays(4),
                    ProcessCount = 75
                }
            },
            TopMemoryProcesses = new[] { process },
            TopCpuProcesses = new[] { process },
            Recommendations = new[] { "Examiner les applications lourdes." }
        });
        var service = CreateService(
            new CountingCleanupService(),
            new CountingUpdateScanService(),
            new EmptyInterventionDiagnosticService(),
            resources);

        var report = await service.RunAsync(ScanMode.Deep, null, CancellationToken.None);

        Assert.True(report.Resources.WasAnalyzed);
        Assert.Equal(1, resources.Calls);
        Assert.Equal(36, report.Resources.AverageCpuPercent);
        Assert.Equal(88, report.Resources.MemoryPercent);
        Assert.Equal(1, report.Resources.HeavyProcessCount);
        Assert.Contains("HeavyApp", report.Resources.TopMemoryProcesses[0]);
        Assert.Contains(report.Findings, finding => finding.Id == "resources-heavy-processes");
    }

    private static SystemScanService CreateService(
        ICleanupService cleanupService,
        IUpdateScanService updateScanService,
        IInterventionDiagnosticService? interventionDiagnosticService = null,
        IResourceMonitoringService? resourceMonitoringService = null,
        IApplicationInventoryService? applicationInventoryService = null)
    {
        return new SystemScanService(
            cleanupService,
            updateScanService,
            interventionDiagnosticService ?? new EmptyInterventionDiagnosticService(),
            resourceMonitoringService ?? new CountingResourceMonitoringService(),
            applicationInventoryService ?? new CountingApplicationInventoryService());
    }

    private sealed class CountingCleanupService : ICleanupService
    {
        public int PreviewCalls { get; private set; }

        public CleanupPreview PreviewTemporaryFiles()
        {
            PreviewCalls++;
            return new CleanupPreview(
                DateTimeOffset.Now,
                new[]
                {
                    new CleanupTarget("Zone test lecture seule", "%TEMP%", 42, 1, "Faible")
                });
        }
    }

    private sealed class ThrowingCleanupService : ICleanupService
    {
        public CleanupPreview PreviewTemporaryFiles()
        {
            throw new InvalidOperationException("Simulated cleanup failure.");
        }
    }

    private sealed class CountingUpdateScanService : IUpdateScanService
    {
        private readonly UpdateScanReport _report;

        public CountingUpdateScanService()
            : this(new UpdateScanReport
            {
                Scope = UpdateScanScope.AvailabilityOnly,
                OverallStatus = "WinGet non detecte",
                Winget = WingetAvailability.Unavailable("WinGet non detecte.")
            })
        {
        }

        public CountingUpdateScanService(UpdateScanReport report)
        {
            _report = report;
        }

        public List<UpdateScanRequest> Requests { get; } = new();

        public Task<UpdateScanReport> ScanAsync(
            UpdateScanRequest request,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_report with { Scope = request.Scope });
        }
    }

    private sealed class CountingResourceMonitoringService : IResourceMonitoringService
    {
        private readonly ResourceAnalysisReport _report;

        public CountingResourceMonitoringService()
            : this(new ResourceAnalysisReport())
        {
        }

        public CountingResourceMonitoringService(ResourceAnalysisReport report)
        {
            _report = report;
        }

        public int Calls { get; private set; }

        public Task<ResourceAnalysisReport> AnalyzeAsync(
            ResourceAnalysisRequest request,
            IProgress<ResourceProgress>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_report);
        }
    }

    private sealed class EmptyInterventionDiagnosticService : IInterventionDiagnosticService
    {
        public Task<IReadOnlyList<InterventionDiagnostic>> DiagnoseAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<InterventionDiagnostic>>(Array.Empty<InterventionDiagnostic>());
        }

        public Task<InterventionDiagnostic> DiagnoseAsync(InterventionId id, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CountingApplicationInventoryService : IApplicationInventoryService
    {
        public int Calls { get; private set; }

        public Task<ApplicationInventoryReport> InventoryAsync(
            IProgress<ApplicationInventoryProgress>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ApplicationInventoryReport
            {
                Applications =
                [
                    new InstalledApplication
                    {
                        Id = "vlc",
                        DisplayName = "VLC media player",
                        Publisher = "VideoLAN",
                        RiskLevel = ApplicationRiskLevel.SafeToUninstall,
                        CanUninstall = true,
                        EstimatedSizeBytes = 200L * 1024 * 1024
                    }
                ]
            });
        }
    }
}
