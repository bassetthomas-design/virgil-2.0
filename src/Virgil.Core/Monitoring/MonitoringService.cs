using System.Collections.Generic;
using System.IO;
using System.Linq;
using Virgil.Domain;

namespace Virgil.Core.Monitoring;

public sealed class MonitoringService : IMonitoringService
{
    public SystemHealthSnapshot CaptureSnapshot()
    {
        var memory = ReadMemory();
        var drives = ReadFixedDrives();
        var recommendations = BuildRecommendations(memory, drives);
        var overallStatus = recommendations.Count == 0 ? "Stable" : "Attention recommandee";

        return new SystemHealthSnapshot(
            DateTimeOffset.Now,
            CpuUsagePercent: 0,
            memory,
            drives,
            overallStatus,
            recommendations);
    }

    private static MemoryStatus ReadMemory()
    {
        try
        {
            return MemoryReader.Read();
        }
        catch
        {
            return new MemoryStatus(0, 0);
        }
    }

    private static List<DriveStatus> ReadFixedDrives()
    {
        var drives = new List<DriveStatus>();

        foreach (var drive in EnumerateDrives())
        {
            TryAddDrive(drives, drive);
        }

        return drives;
    }

    private static IEnumerable<DriveInfo> EnumerateDrives()
    {
        try
        {
            return DriveInfo.GetDrives();
        }
        catch
        {
            return [];
        }
    }

    private static void TryAddDrive(ICollection<DriveStatus> drives, DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            {
                return;
            }

            drives.Add(new DriveStatus(
                drive.Name,
                string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel,
                drive.TotalSize,
                drive.AvailableFreeSpace));
        }
        catch
        {
            // Some drives can disappear or deny access while being inspected.
        }
    }

    private static List<string> BuildRecommendations(MemoryStatus memory, IReadOnlyList<DriveStatus> drives)
    {
        var result = new List<string>();

        if (memory.TotalBytes > 0 && memory.UsedPercent >= 85)
        {
            result.Add("RAM tres utilisee : analyser les processus actifs.");
        }
        else if (memory.TotalBytes > 0 && memory.UsedPercent >= 70)
        {
            result.Add("RAM elevee : surveillance conseillee.");
        }

        foreach (var drive in drives.Where(drive => drive.UsedPercent >= 90))
        {
            result.Add($"Disque {drive.Name} presque plein : nettoyage recommande.");
        }

        return result;
    }
}
