using Virgil.Core.Cleanup;
using Virgil.Core.Scanning;
using Virgil.Core.Updates;
using Virgil.Domain;
using Xunit;

namespace Virgil.Tests;

public sealed class SystemScanServiceTests
{
    [Fact]
    public async Task QuickScan_does_not_preview_cleanup()
    {
        var cleanup = new CountingCleanupService();
        var updates = new CountingUpdateScanService();
        var service = new SystemScanService(cleanup, updates);

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
        var service = new SystemScanService(cleanup, updates);

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
    }

    [Fact]
    public async Task RunAsync_KeepsReportWhenCleanupReaderFails()
    {
        var service = new SystemScanService(new ThrowingCleanupService(), new CountingUpdateScanService());

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
}
