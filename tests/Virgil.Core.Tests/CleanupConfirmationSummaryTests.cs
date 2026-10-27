using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupConfirmationSummaryTests
{
    internal static CleanupZonePreview Row(CleanupZoneId id, string name) =>
        new(new(id, name, "", "sandbox", TimeSpan.Zero, CleanupRiskLevel.Low, "", "", "", 0),
            DateTimeOffset.UnixEpoch, 1, 1, 10, 0, [], []);

    [Fact]
    public void States_no_system_or_personal_file_for_caches_and_leftovers()
    {
        var preview = CleanupSelectionAction.CreatePreview([Row(CleanupZoneId.BrowserChromeCache, "Chrome"), Row(CleanupZoneId.SoftwareLeftovers, "Ancien outil")]);
        Assert.Equal("Aucun fichier système ni personnel n'est concerné.", preview.Parameters["NatureSummary"]);
        Assert.Equal("0", preview.Parameters["NatureCount:System"]);
        Assert.Equal("0", preview.Parameters["NatureCount:Personal"]);
        Assert.Equal("1", preview.Parameters["NatureCount:Cache"]);
        Assert.Equal("1", preview.Parameters["NatureCount:SoftwareLeftover"]);
    }

    [Fact]
    public void Lists_personal_and_system_items_when_present()
    {
        var personal = Row(CleanupZoneId.OldDownloads, "Téléchargements");
        var system = Row(CleanupZoneId.WindowsOld, "Ancienne installation Windows");
        var preview = CleanupSelectionAction.CreatePreview([personal, system]);
        Assert.Equal("Concernés : 1 élément(s) personnel(s) (Téléchargements) et 1 élément(s) système sensible (Ancienne installation Windows).", preview.Parameters["NatureSummary"]);
        Assert.Equal("1", preview.Parameters["NatureCount:Personal"]);
        Assert.Equal("1", preview.Parameters["NatureCount:System"]);
        Assert.Equal("Concernés : 1 élément(s) système sensible (Ancienne installation Windows).", CleanupConfirmationSummary.Create([system]));
        Assert.Equal("Concernés : 1 élément(s) personnel(s) (Téléchargements).", CleanupConfirmationSummary.Create([personal]));
    }

    [Fact]
    public void Windows_and_user_TEMP_do_not_warn_about_system_or_personal_files()
    {
        var windows = Row(CleanupZoneId.WindowsTemporaryFiles, "TEMP Windows");
        var user = Row(CleanupZoneId.UserTemporaryFiles, "TEMP utilisateur");
        var preview = CleanupSelectionAction.CreatePreview([windows, user]);
        Assert.Equal("Aucun fichier système ni personnel n'est concerné.", preview.Parameters["NatureSummary"]);
        Assert.Equal("1", preview.Parameters["NatureCount:System"]);
        Assert.Equal("1", preview.Parameters["NatureCount:Cache"]);
        Assert.Equal(CleanupNature.System, CleanupItemNature.For(windows));
        Assert.Equal(CleanupNature.Cache, CleanupItemNature.For(user));
    }

    [Theory]
    [InlineData(CleanupZoneId.WindowsTemporaryFiles)]
    [InlineData(CleanupZoneId.WindowsUpdateCache)]
    [InlineData(CleanupZoneId.DeliveryOptimizationCache)]
    [InlineData(CleanupZoneId.WindowsErrorReports)]
    [InlineData(CleanupZoneId.TechnicalLogs)]
    [InlineData(CleanupZoneId.InstallerTemporaryFiles)]
    [InlineData(CleanupZoneId.PrefetchInformation)]
    public void Managed_Windows_temporaries_and_caches_do_not_raise_summary_alert(CleanupZoneId id)
    {
        var row = Row(id, id.ToString());
        Assert.Equal("Aucun fichier système ni personnel n'est concerné.", CleanupConfirmationSummary.Create([row]));
        Assert.Equal(CleanupNature.System, CleanupItemNature.For(row));
        var personal = row with { Definition = row.Definition with { IsPersonalFolder = true } };
        Assert.Equal($"Concernés : 1 élément(s) personnel(s) ({id}).", CleanupConfirmationSummary.Create([personal]));
    }

    [Fact]
    public void Empty_categories_are_omitted_from_summary()
    {
        var personal = Row(CleanupZoneId.OldDownloads, "Téléchargements");
        var system = Row(CleanupZoneId.WindowsOld, "Ancienne installation Windows");
        Assert.Equal("Concernés : 1 élément(s) personnel(s) (Téléchargements).", CleanupConfirmationSummary.Create([personal]));
        Assert.Equal("Concernés : 1 élément(s) système sensible (Ancienne installation Windows).", CleanupConfirmationSummary.Create([system]));
        Assert.Equal("Aucun fichier système ni personnel n'est concerné.", CleanupConfirmationSummary.Create([]));
    }

    [Fact]
    public void Nature_counts_are_bound_to_the_plan_hash()
    {
        var preview = CleanupSelectionAction.CreatePreview([Row(CleanupZoneId.UserTemporaryFiles, "Temp")]);
        foreach (var key in new[] { "NatureCount:System", "NatureCount:Personal", "NatureCount:Cache", "NatureCount:SoftwareLeftover", "NatureSummary" })
        {
            var parameters = preview.Parameters.ToDictionary(p => p.Key, p => p.Value);
            parameters[key] += " modifié";
            ActionPreview tampered = preview with { Parameters = parameters };
            Assert.NotEqual(ActionPreviewHasher.Hash(preview), ActionPreviewHasher.Hash(tampered));
            var authority = new ConfirmationService(TimeProvider.System);
            var plan = authority.Issue(preview, new ConfirmationConsent(ConfirmationLevel.Simple, null));
            Assert.False(authority.TryConsume(plan, tampered).IsAccepted);
        }
    }
}
