using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class AdditionalCleanupAggregationTests
{
    [Fact]
    public async Task TenThousandDownloadsBecomeOneZoneWithEveryCandidate()
    {
        var root = Path.GetFullPath("downloads-sandbox");
        var folders = new Folders(root);
        var files = Enumerable.Range(0, 10000).Select(i => new FootprintFile(Path.Combine(root, $"cache-{i}.tmp"), 10, DateTimeOffset.UtcNow.AddDays(-100))).ToArray();
        var scanner = new AdditionalCleanupScanner(folders, new Downloads(files), new Source(), new LeftoverAnalyzer(), new PersonalFolderPolicy(folders), TimeProvider.System);
        var preview = Assert.Single(await scanner.ScanAsync(CancellationToken.None));
        Assert.Equal(CleanupZoneId.OldDownloads, preview.Definition.Id);
        Assert.Equal(10000, preview.Candidates.Count);
        Assert.Equal(100000, preview.EligibleBytes);
        Assert.Equal(root, preview.Definition.RootPath);
    }

    [Fact]
    public async Task CancelledPreviewDoesNotReadAnySource()
    {
        var folders = new Folders(Path.GetFullPath("downloads-sandbox"));
        var scanner = new AdditionalCleanupScanner(folders, new Downloads([]), new Source(), new LeftoverAnalyzer(), new PersonalFolderPolicy(folders), TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(new CancellationToken(true)));
    }

    private sealed class Folders(string root) : IKnownFoldersProvider
    { public IReadOnlyList<KnownFolderPath> GetFolders() => [new(KnownFolder.Downloads, root)]; }
    private sealed class Downloads(IReadOnlyList<FootprintFile> files) : IDownloadsSource
    { public IReadOnlyList<FootprintFile> ReadFiles(string root, CancellationToken token) => files; }
    private sealed class Source : ISoftwareFootprintSource
    { public Task<SoftwareFootprintSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(new SoftwareFootprintSnapshot([], [], [])); }
}
