using System.Text.RegularExpressions;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

public sealed class LotReparationServicesTests
{
    internal const string UpdateScript = "$ErrorActionPreference='Stop'; Restart-Service -Name wuauserv -ErrorAction Stop; Get-Service wuauserv";
    internal const string AudioScript = "$ErrorActionPreference='Stop'; Restart-Service -Name Audiosrv -ErrorAction Stop; Get-Service Audiosrv,AudioEndpointBuilder";

    internal static readonly (string Name, string Text)[] Scripts =
    [
        ("Services", RepairScripts.Services),
        ("PrintQueue", RepairScripts.PrintQueue),
        ("RestorePoint", RepairScripts.RestorePoint),
        ("StartupRestorePoint", ElevatedActionAllowlist.StartupRestorePointScript),
        ("UpdateRestorePointRead", UpdateRestorePoint.ReadScript),
        ("virgil-update", UpdateScript),
        ("virgil-audio", AudioScript)
    ];

    [Fact]
    public void Helper_powershell_scripts_contain_no_typographic_quotes()
    {
        var quotes = Scripts.SelectMany(script => script.Text.Split('\n')
            .SelectMany((line, index) => line.Where(c => c is >= '\u2018' and <= '\u201e')
                .Select(c => $"{script.Name}:{index + 1}:U+{(int)c:X4}"))).ToArray();
        Assert.Empty(quotes);
    }

    [Fact]
    public void Runner_script_switch_is_a_closed_inventory()
    {
        var source = File.ReadAllText(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(),
            "src", "Virgil.ElevatedHelper", "ElevatedCommandRunner.cs"));
        var arms = Regex.Matches(source, @"""(virgil-[^""]+)""\s*=>\s*(.+),")
            .Select(match => match.Groups[1].Value + "=>" + match.Groups[2].Value).ToArray();
        Assert.Equal(new[]
        {
            "virgil-update=>\"$ErrorActionPreference='Stop'; Restart-Service -Name wuauserv -ErrorAction Stop; Get-Service wuauserv\"",
            "virgil-audio=>\"$ErrorActionPreference='Stop'; Restart-Service -Name Audiosrv -ErrorAction Stop; Get-Service Audiosrv,AudioEndpointBuilder\"",
            "virgil-print=>RepairScripts.PrintQueue",
            "virgil-services=>RepairScripts.Services",
            "virgil-restore-point=>RepairScripts.RestorePoint"
        }, arms);
    }
}
