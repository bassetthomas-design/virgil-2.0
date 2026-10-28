using Xunit;

namespace Virgil.Tests;

public sealed class ApplicationsUninstallViewModelSafetyTests
{
    [Fact]
    [Trait("Category", "Windows")]
    public async Task ApplicationsViewModel_DetailsCommandRequiresConfirmationBeforeLaunch()
    {
        var f = new ApplicationsViewModelTests.Fixture();
        await f.Analyze();
        f.OpenDetails();
        Assert.Empty(f.Runner.Requests);
        await f.Launch();
        Assert.Single(f.Dialog.Calls);
        Assert.Empty(f.Runner.Requests);
        f.Dialog.Answers.Enqueue(true);
        await f.Launch();
        Assert.Single(f.Runner.Requests);

        var doc = ApplicationsViewModelTests.ReadXaml();
        System.Xml.Linq.XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var button = Assert.Single(doc.Descendants(p + "Button"), e => (string?)e.Attribute(x + "Name") == "LaunchUninstallButton");
        Assert.Equal("{Binding LaunchUninstallCommand}", (string?)button.Attribute("Command"));
        Assert.Equal("{Binding IsLaunchUninstallEnabled}", (string?)button.Attribute("IsEnabled"));
        Assert.Contains(button.Ancestors(), e => (string?)e.Attribute(x + "Name") == "ApplicationDetailsOverlay");
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task ApplicationsViewModel_CardsOnlyExposeDetailsAndLocationCommands()
    {
        var f = new ApplicationsViewModelTests.Fixture();
        await f.Analyze();
        var card = Assert.Single(f.Cards);
        var commands = card.GetType().GetProperties().Where(p => typeof(CommunityToolkit.Mvvm.Input.IRelayCommand).IsAssignableFrom(p.PropertyType)).Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "DetailsCommand", "OpenLocationCommand" }, commands);
        card.DetailsCommand.Execute(null);
        Assert.True(f.Vm.IsDetailsOpen);
        Assert.Empty(f.Dialog.Calls);
        Assert.Empty(f.Runner.Requests);

        var doc = ApplicationsViewModelTests.ReadXaml();
        System.Xml.Linq.XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var template = Assert.Single(doc.Descendants(p + "DataTemplate"), e => (string?)e.Attribute("DataType") == "{x:Type vm:ApplicationItemViewModel}");
        Assert.Equal(new[] { "{Binding DetailsCommand}", "{Binding OpenLocationCommand}" }, template.Descendants(p + "Button").Select(e => (string?)e.Attribute("Command")).ToArray());
        Assert.DoesNotContain(template.Descendants(p + "Button"), e => ((string?)e.Attribute("Content"))?.Contains("DÉSINSTALL", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(template.Descendants(p + "Button"), e => ((string?)e.Attribute("Command"))?.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) == true);
    }
}
