using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupLotAReproductionTests
{
    [Fact]
    public async Task Capture_downloads_remain_personal_after_each_scan()
    {
        var ports = new CapturePorts();
        var scanner = new AdditionalCleanupScanner(ports, ports, ports, new LeftoverAnalyzer(),
            new PersonalFolderPolicy(ports), TimeProvider.System);
        for (var scan = 0; scan < 2; scan++)
        {
            var downloads = Assert.Single(await scanner.ScanAsync(CancellationToken.None));
            Assert.Equal(CleanupZoneId.OldDownloads, downloads.Definition.Id);
            Assert.Equal(CleanupNature.Personal, CleanupItemNature.For(downloads));
            Assert.Equal(CleanupGroup.AVerifier, downloads.Definition.Group);
            Assert.Equal(4, downloads.EligibleFileCount);
            Assert.Equal(237921894L, downloads.EligibleBytes);
            Assert.True(downloads.HasEligibleCandidates);
        }
    }

    [Theory]
    [InlineData(KnownFolder.Documents)]
    [InlineData(KnownFolder.Desktop)]
    [InlineData(KnownFolder.Pictures)]
    [InlineData(KnownFolder.Videos)]
    [InlineData(KnownFolder.Music)]
    [InlineData(KnownFolder.OneDrive)]
    public void Each_known_personal_folder_marks_preview_and_candidates(KnownFolder kind)
    {
        var ports = new CapturePorts(kind);
        var root = ports.GetFolders()[0].Path;
        var zone = new CleanupZoneDefinition(CleanupZoneId.UserTemporaryFiles, "Personnel", "", root,
            TimeSpan.Zero, CleanupRiskLevel.Low, "", "", "", 0);
        var candidate = new CleanupCandidate(zone.Id, Path.Combine(root, "private.tmp"), "private.tmp", 10,
            DateTimeOffset.UtcNow.AddDays(-100), true, null);
        var preview = new CleanupZonePreview(zone, DateTimeOffset.UtcNow, 1, 1, 10, 0, [candidate], []);
        var result = new PersonalFolderPolicy(ports).Apply(preview);
        Assert.True(result.Definition.IsPersonalFolder);
        Assert.True(Assert.Single(result.Candidates).IsPersonalFolder);
        Assert.Equal(CleanupNature.Personal, CleanupItemNature.For(result));
    }

    private sealed class CapturePorts(KnownFolder kind = KnownFolder.Downloads) : IKnownFoldersProvider, IDownloadsSource, ISoftwareFootprintSource
    {
        public IReadOnlyList<KnownFolderPath> GetFolders() => [new(kind, Path.GetFullPath("lot-a-downloads"))];
        public IReadOnlyList<FootprintFile> ReadFiles(string downloadsRoot, CancellationToken cancellationToken) =>
            Enumerable.Range(0, 4).Select(i => new FootprintFile(Path.Combine(downloadsRoot, $"old-{i}.tmp"),
                i == 0 ? 59480472 : 59480474, DateTimeOffset.UtcNow.AddDays(-100))).ToArray();
        public Task<SoftwareFootprintSnapshot> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SoftwareFootprintSnapshot([], [], []));
    }
}
