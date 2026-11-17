using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class LotBureauCleanupTests
{
    private const string IconReason = "Cache d'icônes Windows protégé : le supprimer modifie l'affichage du bureau et de la barre des tâches.";
    private const string ThumbnailReason = "Seules les bases de miniatures thumbcache_*.db directement à la racine sont autorisées.";

    [Fact]
    public async Task T1_Preview_keeps_only_root_thumbnails_eligible_and_explains_exclusions()
    {
        using var files = new Sandbox();
        var preview = await files.Preview();
        Assert.Empty(preview.Errors);
        Assert.Equal(6, preview.ExaminedFileCount);
        Assert.Equal(2, preview.EligibleFileCount);
        Assert.Equal(10L, preview.EligibleBytes);
        Assert.Equal(4, preview.ExcludedFileCount);
        Assert.Equal(new[] { "thumbcache_32.db", "thumbcache_idx.db" },
            preview.Candidates.Where(c => c.IsEligible).Select(c => c.LogicalPath).Order());
        Assert.All(preview.Candidates.Where(c => !c.IsEligible), candidate =>
            Assert.Equal(candidate.LogicalPath.StartsWith("sous", StringComparison.Ordinal) ? ThumbnailReason : IconReason,
                candidate.ExclusionReason));
    }

    [Fact]
    public async Task B1_Icon_cache_injected_after_preview_is_refused_at_execution_with_exact_reason()
    {
        using var files = new Sandbox();
        var preview = await files.Preview();
        var path = files.Write("iconcache_injected.db");
        var injected = new CleanupCandidate(preview.Definition.Id, path, "iconcache_injected.db", 5,
            DateTimeOffset.UtcNow.AddDays(-30), true, null);
        preview = preview with { Candidates = [injected], ExaminedFileCount = 1, EligibleFileCount = 1, EligibleBytes = 5, ExcludedFileCount = 0 };
        var effects = new RecordingStorage();
        var result = await new CleanupExecutionService(effects, TestPlans.Authority, quarantine: effects)
            .ExecuteZoneAsync(preview, TestPlans.Cleanup(preview), null, CancellationToken.None);
        Assert.Equal(0, result.QuarantinedFiles);
        Assert.Equal(1, result.SkippedFiles);
        // L'exécuteur compte les refus mais n'expose pas leur motif ; vérifier la même garde au moment de l'exécution.
        Assert.False(new CleanupSafetyClassifier().CanDeleteCandidate(preview.Definition, path, out var reason));
        Assert.Equal(IconReason, reason);
        Assert.Empty(result.Errors);
        Assert.Empty(effects.Quarantined);
        Assert.Empty(effects.Read);
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task T1_Confirmed_execution_never_touches_exclusions_even_if_preview_is_forged(bool forgeEligibility)
    {
        using var files = new Sandbox();
        var preview = await files.Preview();
        if (forgeEligibility)
        {
            preview = preview with
            {
                Candidates = preview.Candidates.Select(c => c with { IsEligible = true, ExclusionReason = null }).ToArray()
            };
        }
        var effects = new RecordingStorage();
        var service = new CleanupExecutionService(effects, TestPlans.Authority, quarantine: effects);
        var result = await service.ExecuteZoneAsync(preview, TestPlans.Cleanup(preview), null, CancellationToken.None);
        Assert.Equal(CleanupStepStatus.Completed, result.Status);
        Assert.Equal(2, result.QuarantinedFiles);
        var expected = new[] { Path.Combine(files.Root, "thumbcache_32.db"), Path.Combine(files.Root, "thumbcache_idx.db") };
        Assert.Equal(expected, effects.Quarantined.Order());
        Assert.Equal(expected, effects.Read.Order());
        Assert.All(preview.Candidates, c => Assert.True(File.Exists(c.FullPath)));
    }

    [Theory]
    [InlineData("iconcache.db")]
    [InlineData("iconcache_32.db")]
    [InlineData("ICONCACHE_idx.DB")]
    [InlineData("iconcache-custom.db")]
    [InlineData("Desktop/cache.db")]
    [InlineData("Desktop/nested/cache.db")]
    [InlineData("Public/Desktop/cache.db")]
    [InlineData("Microsoft/Internet Explorer/Quick Launch/cache.db")]
    [InlineData("Microsoft/Internet Explorer/Quick Launch/User Pinned/TaskBar/cache.db")]
    [InlineData("Microsoft/Windows/Shell/cache.db")]
    [InlineData("mIcRoSoFt/wInDoWs/sHeLl/nested/cache.db")]
    public void T2_Shell_files_are_refused_in_every_executable_zone(string relative)
    {
        using var files = new Sandbox();
        var path = files.Write(relative);
        foreach (var definition in CleanupZoneCatalog.CreateDefault().Where(z => z.IsExecutable))
        {
            var zone = definition with { RootPath = files.Root, AllowedExtensions = new[] { ".db" } };
            Assert.False(new CleanupSafetyClassifier().CanDeleteCandidate(zone, path, out var reason));
            Assert.Equal(relative.StartsWith("iconcache", StringComparison.OrdinalIgnoreCase) ? IconReason :
                "Bureau, raccourcis épinglés et données du shell Windows protégés.", reason);
        }
    }

    [Theory]
    [InlineData("Desktop-copy/cache.db")]
    [InlineData("Microsoft/Windows/Shell-copy/cache.db")]
    [InlineData("Microsoft/Internet Explorer/Quick Launch-copy/cache.db")]
    [InlineData("my-iconcache.db")]
    [InlineData("iconcache.tmp")]
    public void T2_Segment_boundaries_preserve_unrelated_technical_files(string relative)
    {
        using var files = new Sandbox();
        var zone = CleanupZoneCatalog.CreateDefault().Single(z => z.Id == CleanupZoneId.UserTemporaryFiles) with { RootPath = files.Root };
        Assert.True(new CleanupSafetyClassifier().CanDeleteCandidate(zone, files.Write(relative), out _));
    }

    [Fact]
    public void T6_Source_has_no_shell_mutation_markers_outside_the_path_guard()
    {
        var src = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src");
        var markers = new[] { "Taskband", "User Pinned", "Quick Launch", "IconStreams", "ie4uinit", "SHChangeNotify" };
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
                     .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj")))
        {
            foreach (var line in File.ReadLines(file))
            foreach (var marker in markers)
            {
                if (Path.GetFileName(file) == "CleanupSafetyClassifier.cs" && marker == "Quick Launch" &&
                    line.Trim() == "@\"\\microsoft\\internet explorer\\quick launch\\\",") continue;
                Assert.False(line.Contains(marker, StringComparison.OrdinalIgnoreCase), $"{file}: {line}");
            }
        }
    }

    [Fact]
    public void T6_Default_catalog_does_not_target_desktop_or_pinned_shortcuts()
    {
        Assert.All(CleanupZoneCatalog.CreateDefault(), zone =>
        {
            var segments = zone.RootPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            Assert.DoesNotContain(segments, s => s.Equals("Desktop", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(segments, s => s.Equals("Quick Launch", StringComparison.OrdinalIgnoreCase));
        });
    }

    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "virgil-lot-bureau", Guid.NewGuid().ToString("N"));
        public Sandbox()
        {
            foreach (var name in new[] { "thumbcache_32.db", "thumbcache_idx.db", "iconcache_32.db", "iconcache_idx.db", "autre.db", "sous/thumbcache_96.db" }) Write(name);
        }
        public string Write(string relative)
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "cache");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-30));
            return path;
        }
        public async Task<CleanupZonePreview> Preview()
        {
            var zone = CleanupZoneCatalog.CreateDefault().Single(z => z.Id == CleanupZoneId.WindowsThumbnailCache) with { RootPath = Root };
            return Assert.Single(await new CleanupPreviewService([zone]).PreviewAsync(null, CancellationToken.None));
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class RecordingStorage : ICleanupDisk, IQuarantineStore
    {
        public List<string> Read { get; } = new();
        public List<string> Quarantined { get; } = new();
        public IRecycleBinService RecycleBin => throw new InvalidOperationException();
        public CleanupFileState ReadFile(string path) { Read.Add(path); return new(5, DateTimeOffset.UtcNow.AddDays(-30)); }
        public QuarantineEntry MoveToQuarantine(string path, CleanupZoneDefinition zone, DateTimeOffset now, int retentionDays)
        {
            Quarantined.Add(path);
            return new(Guid.NewGuid(), path, zone, 5, "test", now, now.AddDays(retentionDays));
        }
        public void DeleteFile(string path) => throw new InvalidOperationException();
        public IReadOnlyList<string> GetDirectories(string path) => throw new InvalidOperationException();
        public bool HasEntries(string path) => throw new InvalidOperationException();
        public void DeleteEmptyDirectory(string path) => throw new InvalidOperationException();
        public IReadOnlyList<QuarantineEntry> List() => throw new InvalidOperationException();
        public string? ValidateRestore(QuarantineEntry entry) => throw new InvalidOperationException();
        public void Restore(QuarantineEntry entry) => throw new InvalidOperationException();
        public void Purge(QuarantineEntry entry) => throw new InvalidOperationException();
    }
}
