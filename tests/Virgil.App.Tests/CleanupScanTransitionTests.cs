using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupScanTransitionTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Gestures_during_complete_or_progress_reporting_rescan_never_add_personal_rows(bool estimator, bool progressive)
    {
        var fixture = new CleanupViewModelTests.Fixture();
        var source = new HeldPreview();
        CleanupMaintenanceModule Module() => new(new CleanupAnalyzer(source, new CleanupStorageAnalyzer([])), fixture.Actions);
        var vm = new CleanupViewModel(Module(), fixture.Runner, fixture.History, fixture.Confirmation,
            estimator: estimator ? new CleanupEstimateService(Module()) : null);
        await vm.AnalyzeZonesCommand.ExecuteAsync(null);
        var original = vm.Groups.SelectMany(g => g.Rows).ToArray();
        Assert.All(original, row => Assert.Contains(row, vm.Categories.SelectMany(c => c.Rows)));
        AssertPersonalUnchecked(vm);
        source.Hold = true;
        var pending = vm.AnalyzeZonesCommand.ExecuteAsync(null);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.False(vm.IsIdle);
            for (var step = 0; step < 3; step++)
            {
                if (progressive) source.Progress!.Report(new(CleanupZoneId.BrowserBraveCache, "Cache Brave", 77,
                    "Inventaire en cours", 324 + step, 422));
                vm.SelectAllCommand.Execute(null);
                foreach (var category in vm.Categories)
                {
                    category.Selection.IsSelected = true;
                    AssertPersonalUnchecked(vm);
                    category.Selection.IsSelected = false;
                    AssertPersonalUnchecked(vm);
                }
                vm.ReturnHomeCommand.Execute(null);
                AssertPersonalUnchecked(vm);
                Assert.Equal(original, vm.Groups.SelectMany(g => g.Rows));
            }
        }
        finally { source.Release.TrySetResult(); }
        await pending;
        Assert.True(vm.IsIdle);
        AssertPersonalUnchecked(vm);
        Assert.All(vm.Groups.SelectMany(g => g.Rows), row => Assert.DoesNotContain(row, original));
        vm.SelectAllCommand.Execute(null);
        AssertPersonalUnchecked(vm);
        foreach (var category in vm.Categories)
        {
            category.Selection.IsSelected = true;
            Assert.All(category.Rows.Where(r => r.CanGroupSelect), r => Assert.True(r.IsSelected));
            AssertPersonalUnchecked(vm);
            category.Selection.IsSelected = false;
            Assert.All(category.Rows, r => Assert.False(r.IsSelected));
        }
        vm.ReturnHomeCommand.Execute(null);
        AssertPersonalUnchecked(vm);
        Assert.Empty(fixture.Actions.Order);
        Assert.Equal(0, fixture.Confirmation.Calls);
    }

    [Theory]
    [InlineData(CleanupZoneId.OldDownloads)] [InlineData(CleanupZoneId.RecycleBin)]
    [InlineData(CleanupZoneId.SoftwareLeftovers)]
    public async Task Individual_personal_choice_requires_existing_typed_critical_consent(CleanupZoneId id)
    {
        var fixture = new CleanupViewModelTests.Fixture();
        var preview = CleanupLotAReproductionTests.CaptureDownload();
        preview = preview with { Candidates = [new(id, Path.Combine(preview.Definition.RootPath, "old.tmp"),
            "old.tmp", 10, preview.GeneratedAt, true, null)] };
        preview = new PersonalFolderPolicy(new EmptyCleanupSource()).Apply(preview with
        { Definition = preview.Definition with { Id = id, IsPersonalFolder = id == CleanupZoneId.SoftwareLeftovers } });
        fixture.Previews.Items = [preview];
        await fixture.Analyze();
        var row = Assert.Single(fixture.Vm.Groups.SelectMany(g => g.Rows));
        Assert.False(row.IsSelected);
        row.IsSelected = true;
        foreach (var consent in new[] { "", "oui", "exact" })
        {
            var pending = fixture.Run();
            Assert.True(fixture.Vm.IsValidationOpen);
            Assert.True(fixture.Vm.RequiresCriticalConfirmation);
            Assert.NotEmpty(fixture.Vm.ExpectedCriticalConfirmation);
            fixture.Vm.CriticalConfirmationText = consent == "exact" ? fixture.Vm.ExpectedCriticalConfirmation : consent;
            fixture.Vm.ExecuteDecisionCommand.Execute(null);
            await pending;
            Assert.Equal(new[] { (consent == "exact" ? "Execute:" : "Cancel:") + id }, fixture.Actions.Order);
            fixture.Vm.CloseReportCommand.Execute(null);
            fixture.Actions.Order.Clear();
        }
    }

    private static void AssertPersonalUnchecked(CleanupViewModel vm) =>
        Assert.All(vm.Groups.SelectMany(g => g.Rows).Where(r => !r.CanGroupSelect), r => Assert.False(r.IsSelected));

    private sealed class HeldPreview : ICleanupPreviewService
    {
        public bool Hold { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<CleanupProgress>? Progress { get; private set; }
        public IReadOnlyList<CleanupZoneDefinition> GetZones() => [];
        public async Task<IReadOnlyList<CleanupZonePreview>> PreviewAsync(IProgress<CleanupProgress>? progress, CancellationToken token)
        {
            Progress = progress;
            if (Hold) { Started.TrySetResult(); await Release.Task.WaitAsync(token); }
            var safe = CleanupSelectionTests.Preview(CleanupClassification.Cleanable);
            return Enum.GetValues<CleanupZoneId>().Select(id => safe with
                { Definition = safe.Definition with { Id = id, IsPersonalFolder = true } })
                .Concat(new[] { safe, CleanupLotAReproductionTests.CaptureDownload(), safe with
                    { Definition = safe.Definition with { Id = CleanupZoneId.RecycleBin } } }).ToArray();
        }
    }
}
