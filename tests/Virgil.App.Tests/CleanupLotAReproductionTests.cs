using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupLotAReproductionTests
{
    [Fact]
    public async Task Capture_downloads_are_unchecked_after_scan_rescan_global_and_category_selection()
    {
        var ports = new CapturePorts();
        var scanner = new AdditionalCleanupScanner(ports, ports, ports, new LeftoverAnalyzer(),
            new PersonalFolderPolicy(ports), TimeProvider.System);
        var fixture = new CleanupViewModelTests.Fixture();
        fixture.Previews.Handler = _ => scanner.ScanAsync(CancellationToken.None);
        for (var scan = 0; scan < 2; scan++)
        {
            await fixture.Analyze();
            var row = Assert.Single(fixture.Vm.Groups.SelectMany(g => g.Rows));
            Assert.Equal(CleanupZoneId.OldDownloads, row.Preview.Definition.Id);
            Assert.Equal(4, row.Preview.EligibleFileCount);
            Assert.True(row.CanSelect);
            Assert.False(row.CanGroupSelect);
            Assert.False(row.IsSelected);
            fixture.Vm.SelectAllCommand.Execute(null);
            Assert.False(row.IsSelected);
            var category = Assert.Single(fixture.Vm.Categories);
            category.Selection.IsSelected = true;
            Assert.False(row.IsSelected);
            category.Selection.IsSelected = false;
            Assert.False(row.IsSelected);
            fixture.Vm.ReturnHomeCommand.Execute(null);
            Assert.False(row.IsSelected);
            // Un choix individuel ne doit pas être restauré au prochain scan.
            row.IsSelected = true;
        }
        Assert.Empty(fixture.Actions.Order);
        Assert.Equal(0, fixture.Confirmation.Calls);
    }

    public static IEnumerable<object[]> PersonalZones() => Enum.GetValues<CleanupZoneId>()
        .SelectMany(id => new[] { new object[] { id, true }, new object[] { id, false } });

    [Theory]
    [MemberData(nameof(PersonalZones))]
    public async Task Every_personal_zone_stays_out_of_bulk_selection_and_rescan(CleanupZoneId id, bool personalDefinition)
    {
        var fixture = new CleanupViewModelTests.Fixture();
        var preview = CleanupSelectionTests.Preview(CleanupClassification.Cleanable);
        fixture.Previews.Items = [preview with
        {
            Definition = preview.Definition with { Id = id, IsPersonalFolder = personalDefinition },
            Candidates = [new(id, "personal.tmp", "personal.tmp", 10, preview.GeneratedAt, true, null)
                { IsPersonalFolder = !personalDefinition }]
        }];
        for (var scan = 0; scan < 2; scan++)
        {
            await fixture.Analyze();
            var row = Assert.Single(fixture.Vm.Groups.SelectMany(g => g.Rows));
            Assert.False(row.IsSelected);
            fixture.Vm.SelectAllCommand.Execute(null);
            Assert.False(row.IsSelected);
            foreach (var category in fixture.Vm.Categories)
            {
                category.Selection.IsSelected = true;
                Assert.False(row.IsSelected);
                category.Selection.IsSelected = false;
                Assert.False(row.IsSelected);
            }
            fixture.Vm.ReturnHomeCommand.Execute(null);
            Assert.False(row.IsSelected);
            if (row.CanSelect) row.IsSelected = true;
        }
        Assert.Empty(fixture.Actions.Order);
    }

    [Theory]
    [InlineData(CleanupZoneId.OldDownloads)]
    [InlineData(CleanupZoneId.RecycleBin)]
    public async Task Personal_ids_remain_excluded_without_personal_flags(CleanupZoneId id)
    {
        var fixture = new CleanupViewModelTests.Fixture();
        var preview = CleanupSelectionTests.Preview(CleanupClassification.Cleanable);
        fixture.Previews.Items = [preview with { Definition = preview.Definition with { Id = id } }];
        await fixture.Analyze();
        var row = Assert.Single(fixture.Vm.Groups.SelectMany(g => g.Rows));
        Assert.False(row.IsSelected);
        fixture.Vm.SelectAllCommand.Execute(null);
        Assert.False(row.IsSelected);
        foreach (var category in fixture.Vm.Categories)
        {
            category.Selection.IsSelected = true;
            Assert.False(row.IsSelected);
            category.Selection.IsSelected = false;
            Assert.False(row.IsSelected);
        }
    }

    [Theory]
    [InlineData(CleanupZoneId.UserTemporaryFiles, CleanupZoneId.WindowsThumbnailCache)]
    [InlineData(CleanupZoneId.BrowserChromeCache, CleanupZoneId.BrowserBraveCache)]
    public async Task Eligible_category_toggles_every_row_and_reports_partial_selection(CleanupZoneId first, CleanupZoneId second)
    {
        var fixture = new CleanupViewModelTests.Fixture();
        var preview = CleanupSelectionTests.Preview(CleanupClassification.Cleanable);
        fixture.Previews.Items = [preview with { Definition = preview.Definition with { Id = first } },
            preview with { Definition = preview.Definition with { Id = second } }];
        await fixture.Analyze();
        var category = Assert.Single(fixture.Vm.Categories);
        Assert.True(category.Selection.IsSelected);
        category.Selection.IsSelected = false;
        Assert.All(category.Rows, row => Assert.False(row.IsSelected));
        category.Rows[0].IsSelected = true;
        Assert.Null(category.Selection.IsSelected);
        category.Selection.IsSelected = true;
        Assert.All(category.Rows, row => Assert.True(row.IsSelected));
        category.Selection.IsSelected = false;
        Assert.All(category.Rows, row => Assert.False(row.IsSelected));
    }

    internal static CleanupZonePreview CaptureDownload()
    {
        var preview = CleanupSelectionTests.Preview(CleanupClassification.AdvancedCleanable);
        return preview with
        {
            Definition = preview.Definition with { Id = CleanupZoneId.OldDownloads,
                DisplayName = "Téléchargements anciens ou volumineux", RiskLevel = CleanupRiskLevel.Medium },
            EligibleBytes = 237921894, EligibleFileCount = 4
        };
    }

    private sealed class CapturePorts : IKnownFoldersProvider, IDownloadsSource, ISoftwareFootprintSource
    {
        public IReadOnlyList<KnownFolderPath> GetFolders() => [new(KnownFolder.Downloads, Path.GetFullPath("lot-a-downloads"))];
        public IReadOnlyList<FootprintFile> ReadFiles(string downloadsRoot, CancellationToken cancellationToken) =>
            Enumerable.Range(0, 4).Select(i => new FootprintFile(Path.Combine(downloadsRoot, $"old-{i}.tmp"),
                i == 0 ? 59480472 : 59480474, DateTimeOffset.UtcNow.AddDays(-100))).ToArray();
        public Task<SoftwareFootprintSnapshot> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SoftwareFootprintSnapshot([], [], []));
    }
}
