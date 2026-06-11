using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Virgil.Domain;

public static class ScanRules
{
    public static ulong CalculateMemoryUsedBytes(ulong totalPhysicalBytes, ulong availablePhysicalBytes)
    {
        if (totalPhysicalBytes == 0 || availablePhysicalBytes >= totalPhysicalBytes)
        {
            return 0;
        }

        return totalPhysicalBytes - availablePhysicalBytes;
    }

    public static long CalculateDiskUsedBytes(long totalBytes, long availableBytes)
    {
        if (totalBytes <= 0)
        {
            return 0;
        }

        var safeAvailable = Math.Clamp(availableBytes, 0, totalBytes);
        return totalBytes - safeAvailable;
    }

    public static double CalculateUsedPercent(ulong usedBytes, ulong totalBytes)
    {
        if (totalBytes == 0)
        {
            return 0;
        }

        var safeUsed = Math.Min(usedBytes, totalBytes);
        return Math.Round((double)safeUsed / totalBytes * 100, 1);
    }

    public static double CalculateUsedPercent(long usedBytes, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return 0;
        }

        var safeUsed = Math.Clamp(usedBytes, 0, totalBytes);
        return Math.Round((double)safeUsed / totalBytes * 100, 1);
    }

    public static ScanSeverity CalculateCpuSeverity(double usagePercent)
    {
        var percent = NormalizePercent(usagePercent);

        if (percent >= 85)
        {
            return ScanSeverity.Warning;
        }

        if (percent >= 70)
        {
            return ScanSeverity.Attention;
        }

        return ScanSeverity.Healthy;
    }

    public static ScanSeverity CalculateMemorySeverity(double usedPercent)
    {
        var percent = NormalizePercent(usedPercent);

        if (percent >= 85)
        {
            return ScanSeverity.Warning;
        }

        if (percent >= 70)
        {
            return ScanSeverity.Attention;
        }

        return ScanSeverity.Healthy;
    }

    public static ScanSeverity CalculateDiskSeverity(double usedPercent)
    {
        var percent = NormalizePercent(usedPercent);

        if (percent >= 90)
        {
            return ScanSeverity.Critical;
        }

        if (percent >= 80)
        {
            return ScanSeverity.Attention;
        }

        return ScanSeverity.Healthy;
    }

    public static string StatusForSeverity(ScanSeverity severity)
    {
        return severity switch
        {
            ScanSeverity.Critical => "Critique",
            ScanSeverity.Warning => "Eleve",
            ScanSeverity.Attention => "Attention",
            ScanSeverity.Information => "Information",
            _ => "Normal"
        };
    }

    public static string OverallStatus(IEnumerable<ScanSeverity> severities)
    {
        var highestSeverity = severities.DefaultIfEmpty(ScanSeverity.Healthy).Max();

        return highestSeverity switch
        {
            ScanSeverity.Critical => "Intervention prioritaire",
            ScanSeverity.Warning => "Intervention recommandee",
            ScanSeverity.Attention => "A surveiller",
            _ => "Stable"
        };
    }

    public static IReadOnlyList<string> BuildRecommendations(IEnumerable<ScanFinding> findings)
    {
        return findings
            .Select((finding, index) => new { Finding = finding, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Finding.Recommendation))
            .OrderByDescending(item => item.Finding.Severity)
            .ThenBy(item => item.Index)
            .Select(item => item.Finding.Recommendation!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static DiskScanInfo? SelectSystemDisk(IEnumerable<DiskScanInfo> disks, string systemPath)
    {
        var diskList = disks.ToList();
        var markedSystemDisk = diskList.FirstOrDefault(disk => disk.IsSystemDrive);

        if (markedSystemDisk is not null)
        {
            return markedSystemDisk;
        }

        var systemRoot = NormalizeRoot(systemPath);
        if (!string.IsNullOrEmpty(systemRoot))
        {
            var rootMatch = diskList.FirstOrDefault(disk => NormalizeRoot(disk.Name) == systemRoot);
            if (rootMatch is not null)
            {
                return rootMatch;
            }
        }

        return diskList.FirstOrDefault();
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        var value = (double)Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", value, units[unit]);
    }

    public static string FormatBytes(ulong bytes)
    {
        return bytes > long.MaxValue
            ? FormatBytes(long.MaxValue)
            : FormatBytes((long)bytes);
    }

    public static bool IsImportant(ScanSeverity severity)
    {
        return severity >= ScanSeverity.Warning;
    }

    private static double NormalizePercent(double percent)
    {
        if (double.IsNaN(percent) || double.IsInfinity(percent))
        {
            return 0;
        }

        return Math.Clamp(percent, 0, 100);
    }

    private static string NormalizeRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var root = value;
        try
        {
            root = Path.GetPathRoot(value) ?? value;
        }
        catch (ArgumentException)
        {
        }

        return root.TrimEnd('\\', '/').ToUpperInvariant();
    }
}
