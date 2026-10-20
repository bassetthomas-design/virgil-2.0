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
    [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void GroupNeverSelectsOrDeselectsPersonalOrUncertainRows(bool personal, bool uncertain)
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
