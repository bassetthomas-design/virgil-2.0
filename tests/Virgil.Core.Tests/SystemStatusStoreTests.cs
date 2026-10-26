using Virgil.Contracts;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Cleanup;
using Virgil.Core.Diagnostic;
using Virgil.Core.Status;
using Xunit;

namespace Virgil.Tests;

public sealed class SystemStatusStoreTests
{
    [Fact]
    public void DiagnosticPublishesMemorySystemVolumeAndWeightedPrioritiesWithoutErasingCleanup()
    {
        var store = new SystemStatusStore();
        var cleanup = Analysis(DateTimeOffset.UnixEpoch);
        store.PublishCleanup(cleanup);
        var s = DiagnosticAnalysisTests.Healthy();
        s = s with { Disks = [s.Disks[1] with { IsSystemVolume = true }, s.Disks[1] with { Id = "D:" }] };
        var findings = new DiagnosticFindings(s, DiagnosticAnalysisTests.Signals(),
            [new("issue", "Disque", "À examiner", 2, 10, "space"), new("info", "Démarrage", "Information", 0, 0, "startup")], new(90, false), []);
        var changed = 0;
        store.Changed += (_, _) => changed++;

        store.PublishDiagnostic(findings);

        Assert.Equal(50, store.LastMemory!.Value.UsedPercent);
        Assert.Equal("C:", store.LastSystemDisk!.Value.Name);
        Assert.Equal(s.CapturedAtUtc, store.LastMemory.CapturedAt);
        Assert.Equal(1, store.LastPriorities!.Value);
        Assert.Equal(1, changed);
        Assert.Equal(CleanupEstimateService.Summarize(cleanup).PotentialBytes, store.LastCleanupEstimate!.Value.PotentialBytes);
    }

    [Fact]
    public void OlderOrMissingDiagnosticMeasurementsDoNotReplaceLastAvailableValues()
    {
        var store = new SystemStatusStore();
        var s = DiagnosticAnalysisTests.Healthy() with { CapturedAtUtc = DateTimeOffset.UnixEpoch.AddHours(1) };
        store.PublishDiagnostic(DiagnosticAnalyzer.Analyze(s, DiagnosticAnalysisTests.Signals()));
        var first = store.LastMemory;
        store.PublishDiagnostic(DiagnosticAnalyzer.Analyze(s with { CapturedAtUtc = DateTimeOffset.UnixEpoch }, DiagnosticAnalysisTests.Signals()));
        Assert.Same(first, store.LastMemory);
        var missing = DiagnosticValue<double>.Unavailable("Indisponible");
        store.PublishDiagnostic(DiagnosticAnalyzer.Analyze(s with { CapturedAtUtc = s.CapturedAtUtc.AddHours(1),
            Memory = s.Memory with { TotalBytes = missing } }, DiagnosticAnalysisTests.Signals()));
        Assert.Same(first, store.LastMemory);
    }

    [Fact]
    public void SharedEstimateIncludesAdditionalZonesButExcludesInformationAndProtectedItems()
    {
        var analysis = Analysis(DateTimeOffset.UnixEpoch);
        var summary = CleanupEstimateService.Summarize(analysis);
        var store = new SystemStatusStore();
        store.PublishCleanup(analysis);

        Assert.Equal(600, summary.PotentialBytes);
        Assert.Equal(100, summary.SafePotentialBytes);
        Assert.Equal(500, summary.AdvancedPotentialBytes);
        Assert.Equal(3, summary.FileCount);
        Assert.False(summary.ExecutedAnyAction);
        Assert.True(summary.PersonalDataProtected);
        Assert.Equal(summary.PotentialBytes, store.LastCleanupEstimate!.Value.PotentialBytes);
        Assert.Equal(analysis.CapturedAt, store.LastCleanupEstimate.CapturedAt);
        Assert.Equal("Sans risque : 100 o · À vérifier : 500 o · 0 éléments à revoir", SystemStatusText.CleanupDetail(summary));
        Assert.Equal("7,2 Go récupérables", SystemStatusText.CleanupValue(summary with { PotentialBytes = (long)(7.2 * 1024 * 1024 * 1024) }));
    }

    private static CleanupAnalysisReport Analysis(DateTimeOffset at) => new(at,
        [Zone(CleanupZoneId.UserTemporaryFiles, 100, CleanupClassification.Cleanable),
         Zone(CleanupZoneId.OldDownloads, 200, CleanupClassification.AdvancedCleanable),
         Zone(CleanupZoneId.SoftwareLeftovers, 300, CleanupClassification.AdvancedCleanable),
         Zone(CleanupZoneId.PrefetchInformation, 5000, CleanupClassification.InformationOnly),
         Zone(CleanupZoneId.WindowsOld, 6000, CleanupClassification.Protected)],
        new(at, [], [], []), [], []);

    private static CleanupZonePreview Zone(CleanupZoneId id, long bytes, CleanupClassification classification) =>
        new(new(id, id.ToString(), "Zone", "Racine", TimeSpan.Zero, CleanupRiskLevel.Low, "", "", "", 1)
            { Classification = classification }, DateTimeOffset.UnixEpoch, 1, 1, bytes, 0, [], []);
}
