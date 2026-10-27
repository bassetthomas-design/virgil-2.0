using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupItemNatureTests
{
    [Fact]
    public void Every_zone_id_has_a_nature()
    {
        foreach (var id in Enum.GetValues<CleanupZoneId>())
            Assert.Contains(CleanupItemNature.For(id), Enum.GetValues<CleanupNature>());
        Assert.Throws<ArgumentOutOfRangeException>(() => CleanupItemNature.For((CleanupZoneId)int.MaxValue));
    }

    [Fact]
    public void Personal_folders_and_downloads_are_personal()
    {
        Assert.Equal(CleanupNature.Personal, CleanupItemNature.For(CleanupZoneId.OldDownloads));
        var row = CleanupConfirmationSummaryTests.Row(CleanupZoneId.SoftwareLeftovers, "Documents");
        Assert.Equal(CleanupNature.Personal, CleanupItemNature.For(row with
        { Definition = row.Definition with { IsPersonalFolder = true } }));
        Assert.Equal(CleanupNature.Personal, CleanupItemNature.For(row with
        { Candidates = [new(row.Definition.Id, "file", "file", 1, row.GeneratedAt, true, null) { IsPersonalFolder = true }] }));
    }

    [Fact]
    public void Windows_update_cache_is_system() =>
        Assert.Equal(CleanupNature.System, CleanupItemNature.For(CleanupZoneId.WindowsUpdateCache));

    [Fact]
    public void Software_leftovers_are_leftover() =>
        Assert.Equal(CleanupNature.SoftwareLeftover, CleanupItemNature.For(CleanupZoneId.SoftwareLeftovers));
}
