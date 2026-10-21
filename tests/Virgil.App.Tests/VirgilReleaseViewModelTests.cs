using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Releases;
using Virgil.Core.Settings;
using Xunit;

namespace Virgil.Tests;
public sealed class VirgilReleaseViewModelTests
{
    private sealed class Settings : ISettingsService
    {
        public VirgilSettings Current { get; private set; } = new();
        public Task SaveAsync(VirgilSettings settings, CancellationToken ct) { Current = settings; return Task.CompletedTask; }
    }
    private sealed class Version : IApplicationVersionSource { public string ReadInformationalVersion() => "2.0.0-alpha"; }
    private sealed class Source(ReleaseSourceResult result) : IVirgilReleaseSource
    {
        public int Calls;
        public Task<ReleaseSourceResult> ReadAsync(CancellationToken ct) { Calls++; return Task.FromResult(result); }
    }
    private sealed class Runner : IProcessRunner
    {
        public readonly List<ProcessLaunchRequest> Launches = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct) => throw new InvalidOperationException("Aucune commande d'installation autorisée.");
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken ct)
        { Launches.Add(request); return Task.FromResult(new ProcessLaunchResult(true, null)); }
    }
    private static VirgilReleaseViewModel Create(Source source, Runner runner, Settings settings)
    {
        var version = new ApplicationVersionService(new Version());
        return new(new VirgilReleaseChecker(source, version, settings, TimeProvider.System), version, settings, runner);
    }
    [Fact]
    public async Task StartupAndManualCheckNeverLaunchAnythingUntilExplicitReleasePageCommand()
    {
        var url = new Uri("https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-beta");
        var source = new Source(new ReleaseSourceResult.Published([new(SemanticVersion.Parse("2.0.0-beta")!, DateTimeOffset.UnixEpoch, url)]));
        var runner = new Runner(); using var vm = Create(source, runner, new Settings());
        Assert.False(vm.OpenReleaseCommand.CanExecute(null));
        await vm.StartAsync(); await vm.StartAsync();
        Assert.Equal(1, source.Calls); Assert.Empty(runner.Launches);
        Assert.Contains("Nouvelle version disponible : 2.0.0-beta", vm.Status);
        Assert.Contains("01/01/1970", vm.Status);
        await vm.CheckCommand.ExecuteAsync(null);
        Assert.Equal(2, source.Calls); Assert.Empty(runner.Launches);
        await vm.OpenReleaseCommand.ExecuteAsync(null);
        var launch = Assert.Single(runner.Launches);
        Assert.Equal(url.AbsoluteUri, launch.FileName); Assert.True(launch.UseShellExecute);
        Assert.Empty(launch.Arguments); Assert.Null(launch.RawArguments); Assert.Null(launch.Verb); Assert.False(launch.WaitForExit);
    }
    [Theory]
    [InlineData(ReleaseFailure.OfflineOrTimeout, "hors ligne")]
    [InlineData(ReleaseFailure.ApiLimit, "403/429")]
    [InlineData(ReleaseFailure.InvalidResponse, "invalide")]
    [InlineData(ReleaseFailure.NoPublishedRelease, "aucune release publiée")]
    [InlineData(ReleaseFailure.Cancelled, "annulée")]
    [InlineData(ReleaseFailure.SettingsUnavailable, "paramètres indisponibles")]
    public async Task EveryFailureIsDisplayedInFrenchAndCannotOpenOrInstall(ReleaseFailure reason, string expected)
    {
        var runner = new Runner(); using var vm = Create(new Source(new ReleaseSourceResult.Unavailable(reason)), runner, new Settings());
        await vm.CheckCommand.ExecuteAsync(null);
        Assert.Contains("Vérification impossible", vm.Status); Assert.Contains(expected, vm.Status);
        Assert.False(vm.OpenReleaseCommand.CanExecute(null)); Assert.Empty(runner.Launches);
    }
    [Fact]
    public async Task DisablingIsSavedAndManualCheckStillWorks()
    {
        var settings = new Settings(); var source = new Source(new ReleaseSourceResult.Published([])); var runner = new Runner();
        using var vm = Create(source, runner, settings);
        vm.CheckAtStartup = false; await vm.SavePreferenceCommand.ExecuteAsync(null);
        Assert.False(settings.Current.CheckVirgilUpdatesAtStartup);
        await vm.StartAsync(); Assert.Equal(0, source.Calls);
        await vm.CheckCommand.ExecuteAsync(null); Assert.Equal(1, source.Calls);
        Assert.Contains("aucune release publiée", vm.Status); Assert.Empty(runner.Launches);
    }
    [Fact]
    public async Task UpToDateIsExplicitAndOffersNoReleasePage()
    {
        var source = new Source(new ReleaseSourceResult.Published([new(SemanticVersion.Parse("2.0.0-alpha")!, DateTimeOffset.UnixEpoch,
            new Uri("https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha"))]));
        using var vm = Create(source, new Runner(), new Settings());
        await vm.CheckCommand.ExecuteAsync(null);
        Assert.StartsWith("À jour.", vm.Status); Assert.False(vm.OpenReleaseCommand.CanExecute(null));
        vm.Dispose(); // Fermeture de la fenêtre puis libération par DI : idempotent.
    }
}
