using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Cleanup;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupGroupsWindowsTests
{
    [Theory]
    [InlineData(KnownFolder.Pictures)] [InlineData(KnownFolder.Videos)] [InlineData(KnownFolder.Music)]
    [InlineData(KnownFolder.Documents)] [InlineData(KnownFolder.Desktop)] [InlineData(KnownFolder.OneDrive)]
    [Trait("Category", "Windows")]
    public async Task PersonalRemnantNeedsCriticalAndMovesOnlyItsSelectedFileToQuarantine(KnownFolder kind)
    {
        var root = Directory.CreateTempSubdirectory("VirgilPersonal-");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var remnant = Directory.CreateDirectory(Path.Combine(root.FullName, "Dofus"));
            var selected = Path.Combine(remnant.FullName, "old.tmp"); var untouched = Path.Combine(remnant.FullName, "other.tmp");
            File.WriteAllText(selected, "cache"); File.WriteAllText(untouched, "autre");
            var policy = new PersonalFolderPolicy(new Folders(kind, root.FullName));
            var footprint = new SoftwareFootprintCandidate("Dofus", remnant.FullName, root.FullName,
                [new(selected, 5, now)], [@"HKCU\Software\Dofus"], now);
            var preview = policy.Apply(Assert.Single(new LeftoverAnalyzer().Analyze(new([], [], [footprint]), now)));
            var quarantine = new QuarantineStore(Path.Combine(root.FullName, "quarantine"));
            var authority = new ConfirmationService(TimeProvider.System);
            var action = new CleanupSelectionAction(new CleanupExecutionService(new CleanupDisk(), authority, quarantine: quarantine, personal: policy), [preview]);
            var actionPreview = await action.PreviewAsync(CancellationToken.None);
            Assert.Equal(RiskLevel.Critical, actionPreview.RiskLevel);
            Assert.Throws<ConfirmationRefusedException>(() => authority.Issue(actionPreview, new(ConfirmationLevel.Reinforced, null)));
            Assert.True(File.Exists(selected)); Assert.Empty(quarantine.List());
            var plan = authority.Issue(actionPreview, new(ConfirmationLevel.Critical, ConfirmationService.ExpectedCriticalText(actionPreview)));
            var result = Assert.IsType<CleanupSelectionActionResult>(await action.ExecuteAsync(plan, CancellationToken.None));
            Assert.True(result.Succeeded); Assert.False(File.Exists(selected)); Assert.True(File.Exists(untouched));
            Assert.Equal(1, Assert.Single(result.Selection.Steps).QuarantinedFiles);
            Assert.Equal(selected, Assert.Single(quarantine.List()).OriginalPath);
            Assert.True(Directory.Exists(remnant.FullName));
            var replay = await action.ExecuteAsync(plan, CancellationToken.None);
            Assert.True(replay.WasRefused); Assert.Single(quarantine.List()); Assert.True(File.Exists(untouched));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void DownloadsAdapterReadsOnlyContainedFilesAndKeepsDiskIntact()
    {
        var root = Directory.CreateTempSubdirectory("VirgilDownloads-");
        try
        {
            var path = Path.Combine(root.FullName, "old.tmp"); File.WriteAllText(path, "cache");
            var file = Assert.Single(new DownloadsSource().ReadFiles(root.FullName, CancellationToken.None));
            Assert.Equal(path, file.Path); Assert.Equal(5, file.SizeBytes); Assert.True(File.Exists(path));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void KnownFoldersAdapterResolvesAllSixWindowsFolders()
    {
        var result = new KnownFoldersProvider().GetFolders();
        foreach (var kind in Enum.GetValues<KnownFolder>().Where(k => k != KnownFolder.OneDrive))
            Assert.True(Path.IsPathFullyQualified(Assert.Single(result, f => f.Kind == kind).Path));
    }

    private sealed class Folders(KnownFolder kind, string root) : IKnownFoldersProvider
    { public IReadOnlyList<KnownFolderPath> GetFolders() => [new(kind, root)]; }
}
