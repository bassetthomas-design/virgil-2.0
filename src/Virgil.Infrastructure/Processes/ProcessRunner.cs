using System.Diagnostics;
using Virgil.Core.Processes;

namespace Virgil.Infrastructure.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult(-1, string.Empty, string.Empty, LaunchError: "Processus non demarre.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);

            return new ProcessRunResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);
            return new ProcessRunResult(-1, string.Empty, string.Empty, Cancelled: true);
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            return new ProcessRunResult(-1, string.Empty, string.Empty, TimedOut: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ProcessRunResult(-1, string.Empty, string.Empty, LaunchError: ex.Message);
        }
    }

    public async Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                UseShellExecute = request.UseShellExecute
            };
            if (request.Verb is not null)
            {
                startInfo.Verb = request.Verb;
            }
            if (request.WindowMode == ProcessWindowMode.Hidden)
            {
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            }
            if (request.RawArguments is not null)
            {
                startInfo.Arguments = request.RawArguments;
            }
            else
            {
                foreach (var argument in request.Arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessLaunchResult(false, null);
            }
            if (request.WaitForExit)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                return new ProcessLaunchResult(true, process.ExitCode);
            }
            return new ProcessLaunchResult(true, process.HasExited ? process.ExitCode : null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProcessLaunchResult(false, null, ex.Message);
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup only; the caller receives a cancelled or timed-out result.
        }
    }
}
