using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupSelectAllTests
{
    [Fact]
    public async Task Select_all_includes_review_and_leftover_rows()
    {
        var f = new CleanupViewModelTests.Fixture();
        var cache = CleanupSelectionTests.Preview(CleanupClassification.AdvancedCleanable);
        f.Previews.Items = [cache with { Definition = cache.Definition with { NeedsReview = true } },
            cache with { Definition = cache.Definition with { Id = CleanupZoneId.SoftwareLeftovers } }];
        await f.Analyze();
        Assert.All(f.Vm.Groups.SelectMany(g => g.Rows), r => Assert.False(r.IsSelected));
        f.Vm.SelectAllCommand.Execute(null);
        Assert.All(f.Vm.Groups.SelectMany(g => g.Rows), r => Assert.True(r.IsSelected));
        Assert.Empty(f.Actions.Order);
        f.Vm.DeselectAllCommand.Execute(null);
        Assert.All(f.Vm.Groups.SelectMany(g => g.Rows), r => Assert.False(r.IsSelected));
        foreach (var category in f.Vm.Categories) category.Selection.IsSelected = true;
        Assert.All(f.Vm.Groups.SelectMany(g => g.Rows), r => Assert.True(r.IsSelected));
    }

    [Fact]
    public async Task Select_all_excludes_personal_recycle_bin_windows_old_and_non_executable_rows()
    {
        var f = new CleanupViewModelTests.Fixture();
        var cache = CleanupSelectionTests.Preview(CleanupClassification.Cleanable);
        f.Previews.Items = [cache,
            cache with { Definition = cache.Definition with { IsPersonalFolder = true } },
            cache with { Candidates = [new(CleanupZoneId.UserTemporaryFiles, "personal", "personal", 10, DateTimeOffset.UtcNow, true, null) { IsPersonalFolder = true }] },
            cache with { Definition = cache.Definition with { Id = CleanupZoneId.OldDownloads } },
            cache with { Definition = cache.Definition with { Id = CleanupZoneId.RecycleBin } },
            cache with { Definition = cache.Definition with { Id = CleanupZoneId.WindowsOld } },
            cache with { Definition = cache.Definition with { IsExecutable = false } }];
        await f.Analyze();
        f.Vm.SelectAllCommand.Execute(null);
        var rows = f.Vm.Groups.SelectMany(g => g.Rows).ToArray();
        Assert.Single(rows.Where(r => r.IsSelected));
        Assert.All(rows.Where(r => !r.CanGroupSelect), r => Assert.False(r.IsSelected));
        foreach (var category in f.Vm.Categories) category.Selection.IsSelected = true;
        Assert.Single(rows.Where(r => r.IsSelected));
        // Les choix personnels restent possibles individuellement et Tout désélectionner les efface.
        foreach (var row in rows.Where(r => r.CanSelect)) row.IsSelected = true;
        f.Vm.DeselectAllCommand.Execute(null);
        Assert.All(rows, r => Assert.False(r.IsSelected));
    }

    [Fact]
    public async Task Select_all_has_no_effect_before_confirmation()
    {
        foreach (var cancel in new[] { false, true })
        {
            var quarantine = new NoEffectQuarantine();
            var f = new CleanupViewModelTests.Fixture(quarantine: quarantine);
            var cache = CleanupSelectionTests.Preview(CleanupClassification.AdvancedCleanable);
            f.Previews.Items = [cache with { Definition = cache.Definition with { NeedsReview = true } },
                cache with { Definition = cache.Definition with { Id = CleanupZoneId.SoftwareLeftovers } }];
            await f.Analyze();
            f.Vm.SelectAllCommand.Execute(null);
            Assert.Empty(f.Actions.Order);
            Assert.Equal(0, f.Confirmation.Calls);
            Assert.Equal(0, quarantine.Effects);
            var pending = f.Run();
            Assert.True(f.Vm.IsValidationOpen);
            Assert.Equal("Aucun fichier système ni personnel n'est concerné.", f.Vm.ValidationNatureSummary);
            Assert.Empty(f.Actions.Order);
            Assert.Equal(0, f.Confirmation.Calls);
            Assert.Equal(0, quarantine.Effects);
            if (cancel) f.Vm.CancelDecisionCommand.Execute(null);
            else f.Vm.SkipDecisionCommand.Execute(null);
            await pending;
            Assert.DoesNotContain(f.Actions.Order, a => a.StartsWith("Execute:", StringComparison.Ordinal));
            Assert.Equal(0, f.Confirmation.Calls);
            Assert.Equal(0, quarantine.Effects);
        }
    }

    private sealed class NoEffectQuarantine : IQuarantineStore
    {
        public int Effects { get; private set; }
        public IReadOnlyList<QuarantineEntry> List() => [];
        public QuarantineEntry MoveToQuarantine(string path, CleanupZoneDefinition zone, DateTimeOffset now, int retentionDays)
        { Effects++; throw new InvalidOperationException("Effet inattendu"); }
        public string? ValidateRestore(QuarantineEntry entry) => null;
        public void Restore(QuarantineEntry entry) { Effects++; }
        public void Purge(QuarantineEntry entry) { Effects++; }
    }
}
