using Virgil.Domain;

namespace Virgil.Core.Monitoring;

public sealed class MonitoringService : IMonitoringService
{
    public SystemHealthSnapshot CaptureSnapshot()
    {
        var memory = ReadMemoryStatus();
        var drives = ReadFixedDrives();
        var recommendations = BuildRecommendations(memory, drives);
        var overallStatus = recommendations.Count == 0 ? "Stable" : "Attention recommandée";

        return new SystemHealthSnapshot(
            DateTimeOffset.Now,
            CpuUsagePercent: 0,
            memory,
            drives,
            overallStatus,
            recommendations);
    }

    private static MemoryStatus ReadMemoryStatus()
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

    private static IReadOnlyList<DriveStatus> ReadFixedDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Select(TryReadDrive)
                .OfType<DriveStatus>()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static DriveStatus? TryReadDrive(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            {
                return null;
            }

            return new DriveStatus(
                drive.Name,
                string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel,
                drive.TotalSize,
                drive.AvailableFreeSpace);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> BuildRecommendations(MemoryStatus memory, IReadOnlyList<DriveStatus> drives)
    {
        var result = new List<string>();

        if (memory.TotalBytes == 0)
        {
            result.Add("RAM inaccessible : lecture système à relancer.");
        }
        else if (memory.UsedPercent >= 85)
        {
            result.Add("RAM très utilisée : analyser les processus actifs.");
        }
        else if (memory.UsedPercent >= 70)
        {
            result.Add("RAM élevée : surveillance conseillée.");
        }

        if (drives.Count == 0)
        {
            result.Add("Disque système inaccessible : vérification impossible.");
        }

        foreach (var drive in drives.Where(drive => drive.UsedPercent >= 90))
        {
            result.Add($"Disque {drive.Name} presque plein : nettoyage recommandé.");
        }

        return result;
    }
}
