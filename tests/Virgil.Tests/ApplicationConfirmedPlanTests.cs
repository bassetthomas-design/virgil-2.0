using Virgil.Contracts.Actions;
using Virgil.Contracts.Applications;
using Virgil.Core.Actions;
using Virgil.Core.Applications;
using Xunit;

namespace Virgil.Tests;

public sealed class ApplicationConfirmedPlanTests
{
    private static InstalledApplication Application() => new()
    {
        Id = "test-gate-app", DisplayName = "Test gate app", WingetId = "Example.GateApp",
        RiskLevel = ApplicationRiskLevel.SafeToUninstall, CanUninstall = true,
        UninstallKind = ApplicationUninstallKind.Winget
    };
    private static ApplicationUninstallConfirmation Confirmation(bool reinforced = false) => new()
    {
        ExplicitlyConfirmed = true, ReinforcedConfirmed = reinforced, Source = "test"
    };
    private static ConfirmedPlan Issue(ConfirmationService authority, ApplicationUninstallService service,
        InstalledApplication application) => authority.Issue(ApplicationActionPreviewFactory.Create(service.BuildPlan(application)),
            new ConfirmationConsent(application.RiskLevel == ApplicationRiskLevel.Caution
                ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple, null));

    [Theory]
    [InlineData("missing")]
    [InlineData("other-authority")]
    [InlineData("expired")]
    [InlineData("other-application")]
    [InlineData("changed-command")]
    [InlineData("reused")]
    public async Task InvalidPlanNeverLaunches(string scenario)
    {
        var clock = new Clock();
        var authority = new ConfirmationService(clock);
        var launcher = new Launcher();
        var service = new ApplicationUninstallService(new ApplicationUninstallCommandValidator(),
            launcher, new ApplicationRemnantScanner(), authority);
        var application = Application();
        var plan = scenario == "missing" ? null : Issue(
            scenario == "other-authority" ? new ConfirmationService(clock) : authority, service, application);
        var expected = scenario switch
        {
            "missing" => PlanRejectionReason.Missing,
            "other-authority" => PlanRejectionReason.UnknownProvenance,
            "expired" => PlanRejectionReason.Expired,
            "reused" => PlanRejectionReason.AlreadyUsed,
            _ => PlanRejectionReason.PreviewHashMismatch
        };
        if (scenario == "expired") clock.Advance(TimeSpan.FromMinutes(10));
        if (scenario == "other-application") application = application with { Id = "different", DisplayName = "Different" };
        if (scenario == "changed-command") application = application with { WingetId = "Example.OtherCommand" };
        if (scenario == "reused")
        {
            var first = await service.LaunchOfficialUninstallAsync(application, plan, Confirmation(), null, CancellationToken.None);
            Assert.True(first.WasLaunched);
        }
        var callsBefore = launcher.Calls;
        var preview = ApplicationActionPreviewFactory.Create(service.BuildPlan(application));
        var refusal = authority.Validate(plan, preview);
        Assert.Equal(expected, refusal.Reason);
        var result = await service.LaunchOfficialUninstallAsync(application, plan, Confirmation(), null, CancellationToken.None);
        Assert.True(result.WasCancelled);
        Assert.False(result.WasLaunched);
        Assert.Equal(callsBefore, launcher.Calls);
        Assert.Equal("Desinstallation annulee.\n" + refusal.Message, result.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidConsentLaunchesExactlyOnce(bool caution)
    {
        var authority = new ConfirmationService(new Clock());
        var launcher = new Launcher();
        var service = new ApplicationUninstallService(new ApplicationUninstallCommandValidator(),
            launcher, new ApplicationRemnantScanner(), authority);
        var application = Application() with { RiskLevel = caution ? ApplicationRiskLevel.Caution : ApplicationRiskLevel.SafeToUninstall };
        var plan = Issue(authority, service, application);
        var result = await service.LaunchOfficialUninstallAsync(application, plan, Confirmation(caution), null, CancellationToken.None);
        Assert.True(result.WasLaunched);
        Assert.Equal(1, launcher.Calls);
        Assert.Equal(["uninstall", "--id", "Example.GateApp", "--exact"], launcher.Arguments);
        var second = await service.LaunchOfficialUninstallAsync(application, plan, Confirmation(caution), null, CancellationToken.None);
        Assert.True(second.WasCancelled);
        Assert.Equal(1, launcher.Calls);
    }

    [Fact]
    public async Task CautionPolicyRefusalDoesNotConsumeValidPlan()
    {
        var authority = new ConfirmationService(new Clock());
        var launcher = new Launcher();
        var service = new ApplicationUninstallService(new ApplicationUninstallCommandValidator(),
            launcher, new ApplicationRemnantScanner(), authority);
        var application = Application() with { RiskLevel = ApplicationRiskLevel.Caution };
        var plan = Issue(authority, service, application);
        var result = await service.LaunchOfficialUninstallAsync(application, plan, Confirmation(), null, CancellationToken.None);
        Assert.True(result.WasCancelled);
        Assert.Equal(0, launcher.Calls);
        Assert.True(authority.Validate(plan, ApplicationActionPreviewFactory.Create(service.BuildPlan(application))).IsAccepted);
    }

    [Fact]
    public void PreviewIsDeterministicAndBindsValidatedArgumentsAndScope()
    {
        var validator = new ApplicationUninstallCommandValidator();
        var authority = new ConfirmationService(new Clock());
        var service = new ApplicationUninstallService(validator, new Launcher(), new ApplicationRemnantScanner(), authority);
        var application = Application();
        var preview = ApplicationActionPreviewFactory.Create(service.BuildPlan(application));
        Assert.Equal("applications.uninstall", preview.ActionId);
        Assert.Equal(application.DisplayName, preview.Title);
        Assert.Equal(new[] { application.Id, application.DisplayName }, preview.Targets);
        Assert.Equal(RiskLevel.Low, preview.RiskLevel);
        Assert.Equal(Reversibility.None, preview.Reversibility);
        Assert.NotEmpty(preview.Effects);
        Assert.Equal(ActionPreviewHasher.Hash(preview),
            ActionPreviewHasher.Hash(ApplicationActionPreviewFactory.Create(service.BuildPlan(application))));
        Assert.Equal("Example.GateApp", preview.Parameters["WingetId"]);
        Assert.Equal("winget", preview.Parameters["Executable"]);
        var changed = service.BuildPlan(application) with
        {
            Validation = validator.Validate(application) with { Arguments = ["uninstall", "--id", "Example.Other", "--exact"] }
        };
        Assert.NotEqual(ActionPreviewHasher.Hash(preview), ActionPreviewHasher.Hash(ApplicationActionPreviewFactory.Create(changed)));
    }

    [Fact]
    public void EveryPublicLaunchOverloadRequiresConfirmedPlan()
    {
        var methods = typeof(ApplicationUninstallService).GetMethods()
            .Where(method => method.Name == nameof(ApplicationUninstallService.LaunchOfficialUninstallAsync)).ToArray();
        Assert.NotEmpty(methods);
        Assert.All(methods, method => Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType == typeof(ConfirmedPlan)));
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(_ticks);
        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }
    private sealed class Launcher : IApplicationProcessLauncher
    {
        public int Calls { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public Task<ApplicationLaunchResult> LaunchAsync(string executable, IReadOnlyList<string> arguments,
            bool useShellExecute, CancellationToken cancellationToken)
        {
            Calls++;
            Arguments = arguments;
            return Task.FromResult(new ApplicationLaunchResult(true, null, null));
        }
    }
}
