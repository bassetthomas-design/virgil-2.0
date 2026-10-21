using Virgil.Contracts;
using Virgil.Core.Releases;
using Virgil.Core.Settings;
using Xunit;

namespace Virgil.Tests;
public sealed class VirgilReleaseCheckerTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Settings : ISettingsService
    {
        public VirgilSettings Current { get; set; } = new();
        public bool Fail;
        public Task SaveAsync(VirgilSettings settings, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (Fail) throw new IOException(); Current = settings; return Task.CompletedTask; }
    }
    private sealed class Version(string value) : IApplicationVersionSource { public string ReadInformationalVersion() => value; }
    private sealed class Source : IVirgilReleaseSource
    {
        public int Calls;
        public ReleaseSourceResult Result = new ReleaseSourceResult.Published([
            new(SemanticVersion.Parse("2.0.0-alpha.2")!, DateTimeOffset.UnixEpoch,
                new Uri("https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.2"))]);
        public Task<ReleaseSourceResult> ReadAsync(CancellationToken ct) { Calls++; return Task.FromResult(Result); }
    }
    [Theory]
    [InlineData("2.0.0-alpha", true)][InlineData("2.0.0-alpha.2", false)]
    [InlineData("2.0.0-beta", false)][InlineData("2.0.0", false)]
    public async Task CurrentVersionComesFromPortAndComparisonDeterminesState(string version, bool newer)
    {
        var checker = new VirgilReleaseChecker(new Source(), new(new Version(version)), new Settings(), new Clock());
        var result = await checker.CheckAsync(false, default);
        if (newer) Assert.IsType<ReleaseCheckResult.NewVersion>(result); else Assert.IsType<ReleaseCheckResult.UpToDate>(result);
    }
    [Fact]
    public async Task StartupSavesBeforeRequestAndRetriesOnlyAfter24HoursIncludingFailures()
    {
        var settings = new Settings(); var clock = new Clock(); var source = new Source { Result = new ReleaseSourceResult.Unavailable(ReleaseFailure.ApiLimit) };
        var checker = new VirgilReleaseChecker(source, new(new Version("2.0.0-alpha")), settings, clock);
        Assert.IsType<ReleaseCheckResult.Impossible>(await checker.CheckAsync(true, default));
        Assert.Equal(clock.Now, settings.Current.LastVirgilUpdateCheckUtc);
        clock.Now += TimeSpan.FromHours(24) - TimeSpan.FromTicks(1);
        Assert.Null(await checker.CheckAsync(true, default));
        clock.Now += TimeSpan.FromTicks(1);
        Assert.IsType<ReleaseCheckResult.Impossible>(await checker.CheckAsync(true, default));
        Assert.Equal(2, source.Calls);
    }
    [Fact]
    public async Task DisabledStartupNeverCallsSourceButManualCheckRemainsAvailable()
    {
        var settings = new Settings { Current = new VirgilSettings { CheckVirgilUpdatesAtStartup = false } }; var source = new Source();
        var checker = new VirgilReleaseChecker(source, new(new Version("2.0.0-alpha")), settings, new Clock());
        Assert.Null(await checker.CheckAsync(true, default)); Assert.Equal(0, source.Calls);
        Assert.IsType<ReleaseCheckResult.NewVersion>(await checker.CheckAsync(false, default)); Assert.Equal(1, source.Calls);
    }
    [Fact]
    public async Task ConcurrentStartupChecksOnlyCallOnceAndPersistedTimestampSurvivesNewChecker()
    {
        var settings = new Settings(); var clock = new Clock(); var source = new Source();
        var checker = new VirgilReleaseChecker(source, new(new Version("2.0.0-alpha")), settings, clock);
        await Task.WhenAll(checker.CheckAsync(true, default), checker.CheckAsync(true, default));
        var restarted = new VirgilReleaseChecker(source, new(new Version("2.0.0-alpha")), settings, clock);
        Assert.Null(await restarted.CheckAsync(true, default)); Assert.Equal(1, source.Calls);
    }
    [Fact]
    public async Task InvalidCurrentVersionAndSettingsFailureAreExplicitWithoutNetwork()
    {
        var source = new Source(); var settings = new Settings();
        var invalid = new VirgilReleaseChecker(source, new(new Version("invalid")), settings, new Clock());
        Assert.Equal(ReleaseFailure.InvalidResponse, Assert.IsType<ReleaseCheckResult.Impossible>(await invalid.CheckAsync(false, default)).Reason);
        settings.Fail = true;
        Assert.Equal(ReleaseFailure.SettingsUnavailable, Assert.IsType<ReleaseCheckResult.Impossible>(await invalid.CheckAsync(false, default)).Reason);
        Assert.Equal(0, source.Calls);
    }
    [Fact]
    public void FutureTimestampDoesNotCauseRepeatedRequests() => Assert.False(VirgilReleaseChecker.StartupDue(true, DateTimeOffset.MaxValue, DateTimeOffset.UtcNow));
    [Fact]
    public async Task EmptySourceResultIsExplicit()
    {
        var checker = new VirgilReleaseChecker(new Source { Result = new ReleaseSourceResult.Published([]) }, new(new Version("2.0.0")), new Settings(), new Clock());
        Assert.Equal(ReleaseFailure.NoPublishedRelease, Assert.IsType<ReleaseCheckResult.Impossible>(await checker.CheckAsync(false, default)).Reason);
    }
}
