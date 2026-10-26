using System.Globalization;
using Virgil.Contracts;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Cleanup;
using Virgil.Core.Diagnostic;

namespace Virgil.Core.Status;

public sealed record MeasuredStatus<T>(T Value, DateTimeOffset CapturedAt);

public interface ISystemStatusStore
{
    MeasuredStatus<MemoryScanInfo>? LastMemory { get; }
    MeasuredStatus<DiskScanInfo>? LastSystemDisk { get; }
    MeasuredStatus<CleanupScanInfo>? LastCleanupEstimate { get; }
    MeasuredStatus<int>? LastPriorities { get; }
    event EventHandler? Changed;
    void PublishScan(SystemScanReport report);
    void PublishDiagnostic(DiagnosticFindings findings);
    void PublishCleanup(CleanupAnalysisReport analysis);
}

/// <summary>Dernières mesures disponibles ; une mesure ancienne ne remplace pas une plus récente.</summary>
public sealed class SystemStatusStore : ISystemStatusStore
{
    private readonly object _sync = new();
    private MeasuredStatus<MemoryScanInfo>? _memory;
    private MeasuredStatus<DiskScanInfo>? _disk;
    private MeasuredStatus<CleanupScanInfo>? _cleanup;
    private MeasuredStatus<int>? _priorities;
    public MeasuredStatus<MemoryScanInfo>? LastMemory { get { lock (_sync) return _memory; } }
    public MeasuredStatus<DiskScanInfo>? LastSystemDisk { get { lock (_sync) return _disk; } }
    public MeasuredStatus<CleanupScanInfo>? LastCleanupEstimate { get { lock (_sync) return _cleanup; } }
    public MeasuredStatus<int>? LastPriorities { get { lock (_sync) return _priorities; } }
    public event EventHandler? Changed;

    public void PublishScan(SystemScanReport report)
    {
        lock (_sync)
        {
            if (report.Memory.TotalPhysicalBytes > 0) Set(ref _memory, report.Memory, report.CapturedAt);
            var disk = report.Disks.FirstOrDefault(d => d.IsSystemDrive);
            if (disk is { TotalBytes: > 0 }) Set(ref _disk, disk, report.CapturedAt);
            if (report.Cleanup.WasAnalyzed && report.Cleanup.Errors.Count == 0) Set(ref _cleanup, report.Cleanup, report.CapturedAt);
            Set(ref _priorities, report.Findings.Count(f => f.Severity >= ScanSeverity.Attention), report.CapturedAt);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void PublishDiagnostic(DiagnosticFindings findings)
    {
        var snapshot = findings.Snapshot;
        var memory = snapshot.Memory;
        lock (_sync)
        {
            if (Valid(memory.TotalBytes) && Valid(memory.AvailableBytes) && Valid(memory.UsedBytes) && memory.TotalBytes.Value > 0)
                Set(ref _memory, new MemoryScanInfo((ulong)memory.TotalBytes.Value, (ulong)memory.AvailableBytes.Value,
                    (ulong)memory.UsedBytes.Value, 100 * memory.UsedBytes.Value / memory.TotalBytes.Value,
                    ScanSeverity.Information, "Mesure Diagnostic"), snapshot.CapturedAtUtc);
            var disk = snapshot.Disks.FirstOrDefault(d => d.IsSystemVolume);
            if (disk is not null && Valid(disk.TotalBytes) && Valid(disk.FreeBytes) && disk.TotalBytes.Value > 0)
                Set(ref _disk, new DiskScanInfo(disk.Id, disk.Id, (long)disk.TotalBytes.Value, (long)disk.FreeBytes.Value,
                    (long)(disk.TotalBytes.Value - disk.FreeBytes.Value), 100 * (1 - disk.FreeBytes.Value / disk.TotalBytes.Value),
                    ScanSeverity.Information, "Mesure Diagnostic", true), snapshot.CapturedAtUtc);
            Set(ref _priorities, findings.Issues.Count(i => i.Impact > 0), snapshot.CapturedAtUtc);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void PublishCleanup(CleanupAnalysisReport analysis)
    {
        lock (_sync) Set(ref _cleanup, CleanupEstimateService.Summarize(analysis), analysis.CapturedAt);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool Valid(DiagnosticValue<double> value) => value.IsAvailable && double.IsFinite(value.Value) && value.Value >= 0;
    private static void Set<T>(ref MeasuredStatus<T>? destination, T value, DateTimeOffset capturedAt)
    {
        if (destination is null || capturedAt >= destination.CapturedAt) destination = new(value, capturedAt);
    }
}

public static class SystemStatusText
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    public static string Bytes(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double value = Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return value.ToString("0.#", French) + " " + units[index];
    }
    public static string CleanupValue(CleanupScanInfo estimate) => Bytes(estimate.PotentialBytes) + " récupérables";
    public static string CleanupDetail(CleanupScanInfo estimate) =>
        $"Sans risque : {Bytes(estimate.SafePotentialBytes)} · À vérifier : {Bytes(estimate.AdvancedPotentialBytes)} · {estimate.ReviewItemCount} éléments à revoir";
    public static string MeasuredAt(DateTimeOffset capturedAt) => "Mesuré à " + capturedAt.ToLocalTime().ToString("HH:mm", French);
}
