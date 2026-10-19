using System.Xml.Linq;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

public sealed class ElevatedHelperDependencyArchitectureTests
{
    [Fact]
    public void HelperAssemblyReferencesOnlyContractsAmongVirgilAssemblies()
    {
        var references = typeof(ElevatedActionDispatcher).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.Equal("Virgil.Contracts", Assert.Single(references,
            name => name is not null && name.StartsWith("Virgil.", StringComparison.Ordinal)));
    }

    [Fact]
    public void ContractsAssemblyHasNoWindowsSecurityReferences()
    {
        var references = typeof(Virgil.Contracts.Elevated.ElevatedPathGuard).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("System.Security.AccessControl", references);
        Assert.DoesNotContain("System.IO.FileSystem.AccessControl", references);
        Assert.DoesNotContain("System.Security.Principal.Windows", references);
    }

    [Fact]
    public void HelperProjectReferencesOnlyContracts()
    {
        var root = FindRepositoryRoot();
        var helperDirectory = Path.Combine(root, "src", "Virgil.ElevatedHelper");
        var project = XDocument.Load(Path.Combine(helperDirectory, "Virgil.ElevatedHelper.csproj"));
        var reference = Assert.Single(project.Descendants(),
            element => element.Name.LocalName == "ProjectReference");
        var include = Assert.IsType<string>(reference.Attribute("Include")?.Value);
        var referencedProject = Path.GetFullPath(Path.Combine(helperDirectory,
            include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal(Path.Combine(root, "src", "Virgil.Contracts", "Virgil.Contracts.csproj"), referencedProject);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Virgil.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Virgil.sln introuvable depuis AppContext.BaseDirectory.");
    }
}
