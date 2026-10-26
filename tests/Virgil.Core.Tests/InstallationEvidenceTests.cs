using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class InstallationEvidenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-21T12:00:00Z");
    private static SoftwareFootprintCandidate Candidate(string name, string registryKey)
    {
        var root = Path.GetFullPath("evidence-sandbox");
        var path = Path.Combine(root, name);
        return new(name, path, root, [new(Path.Combine(path, "cache.tmp"), 1024, Now)], [registryKey], Now);
    }

    [Fact]
    public async Task Division2_Steam_Installed_NotLeftover()
    {
        var candidate = Candidate("The Division 2 - Steam", @"HKLM\SOFTWARE\WOW6432Node\Ubisoft\The Division 2 - Steam");
        var snapshot = new SoftwareFootprintSnapshot([new("Steam App 2221490", [])], [], [candidate])
        { Evidence = [new("Tom Clancy's The Division 2", [Path.GetFullPath("SteamLibrary/steamapps/common/The Division 2")], "Manifeste Steam")] };
        Assert.Empty(new LeftoverAnalyzer().Analyze(await new FakeSource(snapshot).ReadAsync(CancellationToken.None), Now));
    }

    [Fact]
    public void WeGame_Installed_NotLeftover()
    {
        var candidate = Candidate("WeGame", @"HKLM\SOFTWARE\WOW6432Node\Tencent\WeGame");
        var snapshot = new SoftwareFootprintSnapshot([], [], [candidate])
        { Evidence = [new("腾讯WeGame", [candidate.Path], "InstallPath", [candidate.RegistryKeys[0]])] };
        Assert.Empty(new LeftoverAnalyzer().Analyze(snapshot, Now));
    }

    [Fact]
    public void SteamService_Installed_NotLeftover()
    {
        var candidate = Candidate("SteamService", @"HKLM\SOFTWARE\WOW6432Node\Valve\SteamService");
        Assert.Empty(new LeftoverAnalyzer().Analyze(new([], [], [candidate])
        { Evidence = [new("Steam", [], "Steam Client Service")] }, Now));
    }

    [Fact]
    public void ExistingVendorInstallPathProtectsChildKeysWithUnrelatedNames()
    {
        var candidate = Candidate("UnknownGame", @"HKLM\SOFTWARE\Vendor\UnknownGame");
        Assert.Empty(new LeftoverAnalyzer().Analyze(new([], [], [candidate])
        { Evidence = [new("Vendor", [Path.GetFullPath("installed-vendor")], "InstallDir", [@"HKLM\SOFTWARE\Vendor"])] }, Now));
    }

    [Fact]
    public void RegistryOnlyAndEmptyCandidatesAreOmitted()
    {
        var candidate = Candidate("Gone", @"HKCU\SOFTWARE\Gone");
        Assert.Empty(new LeftoverAnalyzer().Analyze(new([], [], [candidate with { Path = "", Files = [] },
            candidate with { Files = [candidate.Files[0] with { SizeBytes = 0 }] }]), Now));
    }

    [Fact]
    public void IncompleteInventoryIsNeverLabelledUninstalledOrGroupSelectable()
    {
        var preview = Assert.Single(new LeftoverAnalyzer().Analyze(new([], [], [Candidate("Gone", @"HKCU\SOFTWARE\Gone")], false), Now));
        Assert.True(preview.Definition.NeedsReview);
        Assert.DoesNotContain("(désinstallé)", preview.Definition.DisplayName);
        Assert.Contains("Reste possible — à vérifier", preview.Definition.DisplayName);
        Assert.All(preview.Candidates, c => { Assert.True(c.NeedsReview); });
    }

    [Theory]
    [InlineData("SteamService", "Steam", true)]
    [InlineData("The Division 2 - Steam", "Tom Clancy's The Division 2", true)]
    [InlineData("WeGame", "腾讯WeGame", true)]
    [InlineData("abc", "abcdef", false)]
    [InlineData("", "Something", false)]
    public void ConservativeNameMatching(string candidate, string installed, bool expected) =>
        Assert.Equal(expected, LeftoverAnalyzer.MatchesInstalledName(candidate, installed));

    [Fact]
    public void ParsesModernAndLegacySteamLibraries()
    {
        var modern = "\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Steam\" \"apps\" { \"2221490\" \"1234\" } } \"1\" { \"path\" \"D:\\\\Games\" } }";
        Assert.Equal(new[] { @"C:\Steam", @"D:\Games" }, LauncherManifestParser.SteamLibraries(modern));
        Assert.Equal(new[] { @"E:\SteamLibrary" }, LauncherManifestParser.SteamLibraries("\"LibraryFolders\" { \"1\" \"E:\\\\SteamLibrary\" }"));
    }

    [Fact]
    public void ParsesSteamApplicationAndEpicManifest()
    {
        var steam = LauncherManifestParser.SteamApplication("\"AppState\" { \"appid\" \"2221490\" \"name\" \"Tom Clancy's The Division 2\" \"installdir\" \"The Division 2\" }");
        Assert.Equal("Tom Clancy's The Division 2", steam.Name);
        Assert.Equal("The Division 2", steam.InstallDirectory);
        Assert.Equal(("Example", @"D:\Games\Example"), LauncherManifestParser.EpicApplication("{\"DisplayName\":\"Example\",\"InstallLocation\":\"D:\\\\Games\\\\Example\"}"));
    }

    [Fact]
    public void ParsesSteamSampleFiles()
    {
        var fixtures = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "tests", "Virgil.Core.Tests", "Fixtures", "Steam");
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" },
            LauncherManifestParser.SteamLibraries(File.ReadAllText(Path.Combine(fixtures, "libraryfolders.vdf"))));
        var app = LauncherManifestParser.SteamApplication(File.ReadAllText(Path.Combine(fixtures, "appmanifest_2221490.acf")));
        Assert.Equal("Tom Clancy's The Division 2", app.Name);
        Assert.Equal("Tom Clancy's The Division 2", app.InstallDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"AppState\" { \"name\" \"Example\" }")]
    [InlineData("\"AppState\" { \"name\" \"Example\" \"installdir\" \"Example\"")]
    public void InvalidSteamManifestFailsInsteadOfInventingAbsence(string text) =>
        Assert.Throws<FormatException>(() => LauncherManifestParser.SteamApplication(text));

    private sealed class FakeSource(SoftwareFootprintSnapshot snapshot) : ISoftwareFootprintSource
    {
        public Task<SoftwareFootprintSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }
}
