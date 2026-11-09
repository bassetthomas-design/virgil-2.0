using Virgil.Contracts;
using Virgil.Core.Actions;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class L6PlusSequenceTests
{
    [Fact]
    [Trait("Category", "Safety")]
    public async Task Opening_update_plan_never_searches_Windows_Update()
    {
        var backend = new Backend();
        var service = new UpdateSequenceService(backend, new ConfirmationService(TimeProvider.System));
        var steps = await service.ScanAsync([], CancellationToken.None);
        Assert.Empty(backend.Executed);
        service.CreatePreview(steps);
        Assert.Empty(backend.Verified);
        Assert.True((await service.ExecuteAsync(steps, null, null, CancellationToken.None)).WasRefused);
        Assert.Empty(backend.Verified);
        Assert.Empty(backend.Executed);
        Assert.Equal(new[] { "restore", "defender", "microsoft-update", "software", "drivers", "store", "windows-feature-upgrade", "winget-final", "pending-reboot" }, steps.Select(step => step.Id));
    }

    [Theory]
    [InlineData("1.4.0", false)]
    [InlineData("1.5.0", true)]
    [Trait("Category", "Safety")]
    public void Winget_upgrade_arguments_match_script_flags(string version, bool pinned)
    {
        var args = WingetUpdateExecutionService.BuildUpgradeArguments("Safe.Package", WingetAvailabilityService.GetCapabilities(version));
        foreach (var flag in new[] { "--force", "--include-unknown", "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity" }) Assert.Contains(flag, args);
        Assert.Equal(pinned, args.Contains("--include-pinned"));
        Assert.DoesNotContain("--all", args);
        Assert.DoesNotContain("--ignore-security-hash", args);
    }

    private sealed class Backend : IUpdateSequenceBackend
    {
        public List<UpdateSequenceStep> Executed { get; } = [];
        public List<UpdateComponent> Verified { get; } = [];
        public Task<UpdateSourceVerification> VerifyAsync(UpdateComponent component, CancellationToken token)
        { Verified.Add(component); throw new InvalidOperationException("Aucun contrôle final avant confirmation."); }
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) { Executed.Add(step); throw new InvalidOperationException(); }
    }
}
