namespace Virgil.Domain;

public sealed record DriveStatus(
    string Name,
    string Label,
    long TotalBytes,
    long AvailableBytes)
{
    public long UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;

    public double UsedPercent => TotalBytes <= 0
        ? 0
        : Math.Round((double)UsedBytes / TotalBytes * 100, 1);
}
