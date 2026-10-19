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

    [Fact]
    public void CoreDefinesNoElevatedProtocolOsAdapters()
    {
        var types = typeof(Virgil.Core.Interventions.ElevatedHelperRequestStore).Assembly.GetTypes();
        Assert.DoesNotContain(types, type => type.Name is "ElevatedProtocolRoot" or "ElevatedDiskInspector" or "ElevatedProtocolFiles");
        Assert.Equal(typeof(Virgil.Infrastructure.Windows.ElevatedProtocolRoot).Assembly,
            typeof(Virgil.Infrastructure.Windows.ElevatedDiskInspector).Assembly);
        Assert.NotEqual(typeof(Virgil.Core.Interventions.IElevatedProtocolRoot).Assembly,
            typeof(Virgil.Infrastructure.Windows.ElevatedProtocolRoot).Assembly);
    }

    [Fact]
    public void RequestStoreRequiresExplicitInfrastructurePorts()
    {
        var constructor = Assert.Single(typeof(Virgil.Core.Interventions.ElevatedHelperRequestStore).GetConstructors());
        var parameters = constructor.GetParameters();
        Assert.Contains(parameters, parameter => parameter.ParameterType == typeof(Virgil.Core.Interventions.IElevatedProtocolRoot));
        Assert.Contains(parameters, parameter => parameter.ParameterType == typeof(Virgil.Core.Interventions.IElevatedProtocolFiles));
        Assert.All(parameters, parameter => Assert.False(parameter.IsOptional));
    }

}
