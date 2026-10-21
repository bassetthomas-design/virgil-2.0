using Virgil.Contracts;
using Virgil.Infrastructure.Settings;
using Virgil.Infrastructure.Storage;
using Xunit;

namespace Virgil.Tests;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VirgilTests", Guid.NewGuid().ToString("N"));
    private VirgilPaths Paths => new(_root, _root);

    [Fact]
    public void MissingFileUsesDefaultsWithoutCreatingAnything()
    {
        var service = new JsonSettingsService(Paths);
        Assert.Equal(new VirgilSettings(), service.Current);
        Assert.False(File.Exists(Paths.SettingsFile));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task SaveReloadsValuesAndUpdatesCurrent()
    {
        var service = new JsonSettingsService(Paths);
        var settings = new VirgilSettings { SchemaVersion = 2, MinimumLogLevel = "Debug" };
        await service.SaveAsync(settings, CancellationToken.None);
        Assert.Equal(settings, service.Current);
        Assert.Equal(settings, new JsonSettingsService(Paths).Current);
        Assert.Contains("\n", await File.ReadAllTextAsync(Paths.SettingsFile));
    }

    [Fact]
    public void CorruptJsonIsPreservedAndWarnsWithoutLeakingContent()
    {
        Paths.EnsureSettingsDirectory();
        const string content = "{private-invalid-json";
        File.WriteAllText(Paths.SettingsFile, content);
        var warnings = new List<string>();
        var service = new JsonSettingsService(Paths, warnings.Add);
        Assert.Equal(new VirgilSettings(), service.Current);
        Assert.Equal(content, File.ReadAllText(Paths.SettingsFile));
        Assert.DoesNotContain(content, Assert.Single(warnings));
    }

    [Fact]
    public async Task SuccessfulSaveLeavesNoTemporaryFile()
    {
        await new JsonSettingsService(Paths).SaveAsync(new VirgilSettings(), CancellationToken.None);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Paths.SettingsFile)!, "*.tmp"));
    }

    [Fact]
    public void UnknownLevelFallsBackToInformation()
    {
        Paths.EnsureSettingsDirectory();
        File.WriteAllText(Paths.SettingsFile, "{\"MinimumLogLevel\":\"Unknown\"}");
        Assert.Equal("Information", new JsonSettingsService(Paths).Current.MinimumLogLevel);
    }

    [Fact]
    public async Task CancelledSavePreservesCurrentAndExistingFile()
    {
        var service = new JsonSettingsService(Paths);
        await service.SaveAsync(new VirgilSettings(), CancellationToken.None);
        var content = File.ReadAllText(Paths.SettingsFile);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(
            new VirgilSettings { MinimumLogLevel = "Error" }, new CancellationToken(true)));
        Assert.Equal(content, File.ReadAllText(Paths.SettingsFile));
        Assert.Equal("Information", service.Current.MinimumLogLevel);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Paths.SettingsFile)!, "*.tmp"));
    }

    [Fact]
    public async Task UpdateCheckTimestampAndOptOutSurviveReloadWithOtherSettings()
    {
        var service = new JsonSettingsService(Paths);
        var expected = service.Current with { CheckVirgilUpdatesAtStartup = false,
            LastVirgilUpdateCheckUtc = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), MinimumLogLevel = "Warning" };
        await service.SaveAsync(expected, CancellationToken.None);
        Assert.Equal(expected, new JsonSettingsService(Paths).Current);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
