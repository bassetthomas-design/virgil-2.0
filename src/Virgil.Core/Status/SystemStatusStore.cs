using System.Globalization;
using Virgil.Contracts;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Cleanup;
using Virgil.Core.Diagnostic;

namespace Virgil.Core.Status;

public enum PrioritySource { Scan, Diagnostic }
public enum PriorityState { Active, ToRecheck, Resolved }

public sealed record PriorityPoint(string Id, string Title, string Value, int Order)
{
    public PrioritySource Source { get; init; }
    public PriorityState State { get; init; }
    public DateTimeOffset MeasuredAt { get; init; }
}

public sealed record MeasuredStatus<T>(T Value, DateTimeOffset CapturedAt);

public interface ISystemStatusStore
{
    MeasuredStatus<MemoryScanInfo>? LastMemory { get; }
    MeasuredStatus<DiskScanInfo>? LastSystemDisk { get; }
    MeasuredStatus<CleanupScanInfo>? LastCleanupEstimate { get; }
    MeasuredStatus<int>? LastPriorities { get; }
    MeasuredStatus<IReadOnlyList<PriorityPoint>>? LastPriorityPoints { get; }
    IReadOnlyList<PriorityPoint> LastResolvedPoints { get; }
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
    private MeasuredStatus<IReadOnlyList<PriorityPoint>>? _priorityPoints;
    private MeasuredStatus<IReadOnlyList<PriorityPoint>>? _scanPoints;
    private MeasuredStatus<IReadOnlyList<PriorityPoint>>? _diagnosticPoints;
    private IReadOnlyList<PriorityPoint> _resolved = Array.Empty<PriorityPoint>();
    public MeasuredStatus<MemoryScanInfo>? LastMemory { get { lock (_sync) return _memory; } }
    public MeasuredStatus<DiskScanInfo>? LastSystemDisk { get { lock (_sync) return _disk; } }
    public MeasuredStatus<CleanupScanInfo>? LastCleanupEstimate { get { lock (_sync) return _cleanup; } }
    public MeasuredStatus<int>? LastPriorities { get { lock (_sync) return _priorities; } }
    public MeasuredStatus<IReadOnlyList<PriorityPoint>>? LastPriorityPoints { get { lock (_sync) return _priorityPoints; } }
    public IReadOnlyList<PriorityPoint> LastResolvedPoints { get { lock (_sync) return _resolved; } }
    public event EventHandler? Changed;

