using System.Xml.Linq;
using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class Alpha11NettoyageTests
{
    private const string Message = "Phrase de confirmation absente ou incorrecte : la Corbeille et les dossiers personnels n'ont pas été touchés.";

    [Theory]
    [InlineData("")]
    [InlineData("oui")]
    public async Task Phrase_refusee_annule_la_corbeille_et_nettoie_la_zone_temporaire(string phrase)
    {
        var f = CreateFixture(DateTimeOffset.UtcNow);
        await ConfirmSelection(f, phrase);
        Assert.Equal(new[] { "Cancel:RecycleBin", "Execute:UserTemporaryFiles" }, f.Actions.Order);
        Assert.Equal(2, f.Confirmation.Calls);
        Assert.Contains(Message, f.Vm.StatusText);
        Assert.Contains(Message, f.Vm.ReportText);
        Assert.Contains(f.Messages, message => message.Contains(Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Phrase_exacte_et_phrase_refusee_appliquent_des_selections_distinctes()
    {
        var exact = CreateFixture(DateTimeOffset.UtcNow);
        await ConfirmSelection(exact, null);
        Assert.Equal(new[] { "Execute:RecycleBin", "Execute:UserTemporaryFiles" }, exact.Actions.Order);

        var refused = CreateFixture(DateTimeOffset.UtcNow);
        await ConfirmSelection(refused, "");
        Assert.Equal(new[] { "Cancel:RecycleBin", "Execute:UserTemporaryFiles" }, refused.Actions.Order);
    }

    [Fact]
    public async Task Analyse_expiree_ne_presente_pas_de_validation()
    {
        var f = CreateFixture(DateTimeOffset.UtcNow - CleanupExecutionService.PreviewValidity - TimeSpan.FromMinutes(1));
        await f.Analyze();
        f.Vm.SelectAllCommand.Execute(null);
        var pending = f.Run();
        try
        {
            Assert.False(f.Vm.IsValidationOpen);
            await pending;
            Assert.Equal("Analyse expirée : relancez l'analyse avant de nettoyer.", f.Vm.StatusText);
            Assert.Empty(f.Actions.Order);
            Assert.Equal(0, f.Confirmation.Calls);
        }
        finally
        {
            if (f.Vm.IsValidationOpen) f.Vm.CancelDecisionCommand.Execute(null);
            await pending;
        }
    }

    private static async Task ConfirmSelection(CleanupViewModelTests.Fixture f, string? phrase)
    {
        var xml = XDocument.Load(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src/Virgil.App/Views/CleanupView.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var confirmation = Assert.Single(xml.Descendants(p + "Button"), b => (string?)b.Attribute(x + "Name") == "ExecuteZoneButton");
        Assert.Equal("{Binding ExecuteZoneLabel}", (string?)confirmation.Attribute("Content"));
        Assert.Equal("{Binding ExecuteDecisionCommand}", (string?)confirmation.Attribute("Command"));
        Assert.Contains(xml.Descendants(p + "Button"), b => (string?)b.Attribute("Command") == "{Binding SelectAllCommand}");

        await f.Analyze();
        f.Vm.SelectAllCommand.Execute(null);
        var pending = f.Run();
        try
        {
            Assert.True(f.Vm.IsValidationOpen);
            Assert.Equal("CONFIRMER LA SÉLECTION", f.Vm.ExecuteZoneLabel);
            f.Vm.CriticalConfirmationText = phrase ?? f.Vm.ExpectedCriticalConfirmation;
            f.Vm.ExecuteDecisionCommand.Execute(null);
        }
        finally
        {
            if (f.Vm.IsValidationOpen) f.Vm.CancelDecisionCommand.Execute(null);
            await pending;
        }
    }

    private static CleanupViewModelTests.Fixture CreateFixture(DateTimeOffset generatedAt)
    {
        var recycle = Assert.Single(CleanupZoneCatalog.CreateDefault(), z => z.Id == CleanupZoneId.RecycleBin);
        var f = new CleanupViewModelTests.Fixture();
        f.Previews.Items =
        [
            CleanupSelectionTests.Preview(CleanupClassification.Cleanable) with { GeneratedAt = generatedAt },
            new CleanupZonePreview(recycle with { IsPersonalFolder = true }, generatedAt, 3, 3, 1024, 0, [], [])
        ];
        return f;
    }
}
