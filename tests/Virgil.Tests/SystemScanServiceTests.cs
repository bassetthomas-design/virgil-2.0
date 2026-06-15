using Virgil.Core.Cleanup;
using Virgil.Core.Scanning;
using Virgil.Domain;
using Xunit;

namespace Virgil.Tests;

public sealed class SystemScanServiceTests
{
    [Fact]
    public async Task QuickScan_does_not_preview_cleanup()
    {
        var cleanup = new CountingCleanupService();
        var service = new SystemScanService(cleanup);

        var report = await service.RunAsync(ScanMode.Quick, null, CancellationToken.None);

        Assert.False(report.Cleanup.WasAnalyzed);
        Assert.Equal(0, cleanup.PreviewCalls);
    }

    [Fact]
    public async Task DeepScan_previews_cleanup_without_execution()
    {
        var cleanup = new CountingCleanupService();
        var service = new SystemScanService(cleanup);

        var report = await service.RunAsync(ScanMode.Deep, null, CancellationToken.None);

        Assert.True(report.Cleanup.WasAnalyzed);
        Assert.Equal(1, cleanup.PreviewCalls);
        Assert.Equal(42, report.Cleanup.PotentialBytes);
        Assert.Equal(1, report.Cleanup.FileCount);
        Assert.Contains("Zone test lecture seule", report.Cleanup.Zones);
    }

    [Fact]
    public async Task RunAsync_KeepsReportWhenCleanupReaderFails()
    {
        var service = new SystemScanService(new ThrowingCleanupService());

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
}
