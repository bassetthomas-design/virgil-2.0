using Virgil.Contracts.Actions;
using Virgil.Contracts.Applications;
using Virgil.Core.Actions;
using Virgil.Core.Applications;
using Virgil.Core.Processes;
using Xunit;

namespace Virgil.Tests;

public sealed class ApplicationsSteamRegressionTests
{
    private const string Uri = "steam://uninstall/2221490";

    private static InstalledApplication Game() => ApplicationRegistryReader.ConvertEntry(new()
    {
        KeyName = "Steam App 2221490", DisplayName = "Jeu de test", Publisher = "Studio de test",
        UninstallString = Uri
    }) with { RiskLevel = ApplicationRiskLevel.SafeToUninstall, CanUninstall = true };

    [Fact]
    public void OfficialSteamUriBuildsLaunchablePlan()
    {
        var service = Service(new(), new(TimeProvider.System));
        var plan = service.BuildPlan(Game());
        // Assertion de comportement sur la base : le plan est actuellement bloqué.
        Assert.True(plan.CanLaunch, plan.Validation.Reason);
        Assert.Equal("Steam", plan.Method.ToString());
        Assert.True(plan.Application.IsGame);
        Assert.Equal(ApplicationCommandValidationStatus.Allowed, plan.Validation.Status);
        Assert.Equal(Uri, plan.Validation.Executable);
        Assert.Empty(plan.Validation.Arguments);
    }

    [Fact]
    public void InventoryClassificationKeepsOrdinarySteamGameUninstallable()
    {
        var app = new ApplicationRiskClassifier().Classify(Game() with
        { RiskLevel = ApplicationRiskLevel.Unknown, CanUninstall = false });
        Assert.True(app.CanUninstall, app.RiskReason);
        Assert.NotEqual(ApplicationRiskLevel.Protected, app.RiskLevel);
    }

