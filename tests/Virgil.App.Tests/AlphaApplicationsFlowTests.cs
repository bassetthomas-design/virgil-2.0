using Virgil.Contracts.Applications;
using Virgil.App.ViewModels;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class AlphaApplicationsFlowTests
{
    [Fact]
    public async Task ProtectedComponentsAreCollapsedAndCannotBeSelected()
    {
        var fixture = new ApplicationsViewModelTests.Fixture();
        fixture.Inventory.Report = new() { Applications =
        [ApplicationsViewModelTests.Safe(), ApplicationsViewModelTests.Safe() with
        { Id = "protected", DisplayName = "Runtime", RiskLevel = ApplicationRiskLevel.Protected, CanUninstall = false, IsSystemComponent = true }] };
        await fixture.Analyze();
        Assert.Single(fixture.Cards);
        var component = Assert.Single(fixture.Vm.ProtectedEntries);
        component.IsSelected = true;
        Assert.False(component.IsSelected);
        Assert.False(component.CanSelect);
        Assert.Empty(fixture.Runner.Requests);
        var document = ApplicationsViewModelTests.ReadXaml();
        System.Xml.Linq.XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var group = Assert.Single(document.Descendants(p + "Expander"));
        Assert.Equal("False", (string?)group.Attribute("IsExpanded"));
    }

    [Fact]
    public async Task SelectionSurvivesFilteringAndBatchUsesOneGlobalPreview()
    {
        var fixture = new ApplicationsViewModelTests.Fixture();
        fixture.Inventory.Report = new() { Applications =
        [ApplicationsViewModelTests.Safe(), ApplicationsViewModelTests.Safe() with { Id = "second", DisplayName = "Second", WingetId = "Example.Second" }] };
        fixture.Runner.Handler = (_, _) => Task.FromResult(new Virgil.Core.Processes.ProcessLaunchResult(false, null, "Échec simulé sans effet."));
        await fixture.Analyze();
        foreach (var card in fixture.Cards) card.IsSelected = true;
        fixture.Vm.SearchText = "Second";
        Assert.True(Assert.Single(fixture.Cards).IsSelected);
        fixture.Dialog.Answers.Enqueue(true);
        await fixture.Vm.UninstallSelectionCommand.ExecuteAsync(null);
        Assert.Single(fixture.Dialog.Calls);
        Assert.Contains("VLC", fixture.Dialog.Calls[0].Message);
        Assert.Contains("Second", fixture.Dialog.Calls[0].Message);
        Assert.Equal(2, fixture.Runner.Requests.Count);
        Assert.All(fixture.Runner.Requests, request => Assert.True(request.WaitForExit));
        Assert.True(fixture.Vm.IsReportOpen);
        Assert.Contains("VLC", fixture.Vm.ReportDetails);
        Assert.Contains("Second", fixture.Vm.ReportDetails);
        Assert.False(fixture.Vm.IsBusy);
    }
}
