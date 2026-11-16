using System.Collections;
using Virgil.Contracts;
using Virgil.Core.Status;
using Virgil.Core.Diagnostic;
using Xunit;

namespace Virgil.Tests;

public sealed class LotFPriorityStoreTests
{
    [Fact]
    public void Scan_filters_information_orders_severity_and_keeps_count_and_list_together()
    {
        var store = new SystemStatusStore();
        store.PublishScan(Report(DateTimeOffset.UnixEpoch,
            new("info", "", "Information", "", "0", ScanSeverity.Information, null),
            new("disk-C:", "", "Disque", "", "4 Go", ScanSeverity.Attention, null),
            new("memory-usage", "", "Mémoire", "", "95 %", ScanSeverity.Warning, null)));
        var measured = Read(store, "LastPriorityPoints");
        Assert.Equal(store.LastPriorities!.CapturedAt, Read(measured, "CapturedAt"));
        var points = ((IEnumerable)Read(measured, "Value")).Cast<object>().ToArray();
        Assert.Equal(2, store.LastPriorities.Value); Assert.Equal(2, points.Length);
        Assert.Equal("memory-usage", Read(points[0], "Id")); Assert.Equal("Mémoire", Read(points[0], "Title"));
        Assert.Equal("95 %", Read(points[0], "Value")); Assert.Equal(0, Read(points[0], "Order"));
        Assert.Equal("disk-C:", Read(points[1], "Id"));
    }
    [Fact]
    public void Newer_real_diagnostic_replaces_scan_but_older_measure_does_not()
    {
        var store = new SystemStatusStore(); var at = DateTimeOffset.UnixEpoch.AddDays(1);
        store.PublishScan(Report(at, new ScanFinding("disk-C:", "", "Disque", "", "", ScanSeverity.Warning, null)));
        var snapshot = DiagnosticAnalysisTests.Healthy() with { CapturedAtUtc = at.AddMinutes(1) };
        var findings = DiagnosticAnalyzer.Analyze(snapshot, DiagnosticAnalysisTests.Signals());
        store.PublishDiagnostic(findings);
        var measured = Read(store, "LastPriorityPoints");
        var points = ((IEnumerable)Read(measured, "Value")).Cast<object>().ToArray();
        Assert.Equal(findings.Issues.Where(i => i.Impact > 0).Select(i => i.Id), points.Select(p => (string)Read(p, "Id")));
        Assert.Equal(points.Length, store.LastPriorities!.Value);
        store.PublishScan(Report(at, new ScanFinding("cpu-usage", "", "Ancien", "", "", ScanSeverity.Critical, null)));
        Assert.Same(measured, Read(store, "LastPriorityPoints"));
    }
    private static object Read(object target, string name)
    {
        var property = target.GetType().GetProperty(name); Assert.NotNull(property);
        var value = property.GetValue(target); Assert.NotNull(value); return value;
    }
    private static SystemScanReport Report(DateTimeOffset at, params ScanFinding[] findings) => new(at, ScanMode.Quick,
        TimeSpan.Zero, "", null!, null!, new(0, 0, 0, 0, ScanSeverity.Information, ""), [], [], null!, new(false, 0, 0, [], []), findings, [], []);
}
