using static Virgil.Tests.TestRepositoryRoot;
using Xunit;

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
        Assert.Contains("A REVOIR MANUELLEMENT", xaml, StringComparison.OrdinalIgnoreCase);
    }


}
