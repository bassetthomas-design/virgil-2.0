using static Virgil.Tests.TestRepositoryRoot;
using Xunit;
using System.Xml.Linq;
using Virgil.App.ViewModels;

namespace Virgil.Tests;

public sealed class CleanupViewArchitectureTests
{
    [Fact]
    public void Cleanup_view_has_no_global_clean_everything_button()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Virgil.App", "Views", "CleanupView.xaml"));

        Assert.DoesNotContain("TOUT NETTOYER", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Binding Categories", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding Selection.IsSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding CanSelect", xaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Détails techniques", xaml, StringComparison.Ordinal);
        Assert.Contains("OperationProgressPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("NETTOYER LA SÉLECTION", xaml, StringComparison.Ordinal);
        Assert.Contains("À REVOIR MANUELLEMENT", xaml, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Cleanup_and_quarantine_details_bind_to_existing_property_names()
    {
        var view = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "Virgil.App", "Views", "CleanupView.xaml"));
        var cleanupDetails = Assert.Single(view.Descendants(), element =>
            element.Name.LocalName == "Expander" && (string?)element.Attribute("Header") == "Détails techniques");
        var quarantine = Assert.Single(view.Descendants(), element =>
            element.Name.LocalName == "ListBox" && (string?)element.Attribute("ItemsSource") == "{Binding QuarantineItems}");

        Assert.Contains(cleanupDetails.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{Binding " + nameof(CleanupSelectionRow.Details) + "}");
        Assert.Contains(quarantine.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{Binding " + nameof(QuarantineItemViewModel.Details) + "}");
    }

}
