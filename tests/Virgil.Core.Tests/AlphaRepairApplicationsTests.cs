using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Contracts.Applications;
using Virgil.Core.Actions;
using Virgil.Core.Applications;
using Virgil.Core.Interventions;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

public sealed class AlphaRepairApplicationsTests
{
    [Theory]
    [InlineData("sfc.exe", 45)]
    [InlineData("dism.exe", 60)]
    [InlineData("ipconfig.exe", 5)]
    public void CommandsHaveBoundedTimeouts(string name, int minutes)
        => Assert.Equal(TimeSpan.FromMinutes(minutes), ElevatedCommandRunner.TimeoutFor(name));

    [Fact]
    public void VerifyUsesReadOnlyCommandsAndNeverCreatesRestorePoint()
    {
        Assert.True(new ElevatedActionAllowlist().TryGet(InterventionId.VerifyHealth, "C:", out var spec));
        Assert.Contains(spec.Commands, command => command.Arguments.Contains("/verifyonly"));
        Assert.Contains(spec.Commands, command => command.Arguments.Contains("/CheckHealth"));
        Assert.Contains(spec.Commands, command => command.Arguments.Contains("/scan"));
        Assert.DoesNotContain(spec.Commands, command => command.FileName == "virgil-restore-point" || command.Arguments.Contains("/scannow") || command.Arguments.Contains("/RestoreHealth"));
    }

    [Theory]
    [InlineData("VIRGIL|DNS|FINDING|Échec", InterventionId.FlushDns)]
    [InlineData("VIRGIL|Audiosrv|FINDING|Arrêté", InterventionId.RestartAudio)]
    [InlineData("VIRGIL|PrintQueue|FINDING|Bloquée", InterventionId.ResetPrintQueue)]
    [InlineData("The component store is repairable.", InterventionId.DismRestoreHealth)]
    public void FindingsProposeOnlyRelatedRepairs(string output, InterventionId expected)
        => Assert.Equal(expected, Assert.Single(InterventionSafety.RepairsFor(output)));

    [Theory]
    [InlineData("")]
    [InlineData("VIRGIL|DNS|UNKNOWN|Indisponible")]
    [InlineData("VIRGIL|DNS|OK|Résolution réussie")]
    public void MissingEvidenceNeverProposesRepairs(string output) => Assert.Empty(InterventionSafety.RepairsFor(output));

    [Fact]
    public void StoreMetadataIsFriendlyAndProtectedPackagesCannotBeUninstalled()
    {
        var result = ApplicationStoreReader.Parse("""
            [
              {"Name":"5319275A.WhatsAppDesktop","DisplayName":"WhatsApp","PublisherDisplayName":"WhatsApp Inc.","PackageFullName":"WhatsApp_1.0.0.0_x64__abc","Logo":"C:\\Packages\\logo.png"},
              {"Name":"Framework","IsFramework":true,"PackageFullName":"Framework_1.0.0.0_x64__abc"},
              {"Name":"System","SignatureKind":"System","PackageFullName":"System_1.0.0.0_x64__abc"},
              {"Name":"Resources","IsResourcePackage":true,"PackageFullName":"Resources_1.0.0.0_x64__abc"},
              {"Name":"Immutable","NonRemovable":true,"PackageFullName":"Immutable_1.0.0.0_x64__abc"}
            ]
            """);
        Assert.Equal(2, result.Applications.Count);
        var app = result.Applications[0];
        Assert.Equal("WhatsApp", app.DisplayName);
        Assert.Equal("WhatsApp Inc.", app.Publisher);
        Assert.EndsWith("logo.png", app.IconPath);
        Assert.True(app.CanUninstall);
        Assert.False(result.Applications[1].CanUninstall);
        Assert.True(result.Applications[1].IsSystemComponent);
    }

    [Theory]
    [InlineData("Package_1.0.0.0_x64__abc'; Remove-Item C:\\*")]
    [InlineData("-AllUsers")]
    [InlineData("*")]
    public void StoreUninstallRejectsInjectedOrAmbiguousIdentity(string package)
    {
        var app = Safe("store") with { Source = ApplicationInventorySource.Store, UninstallKind = ApplicationUninstallKind.StorePackage, StorePackageFullName = package };
        Assert.Equal(ApplicationCommandValidationStatus.Blocked, new ApplicationUninstallCommandValidator().Validate(app).Status);
    }

