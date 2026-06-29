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
