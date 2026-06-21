using Virgil.Core.Cleanup;
using Virgil.Domain;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupV2SafetyTests
{
    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("portrait.heic")]
    [InlineData("rush.raw")]
    [InlineData("video.mp4")]
    [InlineData("souvenir.mkv")]
    [InlineData("document.docx")]
    [InlineData("budget.xlsx")]
    [InlineData("presentation.pptx")]
    [InlineData("contrat.pdf")]
    [InlineData("scene.blend")]
    [InlineData("design.psd")]
    [InlineData("montage.prproj")]
    public void Personal_extensions_are_protected(string name)
    {
        Assert.Equal(CleanupClassification.Protected, new CleanupSafetyClassifier().ClassifyPath(Path.Combine("C:\\Review", name)));
    }

    [Theory]
    [InlineData("bundle.zip")]
    [InlineData("archive.rar")]
    [InlineData("sources.7z")]
    [InlineData("systeme.iso")]
    public void Archives_and_iso_are_review_only(string name)
    {
        Assert.Equal(CleanupClassification.ReviewOnly, new CleanupSafetyClassifier().ClassifyPath(Path.Combine("C:\\Review", name)));
    }

    [Theory]
    [InlineData("Projet client")]
    [InlineData("Backup famille")]
    [InlineData("Photos bebe")]
    [InlineData("Jeux")]
    [InlineData("Travail")]
    public void Sensitive_folder_names_are_protected(string name)
    {
        Assert.Equal(CleanupClassification.Protected, new CleanupSafetyClassifier().ClassifyPath(Path.Combine("C:\\Review", name), true));
    }

    [Fact]
    public void ProgramFiles_is_protected()
    {
        Assert.Equal(CleanupClassification.Protected, new CleanupSafetyClassifier().ClassifyPath("C:\\Program Files\\Application\\cache.dat"));
    }

    [Fact]
    public void Ambiguous_AppData_is_never_classified_cleanable_by_path_alone()
    {
        Assert.Equal(CleanupClassification.ReviewOnly, new CleanupSafetyClassifier().ClassifyPath("C:\\Users\\Test\\AppData\\Local\\Unknown\\data.bin"));
    }

    [Fact]
    public void Catalog_contains_all_four_strict_categories()
    {
        var zones = CleanupZoneCatalog.CreateDefault();
        Assert.Contains(zones, zone => zone.Classification == CleanupClassification.Cleanable);
        Assert.Contains(zones, zone => zone.Classification == CleanupClassification.AdvancedCleanable);
        Assert.Contains(zones, zone => zone.Classification == CleanupClassification.InformationOnly);

        var classifier = new CleanupSafetyClassifier();
        Assert.Equal(CleanupClassification.ReviewOnly, classifier.ClassifyPath("C:\\Review\\unknown.bin"));
        Assert.Equal(CleanupClassification.Protected, classifier.ClassifyPath("C:\\Review\\photo.jpg"));
    }

    [Theory]
    [InlineData(CleanupZoneId.BrowserEdgeCache)]
    [InlineData(CleanupZoneId.BrowserChromeCache)]
    [InlineData(CleanupZoneId.BrowserFirefoxCache)]
    [InlineData(CleanupZoneId.BrowserBraveCache)]
    [InlineData(CleanupZoneId.BrowserOperaCache)]
    public void Browser_cache_zones_are_advanced_and_reinforced(CleanupZoneId id)
    {
        var zone = Assert.Single(CleanupZoneCatalog.CreateDefault(), zone => zone.Id == id);
        Assert.Equal(CleanupClassification.AdvancedCleanable, zone.Classification);
        Assert.True(zone.RequiresReinforcedConfirmation);
        Assert.True(zone.IsExecutable);
    }

    [Theory]
    [InlineData("Login Data")]
    [InlineData("Cookies")]
    [InlineData("Bookmarks")]
    [InlineData("History")]
    [InlineData("Sessions\\session.dat")]
    [InlineData("Extensions\\extension.dat")]
    public void Browser_personal_data_is_not_cleanable(string relativePath)
    {
        using var sandbox = Sandbox.Create();
        var file = sandbox.Write(relativePath);
        var zone = TestZone(sandbox.Root) with
        {
            Id = CleanupZoneId.BrowserChromeCache,
            Classification = CleanupClassification.AdvancedCleanable,
            RequiresReinforcedConfirmation = true
        };

        Assert.False(new CleanupSafetyClassifier().CanDeleteCandidate(zone, file, out _));
    }

    [Fact]
    public void Browser_technical_cache_file_is_cleanable_inside_exact_root()
    {
        using var sandbox = Sandbox.Create();
        var file = sandbox.Write("Cache_Data\\f_00001");
        var zone = TestZone(sandbox.Root) with
        {
            Id = CleanupZoneId.BrowserChromeCache,
            Classification = CleanupClassification.AdvancedCleanable,
            RequiresReinforcedConfirmation = true
        };

        Assert.True(new CleanupSafetyClassifier().CanDeleteCandidate(zone, file, out _));
    }

    [Fact]
    public void Prefetch_is_information_only_and_never_executable()
    {
        var zone = Assert.Single(CleanupZoneCatalog.CreateDefault(), zone => zone.Id == CleanupZoneId.PrefetchInformation);
        Assert.Equal(CleanupClassification.InformationOnly, zone.Classification);
        Assert.False(zone.IsExecutable);
    }

    [Theory]
    [InlineData(CleanupZoneId.WindowsUpdateCache)]
    [InlineData(CleanupZoneId.DeliveryOptimizationCache)]
    [InlineData(CleanupZoneId.MicrosoftStoreCache)]
    [InlineData(CleanupZoneId.WindowsOld)]
    public void Sensitive_advanced_zones_are_information_only_until_reliable(CleanupZoneId id)
    {
        var zone = Assert.Single(CleanupZoneCatalog.CreateDefault(), zone => zone.Id == id);
        Assert.Equal(CleanupClassification.AdvancedCleanable, zone.Classification);
        Assert.True(zone.RequiresReinforcedConfirmation);
        Assert.False(zone.IsExecutable);
    }

    [Fact]
    public void Recycle_bin_is_advanced_executable_and_reinforced()
    {
        var zone = Assert.Single(CleanupZoneCatalog.CreateDefault(), zone => zone.Id == CleanupZoneId.RecycleBin);
        Assert.Equal(CleanupClassification.AdvancedCleanable, zone.Classification);
        Assert.True(zone.IsExecutable);
        Assert.True(zone.RequiresReinforcedConfirmation);
    }

    [Fact]
    public async Task Recycle_bin_preview_and_execution_use_injected_service_only()
    {
        var fake = new FakeRecycleBinService(new RecycleBinState(true, 3, 1024));
        var zone = Assert.Single(CleanupZoneCatalog.CreateDefault(), zone => zone.Id == CleanupZoneId.RecycleBin);
        var previewService = new CleanupPreviewService(new[] { zone }, recycleBinService: fake);
        var preview = Assert.Single(await previewService.PreviewAsync(null, CancellationToken.None));

        var result = await new CleanupExecutionService(() => DateTimeOffset.Now, fake)
            .ExecuteZoneAsync(preview, null, CancellationToken.None);

        Assert.Equal(3, preview.EligibleFileCount);
        Assert.Equal(1024, preview.EligibleBytes);
        Assert.Equal(CleanupStepStatus.Completed, result.Status);
        Assert.Equal(1, fake.EmptyCalls);
    }

    [Fact]
    public async Task Recycle_bin_estimation_failure_does_not_throw_or_execute()
    {
        var fake = new FakeRecycleBinService(new RecycleBinState(false, 0, 0, "indisponible"));
        var zone = Assert.Single(CleanupZoneCatalog.CreateDefault(), zone => zone.Id == CleanupZoneId.RecycleBin);
        var service = new CleanupPreviewService(new[] { zone }, recycleBinService: fake);

        var preview = Assert.Single(await service.PreviewAsync(null, CancellationToken.None));

        Assert.False(preview.HasEligibleCandidates);
        Assert.Contains("indisponible", preview.Errors);
        Assert.Equal(0, fake.EmptyCalls);
    }

    [Fact]
    public async Task Executor_refuses_non_executable_zone()
    {
        using var sandbox = Sandbox.Create();
        var file = sandbox.Write("old.tmp");
        var zone = TestZone(sandbox.Root) with { IsExecutable = false, Classification = CleanupClassification.InformationOnly };
        var candidate = new CleanupCandidate(zone.Id, file, "old.tmp", 4, DateTimeOffset.Now.AddDays(-2), true, null);
        var preview = new CleanupZonePreview(zone, DateTimeOffset.Now, 1, 1, 4, 0, new[] { candidate }, Array.Empty<string>());

        var result = await new CleanupExecutionService().ExecuteZoneAsync(preview, null, CancellationToken.None);

        Assert.Equal(CleanupStepStatus.Skipped, result.Status);
        Assert.True(File.Exists(file));
    }

    [Theory]
    [InlineData("C:\\")]
    [InlineData("C:\\Users")]
    [InlineData("C:\\Program Files")]
    [InlineData("C:\\Documents")]
    [InlineData("C:\\Images")]
    [InlineData("C:\\Videos")]
    public void Permission_repair_refuses_broad_or_personal_paths(string path)
    {
        Assert.False(new CleanupPermissionRepairService(Array.Empty<CleanupZoneDefinition>()).Assess(path).IsAllowed);
    }

    [Fact]
    public void Permission_repair_allows_only_a_child_of_an_allowlisted_technical_root()
    {
        using var sandbox = Sandbox.Create();
        var child = Directory.CreateDirectory(Path.Combine(sandbox.Root, "blocked-cache")).FullName;
        var service = new CleanupPermissionRepairService(new[] { TestZone(sandbox.Root) });

        Assert.True(service.Assess(child).IsAllowed);
        Assert.False(service.Assess(sandbox.Root).IsAllowed);
        Assert.True(service.Assess(child).RequiresCriticalConfirmation);
    }

    [Fact]
    public void Permission_repair_accepts_a_path_not_a_free_takeown_command()
    {
        var assessment = new CleanupPermissionRepairService(Array.Empty<CleanupZoneDefinition>())
            .Assess("takeown /f C:\\ /r");

        Assert.False(assessment.IsAllowed);
    }

    [Fact]
    public async Task Preview_excludes_personal_files_even_inside_temp_zone()
    {
        using var sandbox = Sandbox.Create();
        sandbox.Write("technical.tmp", DateTimeOffset.Now.AddDays(-2));
        sandbox.Write("family-photo.jpg", DateTimeOffset.Now.AddDays(-2));
        var service = new CleanupPreviewService(new[] { TestZone(sandbox.Root) }, () => DateTimeOffset.Now);

        var preview = Assert.Single(await service.PreviewAsync(null, CancellationToken.None));

        Assert.Equal(1, preview.EligibleFileCount);
        Assert.Contains(preview.Candidates, candidate => candidate.LogicalPath.EndsWith("family-photo.jpg") && !candidate.IsEligible);
    }

    [Fact]
    public void Cleanup_report_always_states_no_automatic_personal_deletion()
    {
        var now = DateTimeOffset.Now;
        var report = new CleanupSessionReport(now, now, TimeSpan.Zero, Array.Empty<CleanupStepResult>(), Array.Empty<string>());
        var mapped = CleanupReportMapper.Map(report, 0);

        Assert.Contains("Aucun fichier personnel supprime automatiquement", mapped.SimpleView);
        Assert.False(report.PersonalFilesDeletedAutomatically);
    }

    [Fact]
    public async Task Personal_storage_analysis_is_read_only_and_uses_injected_root()
    {
        using var sandbox = Sandbox.Create();
        var photo = sandbox.Write("family.jpg");
        var analyzer = new CleanupStorageAnalyzer(new[] { sandbox.Root }, largeFileThreshold: 1, maximumItems: 10);

        var result = await analyzer.AnalyzeAsync(CancellationToken.None);

        Assert.Contains(result.Items, item => item.FullPath == photo && item.Classification == CleanupClassification.Protected);
        Assert.True(File.Exists(photo));
    }

    [Fact]
    public async Task Large_ambiguous_file_is_review_only_not_deletable()
    {
        using var sandbox = Sandbox.Create();
        var file = sandbox.Write("unknown.bin");
        var analyzer = new CleanupStorageAnalyzer(new[] { sandbox.Root }, largeFileThreshold: 1, maximumItems: 10);

        var result = await analyzer.AnalyzeAsync(CancellationToken.None);

        Assert.Contains(result.Items, item => item.FullPath == file && item.Classification == CleanupClassification.ReviewOnly);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Cloud_named_root_is_not_scanned_by_default()
    {
        using var sandbox = Sandbox.Create();
        var cloud = Directory.CreateDirectory(Path.Combine(sandbox.Root, "OneDrive")).FullName;
        var file = Path.Combine(cloud, "photo.jpg");
        File.WriteAllBytes(file, new byte[] { 1 });
        var analyzer = new CleanupStorageAnalyzer(new[] { cloud }, largeFileThreshold: 1, maximumItems: 10);

        var result = await analyzer.AnalyzeAsync(CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Single(result.SkippedRoots);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Information_only_zones_are_not_enumerated()
    {
        using var sandbox = Sandbox.Create();
        sandbox.Write("photo.jpg", DateTimeOffset.Now.AddYears(-5));
        var zone = TestZone(sandbox.Root) with { Classification = CleanupClassification.InformationOnly, IsExecutable = false };
        var service = new CleanupPreviewService(new[] { zone });

        var preview = Assert.Single(await service.PreviewAsync(null, CancellationToken.None));

        Assert.Equal(0, preview.ExaminedFileCount);
        Assert.Equal(0, preview.EligibleFileCount);
    }

    [Fact]
    public void Safe_zones_use_simple_confirmation_and_advanced_zones_reinforced_confirmation()
    {
        var zones = CleanupZoneCatalog.CreateDefault();
        Assert.All(zones.Where(zone => zone.Classification == CleanupClassification.Cleanable), zone => Assert.False(zone.RequiresReinforcedConfirmation));
        Assert.All(zones.Where(zone => zone.Classification == CleanupClassification.AdvancedCleanable), zone => Assert.True(zone.RequiresReinforcedConfirmation));
    }

    [Fact]
    public void Cleanup_view_has_no_global_clean_everything_button()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Virgil.App", "Views", "CleanupView.xaml"));

        Assert.DoesNotContain("TOUT NETTOYER", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NETTOYAGE SUR", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NETTOYAGE AVANCE", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A REVOIR MANUELLEMENT", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dangerous_user_script_commands_are_not_reproduced_in_cleanup_core()
    {
        var root = Path.Combine(FindRepositoryRoot(), "src", "Virgil.Core", "Cleanup");
        var source = string.Join("\n", Directory.EnumerateFiles(root, "*.cs").Select(File.ReadAllText));

        Assert.DoesNotContain("del *.log /a /s /q /f", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("takeown /f C:\\ /r", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("icacls", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RD /S /Q", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Virgil.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Racine de test introuvable.");
    }

    private static CleanupZoneDefinition TestZone(string root)
    {
        return new CleanupZoneDefinition(
            CleanupZoneId.UserTemporaryFiles, "Zone test", "Technique", root, TimeSpan.FromHours(1), CleanupRiskLevel.Low,
            "Validation", "Suppression technique", "Personnel", 1)
        {
            Classification = CleanupClassification.Cleanable,
            IsExecutable = true
        };
    }

    private sealed class Sandbox : IDisposable
    {
        private Sandbox(string root) => Root = root;
        public string Root { get; }

        public static Sandbox Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "virgil-cleanup-v2-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new Sandbox(root);
        }

        public string Write(string relativePath, DateTimeOffset? timestamp = null)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            if (timestamp.HasValue) File.SetLastWriteTimeUtc(path, timestamp.Value.UtcDateTime);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); }
            catch { }
        }
    }

    private sealed class FakeRecycleBinService : IRecycleBinService
    {
        private readonly RecycleBinState _state;
        public FakeRecycleBinService(RecycleBinState state) => _state = state;
        public int EmptyCalls { get; private set; }
        public RecycleBinState Query() => _state;
        public RecycleBinActionResult Empty()
        {
            EmptyCalls++;
            return new RecycleBinActionResult(true, _state.ItemCount, _state.SizeBytes);
        }
    }
}
