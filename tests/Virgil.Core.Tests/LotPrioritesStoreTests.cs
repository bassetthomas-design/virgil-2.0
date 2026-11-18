using System.Collections;
using Virgil.Contracts;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Diagnostic;
using Virgil.Core.Status;
using Xunit;

namespace Virgil.Tests;

public sealed class LotPrioritesStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 16, 24, 0, TimeSpan.Zero);
    private static readonly DiskScanInfo C = new("C:\\", "Système", 100, 50, 50, 50, ScanSeverity.Healthy, "Normal", true);

    [Fact]
    public void Scan_points_survive_a_diagnostic_and_return_to_normal_on_the_next_scan()
    {
        var store = new SystemStatusStore();
        store.PublishScan(Scan(T0, disks: [C], findings: [F("disk-C:", ScanSeverity.Warning)]));
        store.PublishDiagnostic(Diag(T0.AddMinutes(10), [I("windows.errors")]));

        var points = store.LastPriorityPoints!.Value;
        Assert.Equal(new[] { "disk-C:", "windows.errors" }, Ids(points));
        Assert.Equal(2, store.LastPriorities!.Value);
        Assert.Equal(T0.AddMinutes(10), store.LastPriorities.CapturedAt);
        Assert.Equal(new[] { "Scan", "Diagnostic" }, Tags(points, "Source"));
        Assert.Equal(new[] { "Active", "Active" }, Tags(points, "State"));
        Assert.Equal(new[] { T0, T0.AddMinutes(10) }, Times(points));

        store.PublishScan(Scan(T0.AddMinutes(20), disks: [C]));
        Assert.Equal(new[] { "windows.errors" }, Ids(store.LastPriorityPoints!.Value));
        Assert.Equal(1, store.LastPriorities!.Value);
        Assert.Equal(T0.AddMinutes(20), store.LastPriorities.CapturedAt);
        var resolved = Resolved(store);
        Assert.Equal(new[] { "disk-C:" }, Ids(resolved));
        Assert.Equal(new[] { "Scan" }, Tags(resolved, "Source"));
        Assert.Equal(new[] { "Resolved" }, Tags(resolved, "State"));
        Assert.Equal(new[] { T0.AddMinutes(20) }, Times(resolved));
    }

    [Fact]
    public void Quick_scan_keeps_deep_only_points_to_recheck_and_deep_scan_resolves_only_what_it_measured()
    {
        var store = new SystemStatusStore();
        ProcessScanInfo[] processes = [new("app", 1, 1, null, "OK")];
        store.PublishScan(Scan(T0, ScanMode.Deep, processes: processes, cleanup: new(true, 6L << 30, 3, [], []),
            findings: [F("cleanup-potential-warning", ScanSeverity.Warning), F("process-memory", ScanSeverity.Attention),
                F("updates-preview", ScanSeverity.Attention)]) with { Updates = new() { WasAnalyzed = true } });
        store.PublishScan(Scan(T0.AddMinutes(5), disks: [C]));

        var points = store.LastPriorityPoints!.Value;
        Assert.Equal(new[] { "cleanup-potential-warning", "process-memory", "updates-preview" }, Ids(points));
        Assert.Equal(3, store.LastPriorities!.Value);
        Assert.Equal(new[] { "ToRecheck", "ToRecheck", "ToRecheck" }, Tags(points, "State"));
        Assert.Equal(new[] { T0, T0, T0 }, Times(points));
        Assert.Empty(Resolved(store));

        store.PublishScan(Scan(T0.AddMinutes(10), ScanMode.Deep, processes: processes, cleanup: new(true, 1L << 30, 1, [], []))
            with { Updates = new() { WasAnalyzed = true, Errors = ["Analyse mises à jour indisponible."] } });
        points = store.LastPriorityPoints!.Value;
        Assert.Equal(new[] { "updates-preview" }, Ids(points));
        Assert.Equal(new[] { "ToRecheck" }, Tags(points, "State"));
        Assert.Equal(new[] { T0 }, Times(points));
        var resolved = Resolved(store);
        Assert.Equal(new[] { "cleanup-potential-warning", "process-memory" }, Ids(resolved));
        Assert.Equal(new[] { T0.AddMinutes(10), T0.AddMinutes(10) }, Times(resolved));
    }

    [Fact]
    public void Diagnostic_keeps_unmeasured_point_to_recheck_and_resolves_measured_one()
    {
        var store = new SystemStatusStore();
        store.PublishDiagnostic(Diag(T0, [I("space.C:"), I("windows.errors")]));
        store.PublishDiagnostic(Diag(T0.AddMinutes(5), [], DiagnosticAnalysisTests.Signals() with
        {
            Errors = DiagnosticValue<WindowsErrorSummary>.Unavailable("Journal System indisponible.")
        }));

        var points = store.LastPriorityPoints!.Value;
        Assert.Equal(new[] { "windows.errors" }, Ids(points));
        Assert.Equal(new[] { "ToRecheck" }, Tags(points, "State"));
        Assert.Equal(new[] { T0 }, Times(points));
        Assert.Equal(new[] { "Diagnostic" }, Tags(points, "Source"));
        var resolved = Resolved(store);
        Assert.Equal(new[] { "space.C:" }, Ids(resolved));
        Assert.Equal(new[] { T0.AddMinutes(5) }, Times(resolved));
    }

    [Fact]
    public void Positional_points_are_replaced_unreadable_cpu_stays_to_recheck_and_older_scan_is_ignored()
    {
        var store = new SystemStatusStore();
        store.PublishScan(Scan(T0, findings: [F("partial-error-3", ScanSeverity.Attention), F("cpu-usage", ScanSeverity.Warning)]));
        store.PublishScan(Scan(T0.AddMinutes(5), processor: new("CPU", 8, 0, ScanSeverity.Information, "N/A"),
            findings: [F("partial-error-1", ScanSeverity.Attention)]));

        var before = store.LastPriorityPoints!;
        Assert.Equal(new[] { "partial-error-1", "cpu-usage" }, Ids(before.Value));
        Assert.Equal(new[] { "Active", "ToRecheck" }, Tags(before.Value, "State"));
        Assert.Empty(Resolved(store));

        store.PublishScan(Scan(T0.AddMinutes(-5)));
        Assert.Same(before, store.LastPriorityPoints);
        Assert.Equal(new[] { "partial-error-1", "cpu-usage" }, Ids(store.LastPriorityPoints!.Value));
    }

    [Fact]
    public void Older_publication_of_the_same_source_never_replaces_points()
    {
        var store = new SystemStatusStore();
        store.PublishDiagnostic(Diag(T0.AddMinutes(10), [I("windows.errors")]));
        var before = store.LastPriorityPoints;
        store.PublishDiagnostic(Diag(T0, []));

        Assert.Equal(new[] { "windows.errors" }, Ids(store.LastPriorityPoints!.Value));
        Assert.Equal(1, store.LastPriorities!.Value);
        Assert.Same(before, store.LastPriorityPoints);
    }

    private static SystemScanReport Scan(DateTimeOffset at, ScanMode mode = ScanMode.Quick,
        ProcessorScanInfo? processor = null, IReadOnlyList<DiskScanInfo>? disks = null,
        IReadOnlyList<ProcessScanInfo>? processes = null, CleanupScanInfo? cleanup = null,
        IReadOnlyList<ScanFinding>? findings = null) =>
        new(at, mode, TimeSpan.Zero, "", null!, processor ?? new("CPU", 8, 10, ScanSeverity.Healthy, "Normal"),
            new(0, 0, 0, 0, ScanSeverity.Information, ""), disks ?? [], processes ?? [], null!,
            cleanup ?? new(false, 0, 0, [], []), findings ?? [], [], []);

    private static ScanFinding F(string id, ScanSeverity severity) => new(id, "", id, "", "", severity, null);
    private static DiagnosticIssue I(string id) => new(id, id, "Description", 2, 10, "test");

    private static DiagnosticFindings Diag(DateTimeOffset at, IReadOnlyList<DiagnosticIssue> issues, DiagnosticSignals? signals = null) =>
        new(DiagnosticAnalysisTests.Healthy() with { CapturedAtUtc = at }, signals ?? DiagnosticAnalysisTests.Signals(),
            issues, new HealthScore(90, false), []);

    private static IEnumerable<string> Ids(IEnumerable<PriorityPoint> points) => points.Select(p => p.Id);
    private static IEnumerable<string?> Tags(IEnumerable<PriorityPoint> points, string name) => points.Select(p => Read(p, name).ToString());
    private static IEnumerable<DateTimeOffset> Times(IEnumerable<PriorityPoint> points) => points.Select(p => (DateTimeOffset)Read(p, "MeasuredAt"));
    private static IEnumerable<PriorityPoint> Resolved(SystemStatusStore store) => ((IEnumerable)Read(store, "LastResolvedPoints")).Cast<PriorityPoint>();

    private static object Read(object owner, string name)
    {
        var property = owner.GetType().GetProperty(name);
        Assert.NotNull(property);
        var value = property.GetValue(owner);
        Assert.NotNull(value);
        return value;
    }
}
