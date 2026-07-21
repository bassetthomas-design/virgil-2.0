using Virgil.Core.Applications;
using Virgil.Domain.Applications;
using Xunit;

namespace Virgil.Tests;

public sealed class ApplicationsUninstallSafetyTests
{
    [Fact]
    public void ValidateCommand_AllowsMsiUninstallWithProductCode()
    {
        var validator = new ApplicationUninstallCommandValidator();

        var result = validator.ValidateCommand(
            @"MsiExec.exe /X {11111111-2222-3333-4444-555555555555}",
            ApplicationUninstallKind.Msi,
            ApplicationRiskLevel.SafeToUninstall);

        Assert.Equal(ApplicationCommandValidationStatus.Allowed, result.Status);
        Assert.Equal("msiexec.exe", result.Executable);
        Assert.Contains("/X", result.Arguments);
    }

    [Fact]
    public void ValidateCommand_BlocksFolderDeletionAndChainedCommands()
    {
        var validator = new ApplicationUninstallCommandValidator();

        var result = validator.ValidateCommand(
            @"cmd.exe /c rmdir /s /q ""C:\Program Files\Example"" && del C:\Users\Public\file.txt",
            ApplicationUninstallKind.RegistryUninstallString,
            ApplicationRiskLevel.SafeToUninstall);

        Assert.Equal(ApplicationCommandValidationStatus.Blocked, result.Status);
        Assert.Contains("dangereuse", result.Reason);
    }

    [Fact]
    public void Validate_BlocksProtectedApplicationsEvenWithOfficialCommand()
    {
        var validator = new ApplicationUninstallCommandValidator();
        var application = new InstalledApplication
        {
            DisplayName = "Windows Runtime",
            RiskLevel = ApplicationRiskLevel.Protected,
            UninstallKind = ApplicationUninstallKind.RegistryUninstallString,
            UninstallCommand = @"C:\Program Files\Runtime\uninstall.exe"
        };

        var result = validator.Validate(application);

        Assert.Equal(ApplicationCommandValidationStatus.Blocked, result.Status);
        Assert.Contains("protegee", result.Reason);
    }

    [Fact]
    public void ValidateWinget_RequiresExactPackageId()
    {
        var validator = new ApplicationUninstallCommandValidator();

        var blocked = validator.ValidateWinget("vlc", exactMatch: false);
        var allowed = validator.ValidateWinget("VideoLAN.VLC", exactMatch: true);

        Assert.Equal(ApplicationCommandValidationStatus.Blocked, blocked.Status);
        Assert.Equal(ApplicationCommandValidationStatus.Allowed, allowed.Status);
        Assert.Equal(["uninstall", "--id", "VideoLAN.VLC", "--exact"], allowed.Arguments);
    }

    [Fact]
    public async Task LaunchOfficialUninstallAsync_DoesNotLaunchWithoutUserConfirmation()
    {
        var launcher = new RecordingLauncher();
        var service = new ApplicationUninstallService(
            new ApplicationUninstallCommandValidator(),
            launcher,
            new ApplicationRemnantScanner());
        var application = new InstalledApplication
        {
            Id = "vlc",
            DisplayName = "VLC media player",
            RiskLevel = ApplicationRiskLevel.SafeToUninstall,
            CanUninstall = true,
            UninstallKind = ApplicationUninstallKind.Winget,
            WingetId = "VideoLAN.VLC"
        };

        var result = await service.LaunchOfficialUninstallAsync(application, false, null, CancellationToken.None);

        Assert.True(result.WasCancelled);
        Assert.False(result.WasLaunched);
        Assert.Equal(0, launcher.Calls);
    }

    [Fact]
    public void BuildPlan_RequiresExplicitConfirmationForEveryLaunchableApplication()
    {
        var service = new ApplicationUninstallService(
            new ApplicationUninstallCommandValidator(),
            new RecordingLauncher(),
            new ApplicationRemnantScanner());

        var plan = service.BuildPlan(SafeWingetApplication());

        Assert.True(plan.CanLaunch);
        Assert.True(plan.RequiresExplicitConfirmation);
        Assert.False(plan.RequiresReinforcedConfirmation);
        Assert.Equal(ApplicationUninstallConfirmationLevel.Explicit, plan.RequiredConfirmationLevel);
    }

    [Fact]
    public void BuildPlan_RequiresReinforcedConfirmationForCautionApplications()
    {
        var service = new ApplicationUninstallService(
            new ApplicationUninstallCommandValidator(),
            new RecordingLauncher(),
            new ApplicationRemnantScanner());

        var plan = service.BuildPlan(CautionWingetApplication());

        Assert.True(plan.CanLaunch);
        Assert.True(plan.RequiresExplicitConfirmation);
        Assert.True(plan.RequiresReinforcedConfirmation);
        Assert.Equal(ApplicationUninstallConfirmationLevel.Reinforced, plan.RequiredConfirmationLevel);
    }

