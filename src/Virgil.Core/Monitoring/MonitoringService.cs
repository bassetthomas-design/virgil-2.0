using Virgil.Domain;

namespace Virgil.Core.Monitoring;

public sealed class MonitoringService : IMonitoringService
{
    public SystemHealthSnapshot CaptureSnapshot()
    {
        var memory = MemoryReader.Read();
        var drives = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
            .Select(drive => new DriveStatus(
                drive.Name,
                string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel,
                drive.TotalSize,
                drive.AvailableFreeSpace))
            .ToList();

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

    private static List<string> BuildRecommendations(MemoryStatus memory, IReadOnlyList<DriveStatus> drives)
    {
        var result = new List<string>();

        if (memory.UsedPercent >= 85)
        {
            result.Add("RAM très utilisée : analyser les processus actifs.");
        }
        else if (memory.UsedPercent >= 70)
        {
            result.Add("RAM élevée : surveillance conseillée.");
        }

        foreach (var drive in drives.Where(drive => drive.UsedPercent >= 90))
        {
            result.Add($"Disque {drive.Name} presque plein : nettoyage recommandé.");
        }

        return result;
    }
}
