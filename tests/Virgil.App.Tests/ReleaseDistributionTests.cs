using System.Reflection;
using Virgil.Core.Releases;
using Virgil.Infrastructure.Releases;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;
public sealed class ReleaseDistributionTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), path));
    [Fact]
    public void ApplicationVersionIsReadFromActualAppAssembly()
    {
        var assembly = typeof(Virgil.App.App).Assembly;
        var source = new AssemblyApplicationVersionSource(assembly);
        Assert.Equal(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, source.ReadInformationalVersion());
        Assert.NotNull(new ApplicationVersionService(source).Current);
    }
    [Fact]
    public void PublishVersionAndInstallerVersionHaveSameTagSourceWhileBuildIsDev()
    {
        var release = Read(".github/workflows/release.yml");
        Assert.True(release.IndexOf("- name: Release version", StringComparison.Ordinal) < release.IndexOf("- name: Publish app", StringComparison.Ordinal));
        Assert.Contains("-p:Version=$env:APP_VERSION -p:InformationalVersion=$env:APP_VERSION", release);
        Assert.Contains("-p:IncludeSourceRevisionInInformationalVersion=false", release);
        Assert.Contains("-AppVersion $env:APP_VERSION", release);
        Assert.Contains("-p:Version=0.0.0-dev -p:InformationalVersion=0.0.0-dev", Read(".github/workflows/build.yml"));
    }
    [Fact]
    public void InstallerUsesBothMutexesAndUtf8BomForFrenchUninstallDialogue()
    {
        var bytes = File.ReadAllBytes(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "installer/Virgil.iss"));
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes.Take(3).ToArray());
        Assert.Contains("AppMutex=Local\\Virgil.SingleInstance.v2,Global\\Virgil.Update.v2", Read("installer/Virgil.iss"));
        Assert.Contains("Supprimer", Read("installer/Virgil.iss"));
        var app = Read("src/Virgil.App/AppBootstrap.cs");
        Assert.Contains("_updateSession.TryCreate", app); Assert.Contains("Log.Warning", app); Assert.Contains("_updateSession?.Dispose()", app);
    }
    [Fact]
    public void GlobalMutexCreationFailureIsNonBlockingAndLoggedWithoutExceptionDetails()
    {
        using var guard = new UpdateSessionGuard(() => throw new UnauthorizedAccessException("private")); var messages = new List<string>();
        Assert.False(guard.TryCreate(messages.Add));
        Assert.Contains("Mutex global", Assert.Single(messages)); Assert.DoesNotContain("private", messages[0]);
    }
    [Fact]
    [Trait("Category", "Windows")]
    public void GlobalUpdateMutexCanBeHeldByMultipleSessionsUntilLastHandleCloses()
    {
        // Le namespace Global est partagé entre sessions ; aucun WaitOne exclusif.
        using var first = new UpdateSessionGuard(); using var second = new UpdateSessionGuard();
        Assert.True(first.TryCreate(_ => { })); Assert.True(second.TryCreate(_ => { }));
        first.Dispose();
        using var observed = Mutex.OpenExisting(UpdateSessionGuard.MutexName);
        Assert.NotNull(observed);
        second.Dispose(); second.Dispose();
    }
    [Fact]
    public void ReleaseFeatureHasNoDownloadInstallNotificationOrFocusPath()
    {
        var source = Read("src/Virgil.Infrastructure/Releases/GitHubVirgilReleaseSource.cs");
        Assert.DoesNotContain("browser_download_url", source); Assert.DoesNotContain("File.", source);
        var vm = Read("src/Virgil.App/ViewModels/VirgilReleaseViewModel.cs");
        Assert.DoesNotContain(".RunAsync(", vm); Assert.DoesNotContain("MessageBox", vm);
        Assert.DoesNotContain("Focus(", vm); Assert.DoesNotContain("Activate(", vm);
        var composition = Read("src/Virgil.App/Hosting/ServiceCollectionExtensions.cs");
        Assert.Contains("AllowAutoRedirect = false, UseCookies = false", composition);
        Assert.Contains("Timeout = TimeSpan.FromSeconds(8)", composition);
    }
}
