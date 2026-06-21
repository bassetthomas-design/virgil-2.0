using System;
using System.Collections.Generic;

namespace Virgil.Domain;

public enum ScanMode
{
    Quick,
    Deep
}

public enum ScanSeverity
{
    Information,
    Healthy,
    Attention,
    Warning,
    Critical
}

public sealed record ScanFinding(
    string Id,
    string Category,
    string Title,
    string Description,
    string? Value,
    ScanSeverity Severity,
    string? Recommendation);

public sealed record ScanProgress(
    string Step,
    int? Percent,
    string Message,
    string Category);

public sealed record SystemScanReport(
    DateTimeOffset CapturedAt,
    ScanMode Mode,
    TimeSpan Duration,
    string OverallStatus,
    WindowsScanInfo Windows,
    ProcessorScanInfo Processor,
    MemoryScanInfo Memory,
    IReadOnlyList<DiskScanInfo> Disks,
    IReadOnlyList<ProcessScanInfo> TopProcesses,
    NetworkScanInfo Network,
    CleanupScanInfo Cleanup,
    IReadOnlyList<ScanFinding> Findings,
    IReadOnlyList<string> Recommendations,
    IReadOnlyList<string> Errors)
{
    public UpdateScanSummary Updates { get; init; } = UpdateScanSummary.NotAnalyzed;

    public InterventionScanSummary Interventions { get; init; } = InterventionScanSummary.NotAnalyzed;

    public ResourceScanSummary Resources { get; init; } = ResourceScanSummary.NotAnalyzed;
}

public sealed record WindowsScanInfo(
    string Edition,
    string Version,
    string Build,
    string SystemArchitecture,
    string ProcessArchitecture,
    string MachineName,
    TimeSpan Uptime,
    DateTimeOffset ScanDate);

public sealed record ProcessorScanInfo(
    string Name,
    int LogicalProcessorCount,
    double UsagePercent,
    ScanSeverity Severity,
    string Status);

public sealed record MemoryScanInfo(
    ulong TotalPhysicalBytes,
    ulong AvailablePhysicalBytes,
    ulong UsedPhysicalBytes,
    double UsedPercent,
    ScanSeverity Severity,
    string Status);

public sealed record DiskScanInfo(
    string Name,
    string Label,
    long TotalBytes,
    long AvailableBytes,
    long UsedBytes,
    double UsedPercent,
    ScanSeverity Severity,
    string Status,
    bool IsSystemDrive);

public sealed record ProcessScanInfo(
    string Name,
    int ProcessId,
    long WorkingSetBytes,
    string? Path,
    string AccessStatus);

public sealed record NetworkScanInfo(
    string Name,
    string Type,
    string Status,
    long SpeedBitsPerSecond,
    string IPv4Address,
    string Gateway,
    IReadOnlyList<string> DnsServers);

public sealed record CleanupScanInfo(
    bool WasAnalyzed,
    long PotentialBytes,
    int FileCount,
    IReadOnlyList<string> Zones,
    IReadOnlyList<string> Errors);
