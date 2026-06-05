namespace Virgil.Domain;

public sealed record MemoryStatus(
    ulong TotalBytes,
    ulong AvailableBytes)
{
    // GlobalMemoryStatusEx exposes physical total and available memory. Virgil reports
    // used = total - available; Task Manager can differ slightly because of cached,
    // compressed, and differently sampled memory.
    public ulong UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;

    public double UsedPercent => TotalBytes == 0
        ? 0
        : Math.Round((double)UsedBytes / TotalBytes * 100, 1);
}
