using Virgil.Core.Applications;
using Virgil.Domain.Applications;
using Xunit;

namespace Virgil.Tests;

public sealed class ApplicationsRemnantSafetyTests
{
    [Fact]
    public void Classify_ProtectsPersonalProjectsAndDocuments()
    {
        var classifier = new ApplicationRemnantClassifier();

        var document = classifier.Classify(@"C:\Users\Alice\Documents\Adobe\project.psd", isDirectory: false, 1024);
        var backup = classifier.Classify(@"C:\Users\Alice\AppData\Roaming\Blender\backup", isDirectory: true, null);

        Assert.Equal(ApplicationRemnantKind.ProtectedRemnant, document.Kind);
        Assert.DoesNotContain(ApplicationRemnantAction.DeleteTechnicalOnly, document.AvailableActions);
        Assert.Equal(ApplicationRemnantKind.ProtectedRemnant, backup.Kind);
        Assert.DoesNotContain(ApplicationRemnantAction.DeleteTechnicalOnly, backup.AvailableActions);
    }

    [Fact]
    public void Classify_AllowsOnlyTechnicalRemnantsForManualTechnicalDeleteAction()
    {
        var classifier = new ApplicationRemnantClassifier();

        var cache = classifier.Classify(@"C:\Users\Alice\AppData\Local\Example\cache", isDirectory: true, null);
        var log = classifier.Classify(@"C:\ProgramData\Example\install.log", isDirectory: false, 200);

        Assert.Equal(ApplicationRemnantKind.TechnicalRemnant, cache.Kind);
        Assert.Contains(ApplicationRemnantAction.DeleteTechnicalOnly, cache.AvailableActions);
        Assert.Equal(ApplicationRemnantKind.TechnicalRemnant, log.Kind);
        Assert.Contains(ApplicationRemnantAction.DeleteTechnicalOnly, log.AvailableActions);
    }

    [Fact]
    public void Classify_KeepsAmbiguousAppDataAsManualReview()
    {
        var classifier = new ApplicationRemnantClassifier();

        var remnant = classifier.Classify(@"C:\Users\Alice\AppData\Roaming\ExampleApp\settings", isDirectory: true, null);

        Assert.Equal(ApplicationRemnantKind.UnknownRemnant, remnant.Kind);
        Assert.Contains(ApplicationRemnantAction.MarkReview, remnant.AvailableActions);
        Assert.DoesNotContain(ApplicationRemnantAction.DeleteTechnicalOnly, remnant.AvailableActions);
    }

    [Fact]
    public async Task RemnantScanner_ReadsCandidatesOnlyAndDoesNotDelete()
    {
        var root = Path.Combine(Path.GetTempPath(), "virgil-app-test-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "install.log"), "log");
        try
        {
            var scanner = new ApplicationRemnantScanner();
            var report = await scanner.ScanAsync(
                new InstalledApplication
                {
                    Id = "example",
                    DisplayName = "cache",
                    Publisher = "Example",
                    InstallLocation = cache
                },
                CancellationToken.None);

            Assert.NotEmpty(report.Remnants);
            Assert.True(Directory.Exists(cache));
            Assert.True(File.Exists(Path.Combine(cache, "install.log")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