    [Theory]
    [InlineData("steam://uninstall/0")]
    [InlineData("steam://uninstall/-1")]
    [InlineData("steam://uninstall/abc")]
    [InlineData("steam://uninstall/2221490&&calc.exe")]
    [InlineData("steam://uninstall/C:\\Games\\uninstall.exe")]
    [InlineData("steam://uninstall/2221490/extra")]
    [InlineData("steam://uninstall/2221490?run=calc.exe")]
    [InlineData("steam://uninstall/2221490#fragment")]
    [InlineData("steam://uninstall/%32%32%32%31%34%39%30")]
    [InlineData("steam://uninstall/4294967296")]
    [InlineData("steam://uninstall/٢٢٢١٤٩٠")]
    [InlineData("steam://uninstall/2221490\ncalc.exe")]
    public void InvalidSteamUriIsBlocked(string command)
    {
        var validation = new ApplicationUninstallCommandValidator().Validate(Game() with { UninstallCommand = command });
        Assert.Equal(ApplicationCommandValidationStatus.Blocked, validation.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SteamNeverLaunchesWithoutConfirmedPlan(bool explicitConsent)
    {
        var launcher = new Launcher();
        var service = Service(launcher, new(TimeProvider.System));
        var result = await service.LaunchOfficialUninstallAsync(Game(), null,
            new() { ExplicitlyConfirmed = explicitConsent }, null, CancellationToken.None);
        Assert.False(result.WasLaunched);
        Assert.Equal(0, launcher.Calls);
    }

    [Fact]
    public async Task ConfirmedSteamLaunchUsesOnlyOfficialUriAndShellExecute()
    {
        var launcher = new Launcher();
        var confirmations = new ConfirmationService(TimeProvider.System);
        var service = Service(launcher, confirmations);
        var app = Game();
        var preview = ApplicationActionPreviewFactory.Create(service.BuildPlan(app));
        var consent = confirmations.Issue(preview, new(ConfirmationLevel.Simple, null));
        var result = await service.LaunchOfficialUninstallAsync(app, consent,
            new() { ExplicitlyConfirmed = true }, null, CancellationToken.None);
        Assert.True(result.WasLaunched);
        Assert.Equal(1, launcher.Calls);
        Assert.Equal(Uri, launcher.Executable);
        Assert.Empty(launcher.Arguments);
        Assert.True(launcher.ShellExecute);
        Assert.Contains("Steam", result.Result);
    }

    [Fact]
    public void SteamAndMsiBatchPreviewContainsBothOfficialTargets()
    {
        var service = Service(new(), new(TimeProvider.System));
        var steam = service.BuildPlan(Game());
        var msi = service.BuildPlan(new()
        {
            Id = "msi", DisplayName = "Application MSI", CanUninstall = true,
            RiskLevel = ApplicationRiskLevel.SafeToUninstall, UninstallKind = ApplicationUninstallKind.Msi,
            UninstallCommand = "msiexec.exe /x {11111111-2222-3333-4444-555555555555}"
        });
        Assert.True(steam.CanLaunch);
        Assert.True(msi.CanLaunch);
        var preview = ApplicationBatchPreviewFactory.Create([steam, msi]);
        Assert.Contains(preview.Effects, effect => effect.Contains(Uri, StringComparison.Ordinal));
        Assert.Contains(preview.Effects, effect => effect.Contains("msiexec.exe", StringComparison.Ordinal));
    }

    private static ApplicationUninstallService Service(Launcher launcher, ConfirmationService confirmations) =>
        new(new(), launcher, new(), confirmations);

    [Fact]
    public void SteamKindCannotDisguiseAnotherCommandOrBypassProtection()
    {
        var validator = new ApplicationUninstallCommandValidator();
        Assert.Equal(ApplicationCommandValidationStatus.Blocked, validator.Validate(Game() with
        { UninstallCommand = "msiexec.exe /x {11111111-2222-3333-4444-555555555555}" }).Status);
        Assert.Equal(ApplicationCommandValidationStatus.Blocked, validator.Validate(Game() with
        { IsSystemComponent = true }).Status);
        Assert.Equal(ApplicationCommandValidationStatus.Blocked, validator.Validate(Game() with
        { RiskLevel = ApplicationRiskLevel.Protected }).Status);
    }

    [Fact]
    public async Task CautionSteamKeepsReinforcedConfirmationAndCannotReplayPlan()
    {
        var launcher = new Launcher();
        var confirmations = new ConfirmationService(TimeProvider.System);
        var service = Service(launcher, confirmations);
        var app = new ApplicationRiskClassifier().Classify(Game() with { Publisher = "Steam Studio" });
        var plan = service.BuildPlan(app);
        Assert.Equal(ApplicationCommandValidationStatus.NeedsCaution, plan.Validation.Status);
        Assert.True(plan.RequiresReinforcedConfirmation);
        var confirmed = confirmations.Issue(ApplicationActionPreviewFactory.Create(plan), new(ConfirmationLevel.Reinforced, null));
        var refused = await service.LaunchOfficialUninstallAsync(app, confirmed,
            new() { ExplicitlyConfirmed = true }, null, CancellationToken.None);
        Assert.False(refused.WasLaunched);
        Assert.Equal(0, launcher.Calls);
        var confirmation = new ApplicationUninstallConfirmation { ExplicitlyConfirmed = true, ReinforcedConfirmed = true };
        Assert.True((await service.LaunchOfficialUninstallAsync(app, confirmed, confirmation, null, CancellationToken.None)).WasLaunched);
        Assert.False((await service.LaunchOfficialUninstallAsync(app, confirmed, confirmation, null, CancellationToken.None)).WasLaunched);
        Assert.Equal(1, launcher.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtocolFailureIsReadableAndSteamRequestDoesNotWaitForClientExit(bool throws)
    {
        var runner = new ProtocolRunner(throws);
        var confirmations = new ConfirmationService(TimeProvider.System);
        var service = new ApplicationUninstallService(new(), new ShellApplicationProcessLauncher(runner), new(), confirmations);
        var confirmed = confirmations.Issue(ApplicationActionPreviewFactory.Create(service.BuildPlan(Game())), new(ConfirmationLevel.Simple, null));
        var result = await service.LaunchOfficialUninstallAsync(Game(), confirmed,
            new() { ExplicitlyConfirmed = true }, null, CancellationToken.None);
        Assert.False(result.WasLaunched);
        Assert.Contains("Steam n’a pas pu être ouvert", result.Result);
        Assert.DoesNotContain("exception technique", result.Result);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(Uri, request.FileName);
        Assert.Empty(request.Arguments);
        Assert.True(request.UseShellExecute);
        Assert.False(request.WaitForExit);
    }

    private sealed class ProtocolRunner(bool throws) : IProcessRunner
    {
        public List<ProcessLaunchRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token)
        {
            Requests.Add(request);
            if (throws) throw new InvalidOperationException("exception technique");
            return Task.FromResult(new ProcessLaunchResult(false, null, "exception technique"));
        }
    }

    private sealed class Launcher : IApplicationProcessLauncher
    {
        public int Calls { get; private set; }
        public string? Executable { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public bool ShellExecute { get; private set; }
        public Task<ApplicationLaunchResult> LaunchAsync(string executable, IReadOnlyList<string> arguments,
            bool useShellExecute, CancellationToken cancellationToken)
        {
            Calls++;
            Executable = executable;
            Arguments = arguments;
            ShellExecute = useShellExecute;
            return Task.FromResult(new ApplicationLaunchResult(true, null, null));
        }
    }
}