    [Fact]
    public async Task LaunchOfficialUninstallAsync_BlocksCautionWithoutReinforcedConfirmation()
    {
        var launcher = new RecordingLauncher();
        var service = new ApplicationUninstallService(
            new ApplicationUninstallCommandValidator(),
            launcher,
            new ApplicationRemnantScanner());

        var result = await service.LaunchOfficialUninstallAsync(
            CautionWingetApplication(),
            new ApplicationUninstallConfirmation
            {
                ExplicitlyConfirmed = true,
                ReinforcedConfirmed = false,
                Source = "test"
            },
            null,
            CancellationToken.None);

        Assert.True(result.WasCancelled);
        Assert.False(result.WasLaunched);
        Assert.Equal(0, launcher.Calls);
        Assert.Contains("renforcee", result.Result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchOfficialUninstallAsync_LaunchesCautionOnlyWithReinforcedConfirmation()
    {
        var launcher = new RecordingLauncher();
        var service = new ApplicationUninstallService(
            new ApplicationUninstallCommandValidator(),
            launcher,
            new ApplicationRemnantScanner());

        var result = await service.LaunchOfficialUninstallAsync(
            CautionWingetApplication(),
            new ApplicationUninstallConfirmation
            {
                ExplicitlyConfirmed = true,
                ReinforcedConfirmed = true,
                Source = "test"
            },
            null,
            CancellationToken.None);

        Assert.True(result.WasLaunched);
        Assert.True(result.WasExplicitlyConfirmed);
        Assert.True(result.WasReinforcedConfirmed);
        Assert.Equal(1, launcher.Calls);
        Assert.Equal("winget", launcher.Executable);
        Assert.Equal(["uninstall", "--id", "Example.ProjectTool", "--exact"], launcher.Arguments);
    }

    [Fact]
    public async Task LaunchOfficialUninstallAsync_LaunchesOnlyValidatedOfficialCommand()
    {
        var launcher = new RecordingLauncher();
        var service = new ApplicationUninstallService(
            new ApplicationUninstallCommandValidator(),
            launcher,
            new ApplicationRemnantScanner());
        var application = new InstalledApplication
        {
            Id = "vlc",
            DisplayName = "VLC media player",
            RiskLevel = ApplicationRiskLevel.SafeToUninstall,
            CanUninstall = true,
            UninstallKind = ApplicationUninstallKind.Winget,
            WingetId = "VideoLAN.VLC"
        };

        var result = await service.LaunchOfficialUninstallAsync(application, true, null, CancellationToken.None);

        Assert.True(result.WasLaunched);
        Assert.Equal(1, launcher.Calls);
        Assert.Equal("winget", launcher.Executable);
        Assert.Equal(["uninstall", "--id", "VideoLAN.VLC", "--exact"], launcher.Arguments);
    }

    [Fact]
    public void ApplicationsView_DoesNotExposeDirectUninstallButtonOnCards()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Virgil.App", "Views", "ApplicationsView.xaml.cs"));

        Assert.DoesNotContain("CreateButton(\"DESINSTALLER\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ShowDetails(application)", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LaunchUninstallButton", source, StringComparison.OrdinalIgnoreCase);
    }

    private static InstalledApplication SafeWingetApplication()
    {
        return new InstalledApplication
        {
            Id = "vlc",
            DisplayName = "VLC media player",
            RiskLevel = ApplicationRiskLevel.SafeToUninstall,
            CanUninstall = true,
            UninstallKind = ApplicationUninstallKind.Winget,
            WingetId = "VideoLAN.VLC"
        };
    }

    private static InstalledApplication CautionWingetApplication()
    {
        return new InstalledApplication
        {
            Id = "project-tool",
            DisplayName = "Example Project Tool",
            RiskLevel = ApplicationRiskLevel.Caution,
            RiskReason = "Application pouvant contenir projets et presets.",
            CanUninstall = true,
            UninstallKind = ApplicationUninstallKind.Winget,
            WingetId = "Example.ProjectTool"
        };
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Virgil.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Racine de test introuvable.");
    }

    private sealed class RecordingLauncher : IApplicationProcessLauncher
    {
        public int Calls { get; private set; }

        public string Executable { get; private set; } = string.Empty;

        public IReadOnlyList<string> Arguments { get; private set; } = Array.Empty<string>();

        public Task<ApplicationLaunchResult> LaunchAsync(
            string executable,
            IReadOnlyList<string> arguments,
            bool useShellExecute,
            CancellationToken cancellationToken)
        {
            Calls++;
            Executable = executable;
            Arguments = arguments;
            return Task.FromResult(new ApplicationLaunchResult(true, null, null));
        }
    }
}
