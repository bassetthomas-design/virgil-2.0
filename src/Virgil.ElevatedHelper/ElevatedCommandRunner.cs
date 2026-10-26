using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Virgil.ElevatedHelper;

public interface IElevatedCommandRunner
{
    Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command);
    Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command, IProgress<int?>? progress,
        CancellationToken cancellationToken) => RunAsync(command);
}

public sealed class ElevatedCommandRunner : IElevatedCommandRunner
{
    public static TimeSpan TimeoutFor(string executable) => executable.ToLowerInvariant() switch
    {
        "sfc.exe" => TimeSpan.FromMinutes(45),
        "dism.exe" => TimeSpan.FromMinutes(60),
        _ => TimeSpan.FromMinutes(5)
    };

    public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command) => RunAsync(command, null, CancellationToken.None);

    public async Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command, IProgress<int?>? progress,
        CancellationToken cancellationToken)
    {
        var script = command.FileName switch
        {
            "virgil-update" => "$ErrorActionPreference='Stop'; Restart-Service -Name wuauserv -ErrorAction Stop; Get-Service wuauserv",
            "virgil-audio" => "$ErrorActionPreference='Stop'; Restart-Service -Name Audiosrv -ErrorAction Stop; Get-Service Audiosrv,AudioEndpointBuilder",
            "virgil-print" => RepairScripts.PrintQueue,
            "virgil-services" => RepairScripts.Services,
            "virgil-restore-point" => RepairScripts.RestorePoint,
            _ => null
        };
        var executablePath = script is null
            ? Path.Combine(Environment.SystemDirectory, command.FileName)
            : Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executablePath))
            return new ElevatedCommandResult(-1, string.Empty, $"{command.FileName} indisponible.");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = command.FileName == "sfc.exe" ? Encoding.Unicode
            : Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = encoding, StandardErrorEncoding = encoding
        };
        if (script is not null)
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(script);
        }
        else foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);

        using var timeout = new CancellationTokenSource(TimeoutFor(command.FileName));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!process.Start()) return new ElevatedCommandResult(-1, "", "Processus non démarré.");
            var output = ReadAsync(process.StandardOutput, progress);
            var error = ReadAsync(process.StandardError, null);
            try { await process.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
                return new ElevatedCommandResult(-1, await output.ConfigureAwait(false),
                    cancellationToken.IsCancellationRequested ? "Annulation demandée : commande arrêtée." : "Délai maximal de la commande dépassé.",
                    Cancelled: cancellationToken.IsCancellationRequested, TimedOut: !cancellationToken.IsCancellationRequested);
            }
            var text = await output.ConfigureAwait(false);
            if (command.FileName == "virgil-services") text += "\n" + DefaultAudioEndpoint.Check();
            return new ElevatedCommandResult(process.ExitCode, text, await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException) { return new ElevatedCommandResult(-1, "", "Commande annulée avant démarrage.", Cancelled: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { return new ElevatedCommandResult(-1, "", ex.Message); }
    }

    private static async Task<string> ReadAsync(StreamReader reader, IProgress<int?>? progress)
    {
        var output = new StringBuilder();
        var line = new StringBuilder();
        var buffer = new char[256];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            foreach (var character in buffer.AsSpan(0, count).ToArray())
            {
                if (output.Length < 65536) output.Append(character);
                if (character is '\r' or '\n')
                {
                    var match = Regex.Match(line.ToString(), @"(\d{1,3}(?:[.,]\d+)?)\s*%");
                    if (match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'),
                        NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percent))
                        progress?.Report(Math.Clamp((int)percent, 0, 100));
                    line.Clear();
                }
                else if (line.Length < 2048) line.Append(character);
            }
        }
        return output.ToString();
    }
}

public sealed record ElevatedCommandResult(int ExitCode, string StandardOutput, string StandardError,
    bool Cancelled = false, bool TimedOut = false);