    [Fact]
    public async Task BatchPlanBindsWholeSelectionAndIsSingleUse()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var launcher = new Launcher();
        var service = new ApplicationUninstallService(new(), launcher, new(), authority);
        var apps = new[] { Safe("one"), Safe("two") };
        var preview = ApplicationBatchPreviewFactory.Create(apps.Select(service.BuildPlan).ToArray());
        var plan = authority.Issue(preview, new(ConfirmationLevel.Simple, null));
        var confirmation = new ApplicationUninstallConfirmation { ExplicitlyConfirmed = true };
        await service.LaunchBatchAsync([apps[0]], plan, confirmation, null, CancellationToken.None);
        Assert.Empty(launcher.Apps);
        await service.LaunchBatchAsync(apps, plan, confirmation, null, CancellationToken.None);
        Assert.Equal(2, launcher.Apps.Count);
        await service.LaunchBatchAsync(apps, plan, confirmation, null, CancellationToken.None);
        Assert.Equal(2, launcher.Apps.Count);
    }

    [Fact]
    public async Task BatchRefusesMissingPlanAndProtectedItemsBeforeAnyLaunch()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var launcher = new Launcher();
        var service = new ApplicationUninstallService(new(), launcher, new(), authority);
        var apps = new[] { Safe("one"), Safe("protected") with { IsSystemComponent = true } };
        var preview = ApplicationBatchPreviewFactory.Create(apps.Select(service.BuildPlan).ToArray());
        var plan = authority.Issue(preview, new(ConfirmationLevel.Simple, null));
        var consent = new ApplicationUninstallConfirmation { ExplicitlyConfirmed = true, ReinforcedConfirmed = true };
        await service.LaunchBatchAsync([apps[0]], null, consent, null, CancellationToken.None);
        await service.LaunchBatchAsync(apps, plan, consent, null, CancellationToken.None);
        Assert.Empty(launcher.Apps);
    }

    [Theory]
    [Trait("Category", "Safety")]
    [InlineData("Report", 1, false)]
    [InlineData("Report", 2, false)]
    [InlineData("skip", 1, false)]
    [InlineData("skip", 2, false)]
    [InlineData("skip", 1, true)]
    public async Task BatchCancellationInsideCallbackNeverLaunchesCurrentOrFollowingInstaller(
        string callback, int cancelAt, bool skipCancelledItem)
    {
        using var cancellation = new CancellationTokenSource();
        var authority = new ConfirmationService(TimeProvider.System);
        var launcher = new Launcher();
        var service = new ApplicationUninstallService(new(), launcher, new(), authority);
        var apps = new[] { Safe("one"), Safe("two") };
        var plan = authority.Issue(ApplicationBatchPreviewFactory.Create(apps.Select(service.BuildPlan).ToArray()),
            new(ConfirmationLevel.Simple, null));
        var notified = new List<int>();
        var progress = new CallbackProgress(value =>
        {
            if (value.Step == "Rapport") return;
            notified.Add(value.StepNumber);
            if (callback == "Report" && value.StepNumber == cancelAt) cancellation.Cancel();
        });
        var result = await service.LaunchBatchAsync(apps, plan,
            new() { ExplicitlyConfirmed = true }, progress, cancellation.Token, app =>
            {
                if (callback != "skip" || app.Id != apps[cancelAt - 1].Id) return false;
                cancellation.Cancel();
                return skipCancelledItem;
            });

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(cancelAt - 1, launcher.Apps.Count);
        Assert.DoesNotContain(launcher.Apps, command => command.Contains("Example.two", StringComparison.Ordinal));
        Assert.Equal(2, result.Count);
        Assert.Equal("two", result[1].Application.Id);
        Assert.True(result[1].WasCancelled);
        Assert.False(result[1].WasLaunched);
        Assert.False(result[1].StatusUnknown);
        Assert.Contains("Non lancée", result[1].Result);
        if (!skipCancelledItem)
        {
            Assert.All(result.Skip(cancelAt - 1), item => Assert.Contains("Non lancée", item.Result));
            if (callback == "Report") Assert.Contains(cancelAt, notified);
        }
    }

    private sealed class CallbackProgress(Action<ApplicationUninstallProgress> report) : IProgress<ApplicationUninstallProgress>
    {
        public void Report(ApplicationUninstallProgress value) => report(value);
    }

    [Fact]
    public async Task BatchSkipNeverLaunchesSkippedItem()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var launcher = new Launcher();
        var service = new ApplicationUninstallService(new(), launcher, new(), authority);
        var apps = new[] { Safe("one"), Safe("two") };
        var plan = authority.Issue(ApplicationBatchPreviewFactory.Create(apps.Select(service.BuildPlan).ToArray()), new(ConfirmationLevel.Simple, null));
        var result = await service.LaunchBatchAsync(apps, plan, new() { ExplicitlyConfirmed = true }, null, CancellationToken.None, app => app.Id == "one");
        Assert.Single(launcher.Apps);
        Assert.True(result[0].WasCancelled);
    }

    [Fact]
    public async Task RestorePointFailureBlocksRepairAndCriticalOverrideIsHashBound()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var helper = new Helper();
        var service = new InterventionExecutionService(helper, new Explorer(), authority);
        var definition = new InterventionCatalog().Get(InterventionId.SfcScan);
        var diagnostic = new InterventionDiagnostic { Definition = definition, IsAvailable = true };
        var plan = authority.Issue(InterventionActionPreviewFactory.Create(definition), new(ConfirmationLevel.Reinforced, null));
        var result = await service.ExecuteAsync(diagnostic, plan, CancellationToken.None);
        Assert.Contains("point de restauration", result.ReadableError);
        Assert.Equal(InterventionId.CreateRestorePoint, Assert.Single(helper.Calls));
        var overrideDefinition = definition with { AllowWithoutRestorePoint = true };
        var preview = InterventionActionPreviewFactory.Create(overrideDefinition);
        Assert.Equal(RiskLevel.Critical, preview.RiskLevel);
        Assert.Contains("aucun point de restauration", preview.Targets);
        Assert.Throws<ConfirmationRefusedException>(() => authority.Issue(preview, new(ConfirmationLevel.Reinforced, null)));
        var critical = authority.Issue(preview, new(ConfirmationLevel.Critical, ConfirmationService.ExpectedCriticalText(preview)));
        await service.ExecuteAsync(diagnostic, critical, CancellationToken.None);
        Assert.Single(helper.Calls); // Override cannot authorize the protected variant, or vice versa.
        await service.ExecuteAsync(diagnostic with { Definition = overrideDefinition }, critical, CancellationToken.None);
        Assert.Equal(InterventionId.SfcScan, helper.Calls.Last());
    }

    private static InstalledApplication Safe(string id) => new() { Id = id, DisplayName = id, CanUninstall = true,
        WingetId = "Example." + id, RiskLevel = ApplicationRiskLevel.SafeToUninstall, UninstallKind = ApplicationUninstallKind.Winget };
    private sealed class Launcher : IApplicationProcessLauncher
    {
        public List<string> Apps { get; } = new();
        public Task<ApplicationLaunchResult> LaunchAsync(string executable, IReadOnlyList<string> arguments, bool useShellExecute, CancellationToken token)
        { Apps.Add(string.Join(" ", arguments)); return Task.FromResult(new ApplicationLaunchResult(false, null, "Échec simulé sans effet OS.")); }
    }
    private sealed class Helper : IInterventionElevatedHelperClient
    {
        public List<InterventionId> Calls { get; } = new();
        public Task<ElevatedInterventionResult> ExecuteAsync(InterventionDefinition definition, CancellationToken token)
        { Calls.Add(definition.Id); return Task.FromResult(new ElevatedInterventionResult { Status = InterventionStatus.Failed, ReadableError = "Protection désactivée." }); }
    }
    private sealed class Explorer : IExplorerRestarter
    {
        public Task<InterventionExecutionResult> RestartAsync(InterventionDiagnostic diagnostic, CancellationToken token) => throw new InvalidOperationException();
    }
}
