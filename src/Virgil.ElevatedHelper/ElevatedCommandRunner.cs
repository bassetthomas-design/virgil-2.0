using System.Diagnostics;

namespace Virgil.ElevatedHelper;

public interface IElevatedCommandRunner
{
    Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command);
}

public sealed class ElevatedCommandRunner : IElevatedCommandRunner
{
    public async Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command)
    {
        var executablePath = Path.Combine(Environment.SystemDirectory, command.FileName);
        if (!File.Exists(executablePath))
        {
            return new ElevatedCommandResult(-1, string.Empty, $"{command.FileName} indisponible.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ElevatedCommandResult(-1, string.Empty, "Processus non demarre.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);

            return new ElevatedCommandResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ElevatedCommandResult(-1, string.Empty, ex.Message);
        }
    }
}

public sealed record ElevatedCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
