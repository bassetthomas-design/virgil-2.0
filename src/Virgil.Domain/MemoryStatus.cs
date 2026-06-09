namespace Virgil.Domain;

public sealed record MemoryStatus(
    ulong TotalBytes,
    ulong AvailableBytes)
{
    // Windows can show slightly different RAM values because sampling timing, cache,
    // compressed memory, and Task Manager presentation can vary.
    public ulong UsedBytes => ScanRules.CalculateMemoryUsedBytes(TotalBytes, AvailableBytes);

    public double UsedPercent => ScanRules.CalculateUsedPercent(UsedBytes, TotalBytes);
}
