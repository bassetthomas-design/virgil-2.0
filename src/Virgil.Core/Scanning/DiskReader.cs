using System.IO;
using Virgil.Domain;

namespace Virgil.Core.Scanning;

internal static class DiskReader
{
    public static ScanReaderResult<IReadOnlyList<DiskScanInfo>> ReadFixedDrives()
    {
        var disks = new List<DiskScanInfo>();
        var errors = new List<string>();
        var systemRoot = NormalizeRoot(Environment.SystemDirectory);

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return new ScanReaderResult<IReadOnlyList<DiskScanInfo>>(disks, ["Liste des disques indisponible."]);
        }

        foreach (var drive in drives)
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }

                var usedBytes = ScanRules.CalculateDiskUsedBytes(drive.TotalSize, drive.AvailableFreeSpace);
                var usedPercent = ScanRules.CalculateUsedPercent(usedBytes, drive.TotalSize);
                var severity = ScanRules.CalculateDiskSeverity(usedPercent);

                disks.Add(new DiskScanInfo(
                    drive.Name,
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel,
                    drive.TotalSize,
                    drive.AvailableFreeSpace,
                    usedBytes,
                    usedPercent,
                    severity,
                    ScanRules.StatusForSeverity(severity),
                    NormalizeRoot(drive.Name) == systemRoot));
            }
            catch
            {
                errors.Add($"Disque {drive.Name} inaccessible.");
            }
        }

        return new ScanReaderResult<IReadOnlyList<DiskScanInfo>>(disks, errors);
    }

    private static string NormalizeRoot(string value)
    {
        try
        {
            return (Path.GetPathRoot(value) ?? value).TrimEnd('\\', '/').ToUpperInvariant();
        }
        catch
        {
            return value.TrimEnd('\\', '/').ToUpperInvariant();
        }
    }
}
