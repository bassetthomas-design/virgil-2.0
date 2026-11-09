using Virgil.Contracts.Applications;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class ApplicationsSteamRegressionTests
{
    private const string Empty = "Aucune application sélectionnée. Cochez au moins une application ou un jeu, ou ouvrez ses détails.";
    private const string Protected = "Application protégée. Virgil ne désinstalle pas les composants système, pilotes ni outils de sécurité.";
    private const string Invalid = "Commande de désinstallation officielle invalide ou manquante. Virgil ne lance que les désinstalleurs officiels (Steam, MSI, WinGet, exécutable d'uninstall).";

    [Fact]
    public void MergedFailureLabelIsAbsentFromViewModel()
    {
        var path = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src", "Virgil.App", "ViewModels", "ApplicationsViewModel.cs");
        Assert.DoesNotContain("Sélection vide, protégée ou commande officielle invalide.", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SteamDetailsShowOfficialMethodAndRequireConsent(bool consent)
    {
        var f = new ApplicationsViewModelTests.Fixture();
        f.Inventory.Report = new() { Applications = [new()
        {
            Id = "steam", DisplayName = "Jeu Steam", IsGame = true, CanUninstall = true,
            RiskLevel = ApplicationRiskLevel.SafeToUninstall, UninstallKind = ApplicationUninstallKind.Steam,
            UninstallCommand = "steam://uninstall/2221490"
        }] };
        await f.Analyze();
        f.OpenDetails();
        Assert.Contains("Steam officiel", f.Vm.DetailsBody);
        Assert.Equal("steam://uninstall/2221490", f.Vm.DetailsCommand);
        f.Dialog.Answers.Enqueue(consent);
        await f.Launch();
        Assert.Contains("Steam officiel", Assert.Single(f.Dialog.Calls).Message);
        if (consent)
        {
            Assert.Equal("steam://uninstall/2221490", Assert.Single(f.Runner.Requests).FileName);
            Assert.Contains("Steam a été ouvert", f.Vm.StatusText);
        }
        else
        {
            Assert.Empty(f.Runner.Requests);
            Assert.Equal("Désinstallation annulée.", f.Vm.StatusText);
        }
    }

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
            if (isProtected)
            {
                Assert.False(card.IsSelected); // La carte protégée reste non sélectionnable.
                InjectDefensiveSelection(f, "bad");
            }
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
        InjectDefensiveSelection(f, "protected");
        await f.Vm.UninstallSelectionCommand.ExecuteAsync(null);
        Assert.Equal(Protected, f.Messages.Last());
        Assert.Empty(f.Runner.Requests);
        Assert.Empty(f.Dialog.Calls);
    }

    private static void InjectDefensiveSelection(ApplicationsViewModelTests.Fixture fixture, string id)
    {
        // Entrée défensive devenue protégée : ne jamais rendre les composants sélectionnables
        // pour atteindre la garde du lot. L'UI normale doit continuer de refuser la sélection.
        var field = typeof(Virgil.App.ViewModels.ApplicationsViewModel).GetField("_selectedIds",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var selected = Assert.IsType<HashSet<string>>(field!.GetValue(fixture.Vm));
        selected.Add(id);
    }
}
