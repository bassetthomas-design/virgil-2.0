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
        Assert.Contains("NETTOYAGE SUR", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NETTOYAGE AVANCE", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A REVOIR MANUELLEMENT", xaml, StringComparison.OrdinalIgnoreCase);
    }


}