    public void PublishScan(SystemScanReport report)
    {
        lock (_sync)
        {
            if (report.Memory.TotalPhysicalBytes > 0) Set(ref _memory, report.Memory, report.CapturedAt);
            var disk = report.Disks.FirstOrDefault(d => d.IsSystemDrive);
            if (disk is { TotalBytes: > 0 }) Set(ref _disk, disk, report.CapturedAt);
            if (report.Cleanup.WasAnalyzed && report.Cleanup.Errors.Count == 0) Set(ref _cleanup, report.Cleanup, report.CapturedAt);
            var fresh = report.Findings.Where(f => f.Severity >= ScanSeverity.Attention).OrderByDescending(f => f.Severity)
                .Select((f, order) => new PriorityPoint(f.Id, f.Title, f.Value ?? string.Empty, order)
                { Source = PrioritySource.Scan, State = PriorityState.Active, MeasuredAt = report.CapturedAt }).ToArray();
            if (Reevaluate(ref _scanPoints, fresh, report.CapturedAt, id => ScanCovers(report, id),
                id => id.StartsWith("partial-error-", StringComparison.Ordinal))) MergePriorities();
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
            var fresh = findings.Issues.Where(i => i.Impact > 0)
                .Select((i, order) => new PriorityPoint(i.Id, i.Title, i.Description, order)
                { Source = PrioritySource.Diagnostic, State = PriorityState.Active, MeasuredAt = snapshot.CapturedAtUtc }).ToArray();
            if (Reevaluate(ref _diagnosticPoints, fresh, snapshot.CapturedAtUtc, id => DiagnosticCovers(findings, id),
                id => id.StartsWith("process.", StringComparison.Ordinal))) MergePriorities();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void PublishCleanup(CleanupAnalysisReport analysis)
    {
        lock (_sync) Set(ref _cleanup, CleanupEstimateService.Summarize(analysis), analysis.CapturedAt);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool Reevaluate(ref MeasuredStatus<IReadOnlyList<PriorityPoint>>? current,
        IReadOnlyList<PriorityPoint> fresh, DateTimeOffset at, Func<string, bool> covers, Func<string, bool> replaced)
    {
        if (current is not null && at < current.CapturedAt) return false;
        var ids = fresh.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var carried = (current?.Value ?? [])
            .Where(p => p.State != PriorityState.Resolved && !replaced(p.Id) && !ids.Contains(p.Id))
            .Select(p => covers(p.Id) ? p with { State = PriorityState.Resolved, MeasuredAt = at }
                : p with { State = PriorityState.ToRecheck });
        current = new(Array.AsReadOnly(fresh.Concat(carried).ToArray()), at);
        return true;
    }

    private void MergePriorities()
    {
        var at = _scanPoints?.CapturedAt ?? _diagnosticPoints!.CapturedAt;
        if (_diagnosticPoints is { } diagnostic && diagnostic.CapturedAt > at) at = diagnostic.CapturedAt;
        var all = (_scanPoints?.Value ?? []).Concat(_diagnosticPoints?.Value ?? []).ToArray();
        var points = Array.AsReadOnly(all.Where(p => p.State != PriorityState.Resolved).ToArray());
        _priorityPoints = new(points, at);
        _priorities = new(points.Count, at);
        _resolved = Array.AsReadOnly(all.Where(p => p.State == PriorityState.Resolved).ToArray());
    }

    private static bool ScanCovers(SystemScanReport report, string id)
    {
        if (id.StartsWith("cleanup-potential-", StringComparison.Ordinal))
            return report.Cleanup is { WasAnalyzed: true, Errors.Count: 0 };
        if (id.StartsWith("updates-", StringComparison.Ordinal))
            return report.Mode == ScanMode.Deep && report.Updates is { WasAnalyzed: true, Errors.Count: 0 };
        if (id.StartsWith("resources-", StringComparison.Ordinal))
            return report.Mode == ScanMode.Deep && report.Resources is { WasAnalyzed: true, Errors.Count: 0 };
        if (id.StartsWith("interventions-", StringComparison.Ordinal))
            return report.Mode == ScanMode.Deep && report.Interventions is { WasAnalyzed: true, Errors.Count: 0 };
        if (id.StartsWith("applications-", StringComparison.Ordinal))
            return report.Mode == ScanMode.Deep && report.Applications is { WasAnalyzed: true, Errors.Count: 0 };
        if (id.StartsWith("disk-", StringComparison.Ordinal))
            return report.Disks.Any(d => "disk-" + d.Name.TrimEnd('\\') == id);
        return id switch
        {
            "process-memory" => report.Mode == ScanMode.Deep && report.TopProcesses.Count > 0,
            "memory-usage" => report.Memory.TotalPhysicalBytes > 0,
            "cpu-usage" => report.Processor is { Status: not "N/A" },
            "network-active-interface" => true,
            _ => false
        };
    }

    private static bool DiagnosticCovers(DiagnosticFindings findings, string id)
    {
        var snapshot = findings.Snapshot;
        if (id == "cpu.load") return DiagnosticAnalyzer.CpuLoad(snapshot) < DiagnosticAnalyzer.LoadThresholdPercent;
        if (id == "memory.load") return DiagnosticAnalyzer.MemoryLoad(snapshot) < DiagnosticAnalyzer.LoadThresholdPercent;
        if (id == "cpu.hot") return snapshot.Cpu.TemperatureCelsius.IsAvailable;
        if (id == "windows.errors") return findings.Signals.Errors.IsAvailable;
        if (id.StartsWith("device.", StringComparison.Ordinal)) return findings.Signals.Devices.IsAvailable;
        if (id.StartsWith("gpu.hot.", StringComparison.Ordinal))
            return snapshot.Gpu.Any(g => "gpu.hot." + g.Id == id && g.TemperatureCelsius.IsAvailable);
        if (id.StartsWith("disk.failure.", StringComparison.Ordinal))
            return snapshot.Disks.Any(d => "disk.failure." + d.Id == id && d.Kind == "Disque physique" && d.Health.IsAvailable);
        return id.StartsWith("space.", StringComparison.Ordinal) && snapshot.Disks.Any(d =>
            "space." + d.Id == id && d.Kind == "Volume" && d.TotalBytes.IsAvailable && d.FreeBytes.IsAvailable && d.TotalBytes.Value > 0);
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
