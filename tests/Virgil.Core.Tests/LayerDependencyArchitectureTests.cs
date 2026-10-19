using static Virgil.Tests.TestRepositoryRoot;
using Xunit;

namespace Virgil.Tests;

public sealed class LayerDependencyArchitectureTests
{
    [Fact]
    public void CoreContractsAndHelperMustNotReferenceInfrastructure()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();
        foreach (var project in new[] { "Virgil.Core", "Virgil.Contracts", "Virgil.ElevatedHelper" })
        {
            var relative = $"src/{project}/{project}.csproj";
            if (File.ReadAllText(Path.Combine(root, "src", project, project + ".csproj")).Contains("Virgil.Infrastructure", StringComparison.Ordinal))
            {
                violations.Add(relative);
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

}
