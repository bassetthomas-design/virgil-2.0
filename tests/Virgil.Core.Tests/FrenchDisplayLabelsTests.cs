using System.Text.Json;
using Virgil.Contracts;
using Virgil.Core.Reports;
using Xunit;

namespace Virgil.Tests;

public sealed class FrenchDisplayLabelsTests
{
    [Theory]
    [InlineData("Non analyse", FrenchDisplayLabels.NotAnalyzed)]
    [InlineData("Non analysé", FrenchDisplayLabels.NotAnalyzed)]
    [InlineData("Inventorie", FrenchDisplayLabels.Inventoried)]
    [InlineData("Eleve", FrenchDisplayLabels.High)]
    [InlineData("modere", FrenchDisplayLabels.Moderate)]
    [InlineData("preview expiree", FrenchDisplayLabels.PreviewExpired)]
    [InlineData("Autre statut", "Autre statut")]
    public void Old_persisted_labels_are_accepted_and_canonicalized(string old, string expected)
    {
        Assert.Equal(expected, FrenchDisplayLabels.NormalizeLegacy(old));
        var report = new ReportSanitizer(string.Empty).Sanitize(new ReportEntry
        {
            Status = old,
            ProposedActions = [new ReportAction { Risk = old, Result = old }]
        });
        Assert.Equal(expected, report.Status);
        Assert.Equal(expected, report.ProposedActions[0].Risk);
        Assert.Equal(expected, report.ProposedActions[0].Result);
    }

    [Theory]
    [InlineData("Point de reanalyse refusé.")]
    [InlineData("Point de réanalyse refusé.")]
    public void Reanalysis_error_count_accepts_old_and_new_labels(string error) =>
        Assert.True(FrenchDisplayLabels.ContainsReanalysis(error));

    [Theory]
    [InlineData("Non analyse")]
    [InlineData("Non analysé")]
    public void Update_defaults_and_old_json_keep_the_same_property_names(string old)
    {
        var json = JsonSerializer.Serialize(new { Status = old });
        var summary = JsonSerializer.Deserialize<UpdateScanSummary>(json)!;
        Assert.Equal(FrenchDisplayLabels.NotAnalyzed, summary.Status);
        Assert.Equal(FrenchDisplayLabels.NotAnalyzed, new UpdateScanReport().OverallStatus);
        Assert.Equal(FrenchDisplayLabels.NotAnalyzed, new InterventionDiagnostic().StateBefore);
        Assert.Contains("\"Status\"", JsonSerializer.Serialize(summary));
    }
}
