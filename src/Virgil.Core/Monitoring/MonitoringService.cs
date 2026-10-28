using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Virgil.Core.Scanning;
using Virgil.Contracts;

namespace Virgil.Core.Monitoring;

public sealed class MonitoringService : IMonitoringService
{
    public Task<SystemHealthSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken) =>
        Task.Run(() => CaptureCoreAsync(cancellationToken), cancellationToken);

    private async Task<SystemHealthSnapshot> CaptureCoreAsync(CancellationToken cancellationToken)
    {
        var memory = ReadMemory();
        var drives = ReadFixedDrives();
        var recommendations = BuildRecommendations(memory, drives);
        var overallStatus = recommendations.Count == 0 ? "Stable" : "Attention recommandee";

        return new SystemHealthSnapshot(
            DateTimeOffset.Now,
            await ReadCpuUsagePercentAsync(cancellationToken).ConfigureAwait(false),
            memory,
            drives,
            overallStatus,
            recommendations);
    }

    private static async Task<double> ReadCpuUsagePercentAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessorReader.MeasureUsageAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return 0;
        }
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
            return Array.Empty<DriveInfo>();
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
            result.Add("RAM tres utilisée : analyser les processus actifs.");
        }
        else if (memory.TotalBytes > 0 && memory.UsedPercent >= 70)
        {
            result.Add("RAM elevee : surveillance conseillée.");
        }

        foreach (var drive in drives.Where(drive => drive.UsedPercent >= 90))
        {
            result.Add($"Disque {drive.Name} presque plein : nettoyage recommandé.");
        }

        return result;
    }
}
