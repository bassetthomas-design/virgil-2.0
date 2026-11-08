using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupSelectionPolicyTests
{
    [Fact]
    public void Every_zone_requires_an_explicit_decision_and_only_reviewed_safe_zones_are_allowed()
    {
        Assert.Equal(Enum.GetValues<CleanupZoneId>().Order(), CleanupSelectionPolicy.DefaultDecisions.Keys.Order());
        CleanupZoneId[] allowed = [CleanupZoneId.UserTemporaryFiles, CleanupZoneId.UserCrashDumps,
            CleanupZoneId.DirectXShaderCache, CleanupZoneId.WindowsTemporaryFiles, CleanupZoneId.WindowsThumbnailCache,
            CleanupZoneId.WindowsErrorReports, CleanupZoneId.TechnicalLogs, CleanupZoneId.BattleNetCache,
            CleanupZoneId.VisualStudioCache, CleanupZoneId.InternetCache];
        Assert.Equal(allowed.Order(), CleanupSelectionPolicy.DefaultDecisions.Where(d => d.Value).Select(d => d.Key).Order());
        foreach (var id in Enum.GetValues<CleanupZoneId>())
            Assert.Equal(allowed.Contains(id), CleanupSelectionPolicy.IsSelectedByDefault(Preview(id)));
        var unknown = Preview((CleanupZoneId)int.MaxValue);
        Assert.False(CleanupSelectionPolicy.IsSelectedByDefault(unknown));
        Assert.False(CleanupSelectionPolicy.CanGroupSelect(unknown));
    }

    [Fact]
    public void Whitelist_never_overrides_personal_review_execution_or_group_restrictions()
    {
        var safe = Preview(CleanupZoneId.UserTemporaryFiles);
        Assert.True(CleanupSelectionPolicy.IsSelectedByDefault(safe));
        CleanupZonePreview[] refused = [
            safe with { Definition = safe.Definition with { IsPersonalFolder = true } },
            safe with { Candidates = [safe.Candidates[0] with { IsPersonalFolder = true }] },
            safe with { Definition = safe.Definition with { NeedsReview = true } },
            safe with { Candidates = [safe.Candidates[0] with { NeedsReview = true }] },
            safe with { Definition = safe.Definition with { IsExecutable = false } },
            safe with { Definition = safe.Definition with { Classification = CleanupClassification.AdvancedCleanable } },
            safe with { Definition = safe.Definition with { Classification = CleanupClassification.InformationOnly } },
            safe with { EligibleFileCount = 0 }
        ];
        Assert.All(refused, p => Assert.False(CleanupSelectionPolicy.IsSelectedByDefault(p)));
    }

    [Theory]
    [InlineData(CleanupZoneId.OldDownloads)]
    [InlineData(CleanupZoneId.RecycleBin)]
    public void Intrinsically_personal_ids_reach_existing_critical_confirmation_even_without_known_folder_paths(CleanupZoneId id)
    {
        var preview = new PersonalFolderPolicy(new NoFolders()).Apply(Preview(id));
        Assert.True(preview.Definition.IsPersonalFolder);
        Assert.True(Assert.Single(preview.Candidates).IsPersonalFolder);
        Assert.Equal(RiskLevel.Critical, CleanupSelectionAction.CreatePreview([preview]).RiskLevel);
        Assert.False(CleanupSelectionPolicy.CanGroupSelect(preview));
    }

    private static CleanupZonePreview Preview(CleanupZoneId id)
    {
        var zone = new CleanupZoneDefinition(id, id.ToString(), "", Path.GetFullPath("lot-a"), TimeSpan.Zero,
            CleanupRiskLevel.Low, "", "", "", 0);
        return new(zone, DateTimeOffset.UtcNow, 1, 1, 10, 0,
            [new(id, Path.Combine(zone.RootPath, "old.tmp"), "old.tmp", 10, DateTimeOffset.UtcNow, true, null)], []);
    }

    private sealed class NoFolders : IKnownFoldersProvider
    { public IReadOnlyList<KnownFolderPath> GetFolders() => []; }
}
