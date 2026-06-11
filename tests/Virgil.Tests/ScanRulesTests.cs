using Virgil.Domain;
using Xunit;

namespace Virgil.Tests;

public sealed class ScanRulesTests
{
    [Fact]
    public void CalculateMemoryUsedBytes_ReturnsZeroForIncoherentValues()
    {
        Assert.Equal<ulong>(0, ScanRules.CalculateMemoryUsedBytes(0, 0));
        Assert.Equal<ulong>(0, ScanRules.CalculateMemoryUsedBytes(100, 150));
    }

    [Fact]
    public void CalculateMemoryUsedPercent_UsesRawTotalAndAvailableValues()
    {
        var used = ScanRules.CalculateMemoryUsedBytes(16UL * 1024 * 1024 * 1024, 4UL * 1024 * 1024 * 1024);

        Assert.Equal(12UL * 1024 * 1024 * 1024, used);
        Assert.Equal(75, ScanRules.CalculateUsedPercent(used, 16UL * 1024 * 1024 * 1024));
    }

    [Theory]
    [InlineData(69.9, ScanSeverity.Healthy)]
    [InlineData(70, ScanSeverity.Attention)]
    [InlineData(84.9, ScanSeverity.Attention)]
    [InlineData(85, ScanSeverity.Warning)]
    public void CalculateMemorySeverity_UsesExpectedThresholds(double percent, ScanSeverity expected)
    {
        Assert.Equal(expected, ScanRules.CalculateMemorySeverity(percent));
    }

    [Theory]
    [InlineData(79.9, ScanSeverity.Healthy)]
    [InlineData(80, ScanSeverity.Attention)]
    [InlineData(89.9, ScanSeverity.Attention)]
    [InlineData(90, ScanSeverity.Critical)]
    public void CalculateDiskSeverity_UsesExpectedThresholds(double percent, ScanSeverity expected)
    {
        Assert.Equal(expected, ScanRules.CalculateDiskSeverity(percent));
    }

    [Fact]
    public void CalculateDiskUsedBytes_ClampsAvailableSpace()
    {
        Assert.Equal(0, ScanRules.CalculateDiskUsedBytes(0, 0));
        Assert.Equal(0, ScanRules.CalculateDiskUsedBytes(100, 150));
        Assert.Equal(100, ScanRules.CalculateDiskUsedBytes(100, -1));
    }

    [Fact]
    public void OverallStatus_ReturnsHighestSeverityStatus()
    {
        Assert.Equal("Stable", ScanRules.OverallStatus(Array.Empty<ScanSeverity>()));
        Assert.Equal("A surveiller", ScanRules.OverallStatus([ScanSeverity.Healthy, ScanSeverity.Attention]));
        Assert.Equal("Intervention recommandee", ScanRules.OverallStatus([ScanSeverity.Warning, ScanSeverity.Attention]));
        Assert.Equal("Intervention prioritaire", ScanRules.OverallStatus([ScanSeverity.Critical, ScanSeverity.Warning]));
    }

    [Fact]
    public void BuildRecommendations_OrdersBySeverityThenOriginalOrder()
    {
        var findings = new[]
        {
            new ScanFinding("memory", "Memoire", "RAM", "RAM elevee", "75 %", ScanSeverity.Attention, "Surveiller la RAM."),
            new ScanFinding("disk", "Stockage", "Disque", "Disque plein", "91 %", ScanSeverity.Critical, "Liberer le disque."),
            new ScanFinding("disk-duplicate", "Stockage", "Disque", "Disque plein", "92 %", ScanSeverity.Critical, "Liberer le disque.")
        };

        Assert.Equal(["Liberer le disque.", "Surveiller la RAM."], ScanRules.BuildRecommendations(findings));
    }

    [Fact]
    public void SelectSystemDisk_PrefersMarkedSystemDrive()
    {
        var disks = new[]
        {
            new DiskScanInfo("D:\\", "Data", 100, 50, 50, 50, ScanSeverity.Healthy, "Normal", false),
            new DiskScanInfo("C:\\", "System", 100, 40, 60, 60, ScanSeverity.Healthy, "Normal", true)
        };

        Assert.Equal("C:\\", ScanRules.SelectSystemDisk(disks, "D:\\Windows")?.Name);
    }

    [Fact]
    public void FormatBytes_FormatsBinaryUnits()
    {
        Assert.Equal("0 o", ScanRules.FormatBytes(-1));
        Assert.Equal("1 Ko", ScanRules.FormatBytes(1024));
        Assert.EndsWith(" Ko", ScanRules.FormatBytes(1536), StringComparison.Ordinal);
    }
}
