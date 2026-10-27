using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupSelectionTests
{
    [Theory]
    [InlineData(CleanupClassification.Cleanable, true)]
    [InlineData(CleanupClassification.AdvancedCleanable, false)]
    public void DefaultsMatchGroup(CleanupClassification classification, bool selected)
    {
        var row = new CleanupSelectionRow(Preview(classification));
        Assert.Equal(selected, row.IsSelected);
    }

    [Theory]
    [InlineData(true, false)] [InlineData(true, true)]
    public void GroupNeverSelectsOrDeselectsPersonalRows(bool personal, bool uncertain)
    {
        var preview = Preview(CleanupClassification.Cleanable);
        var special = preview with { Definition = preview.Definition with { IsPersonalFolder = personal, NeedsReview = uncertain } };
        var group = new CleanupGroupItem(CleanupGroup.Sur, [preview, special]);
        var normal = group.Rows[0]; var excluded = group.Rows[1];
        Assert.False(excluded.IsSelected);
        group.IsSelected = true; Assert.True(normal.IsSelected); Assert.False(excluded.IsSelected);
        excluded.IsSelected = true; group.IsSelected = false;
        Assert.False(normal.IsSelected); Assert.True(excluded.IsSelected);
        Assert.True(excluded.CanSelect);
    }

    [Fact]
    public void GroupCheckboxIsTriStateAndUpdatesFromIndividualRows()
    {
        var group = new CleanupGroupItem(CleanupGroup.Sur, [Preview(CleanupClassification.Cleanable), Preview(CleanupClassification.Cleanable)]);
        Assert.True(group.IsSelected); group.Rows[0].IsSelected = false; Assert.Null(group.IsSelected);
        group.Rows[1].IsSelected = false; Assert.False(group.IsSelected);
        group.IsSelected = true; Assert.True(group.IsSelected);
    }

    [Fact]
    public void SoftwareIsGroupedAcrossRootsWhilePersonalFilesKeepIndividualChoices()
    {
        var preview = Preview(CleanupClassification.AdvancedCleanable);
        var zone = preview.Definition with { Id = CleanupZoneId.SoftwareLeftovers, SoftwareName = "Dofus", DisplayName = "Dofus (désinstallé) : 20 octets, 2 fichiers, 1 clé" };
        var first = preview with { Definition = zone };
        var second = preview with { Definition = zone with { RootPath = Path.GetFullPath("other") } };
        var group = new CleanupGroupItem(CleanupGroup.RestesLogiciels, [first, second]);
        var section = Assert.Single(group.Sections);
        Assert.Contains("Dofus (désinstallé)", section.Title);
        Assert.Equal(2, section.Rows.Count);
        Assert.All(section.Rows, row => Assert.False(row.IsSelected));
        group.IsSelected = true;
        Assert.All(section.Rows, row => Assert.True(row.IsSelected));
    }

    [Fact]
    public void TenThousandRowsToggleInOneBatchWithoutRescanningOnNotifications()
    {
        var group = new CleanupGroupItem(CleanupGroup.Sur, Enumerable.Repeat(Preview(CleanupClassification.Cleanable), 10_000));
        var notifications = 0;
        group.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CleanupGroupItem.IsSelected))
            {
                _ = group.IsSelected;
                notifications++;
            }
        };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        group.IsSelected = false;
        group.IsSelected = true;
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(2, notifications);
        Assert.True(group.IsSelected);
    }

    [Fact]
    public void EmptyRowsAreHiddenAndRowsSortBySize()
    {
        var preview = Preview(CleanupClassification.Cleanable);
        var group = new CleanupGroupItem(CleanupGroup.Sur, new[]
        {
            preview with { EligibleBytes = 0 }, preview, preview with { EligibleBytes = 100 }
        });
        Assert.Equal(new long[] { 100, 10 }, group.Rows.Select(r => r.Preview.EligibleBytes));
    }

    [Fact]
    public void DownloadsAreNotSplitAgainInTheViewModelAndCannotBeGroupSelected()
    {
        var preview = Preview(CleanupClassification.AdvancedCleanable);
        preview = preview with
        {
            Definition = preview.Definition with { Id = CleanupZoneId.OldDownloads, IsPersonalFolder = true },
            Candidates = new[]
            {
                new CleanupCandidate(CleanupZoneId.OldDownloads, "a", "a", 5, DateTimeOffset.UtcNow, true, null),
                new CleanupCandidate(CleanupZoneId.OldDownloads, "b", "b", 5, DateTimeOffset.UtcNow, true, null)
            }
        };
        var group = new CleanupGroupItem(CleanupGroup.AVerifier, new[] { preview });
        var row = Assert.Single(group.Rows);
        group.IsSelected = true;
        Assert.False(row.IsSelected);
        Assert.False(row.CanGroupSelect);
    }

    [Fact]
    public void NewPortsAndPolicyResolveWithValidateOnBuild()
    {
        using var provider = new ServiceCollection().AddVirgilServices().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<Virgil.Infrastructure.Windows.KnownFoldersProvider>(provider.GetRequiredService<IKnownFoldersProvider>());
        Assert.IsType<Virgil.Infrastructure.Windows.SoftwareFootprintSource>(provider.GetRequiredService<ISoftwareFootprintSource>());
        Assert.IsType<Virgil.Infrastructure.Windows.DownloadsSource>(provider.GetRequiredService<IDownloadsSource>());
        Assert.NotNull(provider.GetRequiredService<PersonalFolderPolicy>());
        Assert.NotNull(provider.GetRequiredService<AdditionalCleanupScanner>());
    }

    internal static CleanupZonePreview Preview(CleanupClassification classification) => new(
        new(CleanupZoneId.UserTemporaryFiles, "Temp", "Description", Path.GetFullPath("temp"), TimeSpan.Zero, CleanupRiskLevel.Low, "Attention", "Effet", "Racine", 0)
        { Classification = classification }, DateTimeOffset.UtcNow, 1, 1, 10, 0, [], []);
}

internal sealed class EmptyCleanupSource : ISoftwareFootprintSource, IDownloadsSource, IKnownFoldersProvider, IWindowsOldSource
{
    public CleanupZonePreview? Read(CancellationToken cancellationToken) => null;
    public IReadOnlyList<KnownFolderPath> GetFolders() => [];
    public IReadOnlyList<FootprintFile> ReadFiles(string downloadsRoot, CancellationToken cancellationToken) => [];
    public Task<SoftwareFootprintSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new SoftwareFootprintSnapshot([], [], []));
    public static void Register(IServiceCollection services)
    {
        var source = new EmptyCleanupSource();
        services.AddSingleton<IKnownFoldersProvider>(source);
        services.AddSingleton<ISoftwareFootprintSource>(source);
        services.AddSingleton<IDownloadsSource>(source);
        services.AddSingleton<IWindowsOldSource>(source);
    }
}
