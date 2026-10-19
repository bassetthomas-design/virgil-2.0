using System.Xml.Linq;
using Xunit;

namespace Virgil.Tests;

public sealed class CoreTestsDependencyArchitectureTests
{
    [Fact]
    public void CoreTestsAssemblyDoesNotReferenceApp()
    {
        var references = typeof(CoreTestsDependencyArchitectureTests).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            string.Equals(reference.Name, "Virgil.App", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoreTestsProjectDoesNotReferenceApp()
    {
        var root = FindRepositoryRoot();
        var testDirectory = Path.Combine(root, "tests", "Virgil.Core.Tests");
        var project = XDocument.Load(Path.Combine(testDirectory, "Virgil.Core.Tests.csproj"));
        var references = project.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => Assert.IsType<string>(element.Attribute("Include")?.Value))
            .Select(include => Path.GetFullPath(Path.Combine(testDirectory,
                include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))));
        var appProject = Path.Combine(root, "src", "Virgil.App", "Virgil.App.csproj");

        Assert.DoesNotContain(references, reference =>
            string.Equals(reference, appProject, StringComparison.OrdinalIgnoreCase));
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
