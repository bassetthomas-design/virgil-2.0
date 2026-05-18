namespace Virgil.Domain;

public sealed record MemoryStatus(
    ulong TotalBytes,
    ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;

    public double UsedPercent => TotalBytes == 0
        ? 0
        : Math.Round((double)UsedBytes / TotalBytes * 100, 1);
}
