using Virgil.Contracts.Applications;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class ApplicationsSteamRegressionTests
{
    private const string Empty = "Aucune application sélectionnée. Cochez au moins une application ou un jeu, ou ouvrez ses détails.";
    private const string Protected = "Application protégée. Virgil ne désinstalle pas les composants système, pilotes ni outils de sécurité.";
    private const string Invalid = "Commande de désinstallation officielle invalide ou manquante. Virgil ne lance que les désinstalleurs officiels (Steam, MSI, WinGet, exécutable d'uninstall).";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptySelectionAndMissingDetailsHaveSameExactMessage(bool batch)
    {
        var f = new ApplicationsViewModelTests.Fixture();
        await f.Analyze();
        if (batch) await f.Vm.UninstallSelectionCommand.ExecuteAsync(null);
        else await f.Launch();
        Assert.Equal(Empty, f.Messages.Last());
        Assert.Empty(f.Runner.Requests);
        Assert.Empty(f.Dialog.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ProtectedAndInvalidCausesAreDistinctInDetailsAndBatch(bool batch, bool isProtected)
    {
        var f = new ApplicationsViewModelTests.Fixture();
        f.Inventory.Report = new() { Applications = [new()
        {
            Id = "bad", DisplayName = "Application test", CanUninstall = !isProtected,
            RiskLevel = isProtected ? ApplicationRiskLevel.Protected : ApplicationRiskLevel.SafeToUninstall,
            UninstallKind = ApplicationUninstallKind.RegistryUninstallString,
            UninstallCommand = "steam://uninstall/abc"
        }] };
        await f.Analyze();
        f.Vm.SelectedFilter = "TOUT";
        var card = Assert.Single(f.Cards);
        if (batch)
        {
            card.IsSelected = true;
            await f.Vm.UninstallSelectionCommand.ExecuteAsync(null);
        }
        else { card.DetailsCommand.Execute(null); await f.Launch(); }
        Assert.Equal(isProtected ? Protected : Invalid, f.Messages.Last());
        Assert.Empty(f.Runner.Requests);
        Assert.Empty(f.Dialog.Calls);
    }

    [Fact]
    public async Task ProtectedCauseHasPriorityOverInvalidCommandInBatch()
    {
        var f = new ApplicationsViewModelTests.Fixture();
        f.Inventory.Report = new() { Applications = [
            new() { Id = "invalid", DisplayName = "A invalide", CanUninstall = true,
                RiskLevel = ApplicationRiskLevel.SafeToUninstall, UninstallCommand = "garbage",
                UninstallKind = ApplicationUninstallKind.RegistryUninstallString },
            new() { Id = "protected", DisplayName = "Z protégé", RiskLevel = ApplicationRiskLevel.Protected }
        ] };
        await f.Analyze();
        f.Vm.SelectedFilter = "TOUT";
        foreach (var card in f.Cards) card.IsSelected = true;
        await f.Vm.UninstallSelectionCommand.ExecuteAsync(null);
        Assert.Equal(Protected, f.Messages.Last());
        Assert.Empty(f.Runner.Requests);
        Assert.Empty(f.Dialog.Calls);
    }
}
