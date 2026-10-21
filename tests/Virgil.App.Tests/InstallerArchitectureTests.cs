using System.Text.RegularExpressions;
using Xunit;

namespace Virgil.Tests;

public sealed class InstallerArchitectureTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), path));

    [Fact]
    public void BinariesRequireMachineProtectedX64DirectoryIncludingCommandLineOverride()
    {
        var source = Read("installer/Virgil.iss");
        foreach (var required in new[] { "PrivilegesRequired=admin", "DefaultDirName={autopf}\\Virgil",
            "ArchitecturesAllowed=x64compatible", "ArchitecturesInstallIn64BitMode=x64compatible",
            "UsePreviousAppDir=no", "DisableDirPage=yes", "function PrepareToInstall", "Installation refusée",
            "CompareText(RemoveBackslash(ExpandConstant('{app}'))", "AppId={{79A190B0-5849-4E86-909F-81950C48A7D1}" })
            Assert.Contains(required, source);
        var files = source.Split("[Files]")[1].Split("[Icons]")[0];
        Assert.Contains("Virgil.ElevatedHelper.exe", files);
        Assert.Contains("recursesubdirs createallsubdirs", files);
        var destinations = Regex.Matches(files, @"DestDir: ""([^""]+)""").Cast<Match>().ToArray();
        Assert.NotEmpty(destinations);
        Assert.All(destinations, match => Assert.Equal("{app}", match.Groups[1].Value));
        Assert.DoesNotContain("{userappdata}", files);
        Assert.DoesNotContain("{localappdata}", files);
    }

    [Fact]
    public void UpgradeNeverKillsAndOptionalLaunchUsesOriginalUser()
    {
        var source = Read("installer/Virgil.iss");
        Assert.Contains("AppMutex=Local\\Virgil.SingleInstance.v2", source);
        Assert.Contains("CloseApplications=no", source);
        Assert.Contains("RestartApplications=no", source);
        Assert.Contains("unchecked runasoriginaluser skipifsilent", source);
        Assert.Contains("Flags: unchecked", source);
        Assert.Contains("compiler:Languages\\French.isl", source);
    }

    [Fact]
    public void UninstallNeverDeletesQuarantineOrParentAndRequiresExplicitOptIn()
    {
        var source = Read("installer/Virgil.iss");
        Assert.DoesNotContain("[UninstallDelete]", source);
        Assert.Contains("DeleteUserData := False", source);
        Assert.Contains("Choice.Checked := False", source);
        Assert.Contains("DeleteUserData := Result and Choice.Checked", source);
        Assert.Contains("and DeleteUserData then", source);
        Assert.Contains("if UninstallSilent then exit", source);
        Assert.Contains("%LOCALAPPDATA%\\Virgil\\Quarantine", source);
        var targets = Regex.Matches(source, @"(?:DeleteFile|DelTree)\(ExpandConstant\('([^']+)'\)")
            .Cast<Match>().Select(m => m.Groups[1].Value).Order().ToArray();
        Assert.Equal(new[] { "{localappdata}\\Virgil\\logs", "{userappdata}\\Virgil\\reports", "{userappdata}\\Virgil\\settings.json" }, targets);
        Assert.Contains("\"Virgil\", \"logs\"", Read("src/Virgil.Infrastructure/Storage/VirgilPaths.cs"));
        Assert.Contains("\"Virgil\", \"settings.json\"", Read("src/Virgil.Infrastructure/Storage/VirgilPaths.cs"));
        Assert.Contains("\"reports\"", Read("src/Virgil.Core/Reports/ReportHistoryService.cs"));
        Assert.Contains("\"Virgil\", \"Quarantine\"", Read("src/Virgil.Infrastructure/Windows/QuarantineStore.cs"));
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task ReleaseVersionPureFunctionAcceptsTagsAndRejectsUnsafeInput()
    {
        var start = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "installer", "Test-ReleaseVersion.ps1") })
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start);
        Assert.NotNull(process);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(await error);
        Assert.Contains("3 cas valides et 8 refus validés", await output);
    }

    [Fact]
    public void BothWorkflowsBuildSameInstallerAndReleasePermissionsAreJobScoped()
    {
        var build = Read(".github/workflows/build.yml");
        var release = Read(".github/workflows/release.yml");
        foreach (var workflow in new[] { build, release })
        {
            Assert.Contains("dotnet restore Virgil.sln", workflow);
            Assert.Contains("dotnet build Virgil.sln --configuration Release --no-restore", workflow);
            Assert.Contains("dotnet test Virgil.sln --configuration Release --no-build", workflow);
            Assert.Contains("--self-contained true", workflow);
            Assert.Contains("./installer/Build-Installer.ps1", workflow);
        }
        Assert.Contains("if-no-files-found: error", build);
        Assert.Contains("permissions: {}", release);
        Assert.Contains("    permissions:\n      contents: write", release.Replace("\r\n", "\n"));
        Assert.Contains("- 'v*'", release);
        Assert.Contains("--verify-tag --prerelease", release);
        Assert.Contains("Get-FileHash $setup -Algorithm SHA256", release);
        Assert.Contains("--notes-file docs/RELEASE_NOTES_2.0.0-alpha.md", release);
    }
}
