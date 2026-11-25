using System.Xml.Linq;
using Virgil.Contracts.Applications;
using Virgil.Core.Processes;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class Alpha11DesinstallationVueTests
{
    [Fact]
    public async Task Details_expliquent_le_blocage_et_le_rapport_affiche_le_code_1605()
    {
        var xml = ApplicationsViewModelTests.ReadXaml();
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var overlay = Assert.Single(xml.Descendants(), e => (string?)e.Attribute(x + "Name") == "ApplicationDetailsOverlay");
        var reason = Assert.Single(overlay.Descendants(p + "TextBlock"), e => (string?)e.Attribute(x + "Name") == "UninstallBlockedReasonText");
        Assert.Equal("{Binding UninstallBlockedReason}", (string?)reason.Attribute("Text"));
        Assert.Equal("{Binding HasUninstallBlockedReason, Converter={StaticResource BoolVisibility}}", (string?)reason.Attribute("Visibility"));
        var button = Assert.Single(overlay.Descendants(p + "Button"), e => (string?)e.Attribute(x + "Name") == "LaunchUninstallButton");
        Assert.Equal("{Binding IsLaunchUninstallEnabled}", (string?)button.Attribute("IsEnabled"));
        Assert.Equal("{Binding LaunchUninstallCommand}", (string?)button.Attribute("Command"));

        var blocked = new ApplicationsViewModelTests.Fixture();
        blocked.Inventory.Report = new()
        {
            Applications = [ApplicationsViewModelTests.Safe() with
            {
                UninstallKind = ApplicationUninstallKind.RegistryUninstallString,
                UninstallCommand = @"C:\Users\Exemple\uninstall.exe"
            }]
        };
        await blocked.Analyze();
        blocked.OpenDetails();
        Assert.False(blocked.Vm.IsLaunchUninstallEnabled);
        Assert.False(string.IsNullOrWhiteSpace(blocked.Vm.UninstallBlockedReason));
        Assert.True(blocked.Vm.HasUninstallBlockedReason);
        Assert.Empty(blocked.Runner.Requests);

        var valid = new ApplicationsViewModelTests.Fixture();
        await valid.Analyze();
        valid.OpenDetails();
        Assert.True(valid.Vm.IsLaunchUninstallEnabled);
        Assert.Empty(valid.Vm.UninstallBlockedReason);
        valid.Dialog.Answers.Enqueue(true);
        valid.Runner.Handler = (_, _) => Task.FromResult(new ProcessLaunchResult(true, 1605));
        await valid.Launch();
        Assert.Single(valid.Runner.Requests);
        Assert.Contains("Produit introuvable pour Windows Installer (code 1605)", valid.Vm.ReportSummary);
        Assert.DoesNotContain("lancé", valid.Vm.ReportSummary, StringComparison.OrdinalIgnoreCase);
    }
}
