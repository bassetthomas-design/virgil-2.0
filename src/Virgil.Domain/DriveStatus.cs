namespace Virgil.Domain;

public sealed record DriveStatus(
    string Name,
    string Label,
    long TotalBytes,
    long AvailableBytes)
{
    public long UsedBytes => ScanRules.CalculateDiskUsedBytes(TotalBytes, AvailableBytes);

    public double UsedPercent => ScanRules.CalculateUsedPercent(UsedBytes, TotalBytes);
}
