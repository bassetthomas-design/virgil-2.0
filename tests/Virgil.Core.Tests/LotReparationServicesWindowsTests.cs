using System.Diagnostics;
using System.Text;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotReparationServicesWindowsTests
{
    public static IEnumerable<object[]> ParserCases =>
        from script in LotReparationServicesTests.Scripts
        from engine in new[] { "powershell.exe", "pwsh.exe" }
        select new object[] { script.Name, script.Text, engine };

    private static string Engine(string engine) => engine == "powershell.exe"
        ? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");

    [Theory]
    [MemberData(nameof(ParserCases))]
    public async Task Every_helper_script_parses_with_windows_powershell_and_pwsh(string name, string script, string engine)
    {
        Assert.Equal(new[] { name }, LotReparationServicesTests.Scripts.Where(item => item.Name == name).Select(item => item.Name));
        var result = await Run(Engine(engine),
            "$t=$null;$e=$null;[void][System.Management.Automation.Language.Parser]::ParseInput($env:VIRGIL_T2_SCRIPT,[ref]$t,[ref]$e);foreach($x in $e){$x.ErrorId+'|'+$x.Extent.StartLineNumber};'FIN'", script);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new[] { "FIN" }, Lines(result.Output));
    }

    [Theory]
    [InlineData("echec")]
    [InlineData("succes")]
    public async Task Services_print_and_audio_branches_report_their_own_lines(string scenario)
    {
        const string common = """
            [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
            function Test-Path { $false }
            function Get-NetIPConfiguration { [pscustomobject]@{ IPv4DefaultGateway = [pscustomobject]@{ NextHop = '192.0.2.1' } } }
            function Test-Connection { $true }
            function Resolve-DnsName { [pscustomobject]@{ Name = 'www.microsoft.com' } }
            """;
        const string failure = """
            function Get-Service { [CmdletBinding()] param([string]$Name) if($Name -eq 'Spooler'){ throw 'service absent (test)' }; [pscustomobject]@{ Status = 'Running'; StartType = 'Manual' } }
            function Get-Printer { throw 'spouleur indisponible (test)' }
            function Get-PnpDevice { throw 'audio indisponible (test)' }
            function Get-PrintJob { }
            """;
        const string success = """
            function Get-Service { [CmdletBinding()] param([string]$Name) if($Name -eq 'Audiosrv'){ return [pscustomobject]@{ Status = 'Stopped'; StartType = 'Automatic' } }; [pscustomobject]@{ Status = 'Running'; StartType = 'Manual' } }
            function Get-Printer { [pscustomobject]@{ Name = 'Imprimante test' } }
            function Get-PrintJob { }
            function Get-PnpDevice { [pscustomobject]@{ Status = 'OK' }; [pscustomobject]@{ Status = 'OK' }; [pscustomobject]@{ Status = 'Error' } }
            """;
        var result = await Run(Engine("powershell.exe"), common + "\n" +
            (scenario == "echec" ? failure : success) + "\n" + RepairScripts.Services);
        Save("services-" + scenario + ".txt", result.Output, result.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        string[] expected = scenario == "echec" ?
        [
            "VIRGIL|wuauserv|INFO|Running (démarrage à la demande possible)",
            "VIRGIL|AppXSvc|INFO|Running (démarrage à la demande possible)",
            "VIRGIL|ClipSVC|INFO|Running (démarrage à la demande possible)",
            "VIRGIL|Audiosrv|OK|Audiosrv : Running",
            "VIRGIL|AudioEndpointBuilder|OK|AudioEndpointBuilder : Running",
            "VIRGIL|Spooler|UNKNOWN|État du service indisponible",
            "VIRGIL|Reboot|OK|Redémarrage Windows en attente",
            "VIRGIL|Gateway|OK|Réponse de la passerelle",
            "VIRGIL|DNS|OK|Résolution DNS de www.microsoft.com",
            "VIRGIL|PrintQueue|UNKNOWN|File d'impression indisponible",
            "VIRGIL|AudioEndpoint|UNKNOWN|Points de sortie audio indisponibles"
        ] :
        [
            "VIRGIL|wuauserv|INFO|Running (démarrage à la demande possible)",
            "VIRGIL|AppXSvc|INFO|Running (démarrage à la demande possible)",
            "VIRGIL|ClipSVC|INFO|Running (démarrage à la demande possible)",
            "VIRGIL|Audiosrv|FINDING|Audiosrv : Stopped",
            "VIRGIL|AudioEndpointBuilder|OK|AudioEndpointBuilder : Running",
            "VIRGIL|Spooler|OK|Spooler : Running",
            "VIRGIL|Reboot|OK|Redémarrage Windows en attente",
            "VIRGIL|Gateway|OK|Réponse de la passerelle",
            "VIRGIL|DNS|OK|Résolution DNS de www.microsoft.com",
            "VIRGIL|PrintQueue|OK|0 tâche(s) d'impression bloquée(s)",
            "VIRGIL|AudioEndpoint|OK|2 point(s) de sortie audio disponible(s) ; sortie par défaut à vérifier dans Windows"
        ];
        Assert.Equal(expected, Lines(result.Output));
    }

    [Fact]
    public async Task Real_runner_executes_every_service_check()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await new ElevatedCommandRunner().RunAsync(
            new ElevatedCommandSpec("virgil-services", Array.Empty<string>()), null, timeout.Token);
        Save("services-runner.txt", result.StandardOutput, result.StandardError);
        Assert.False(result.Cancelled);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardError);
        var rows = Lines(result.StandardOutput).Where(line => line.StartsWith("VIRGIL|", StringComparison.Ordinal))
            .Select(line => line.Split('|')).ToArray();
        foreach (var row in rows)
        {
            Assert.Equal(4, row.Length);
            Assert.Contains(row[2], new[] { "OK", "FINDING", "INFO", "UNKNOWN" });
        }
        Assert.Equal(new[] { "wuauserv", "AppXSvc", "ClipSVC", "Audiosrv", "AudioEndpointBuilder", "Spooler",
            "Reboot", "DNS", "PrintQueue", "AudioEndpoint", "DefaultAudioEndpoint" },
            rows.Where(row => row[1] != "Gateway").Select(row => row[1]).ToArray());
    }

    private static string[] Lines(string text) => text.TrimStart('\uFEFF')
        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

    private static void Save(string name, string output, string error)
    {
        var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-t2a");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), output + "\nSTDERR:\n" + error, new UTF8Encoding(false));
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(string engine, string command, string? script = null)
    {
        Assert.True(File.Exists(engine), "Moteur PowerShell absent : " + engine);
        var start = new ProcessStartInfo(engine)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
            start.ArgumentList.Add(argument);
        if (script is not null) start.Environment["VIRGIL_T2_SCRIPT"] = script;
        using var process = new Process { StartInfo = start };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var exited = false;
        try { await process.WaitForExitAsync(timeout.Token); exited = true; }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); }
        Assert.True(exited, "Le processus doit se terminer en 60 secondes.");
        return (process.ExitCode, await output, await error);
    }
}
