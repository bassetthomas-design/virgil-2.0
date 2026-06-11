using Virgil.Core.Cleanup;
using Virgil.Core.Scanning;
using Virgil.Domain;
using Xunit;

namespace Virgil.Tests;

public sealed class SystemScanServiceTests
{
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

    private sealed class ThrowingCleanupService : ICleanupService
    {
        public CleanupPreview PreviewTemporaryFiles()
        {
            throw new InvalidOperationException("Simulated cleanup failure.");
        }
    }
}
