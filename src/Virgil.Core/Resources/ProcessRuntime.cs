using System.Diagnostics;

namespace Virgil.Core.Resources;

public sealed record ProcessRuntimeIdentity(
    int ProcessId,
    string Name,
    string? Path,
    DateTimeOffset? StartedAt,
    bool HasMainWindow,
    bool AccessDenied);

public interface IProcessRuntime
{
    ProcessRuntimeIdentity? ReadIdentity(int processId);

    bool CloseMainWindow(int processId);

    Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken);

    void Kill(int processId);

    bool FileExists(string path);

    bool OpenLocation(string path);
}

public sealed class ProcessRuntime : IProcessRuntime
{
    public ProcessRuntimeIdentity? ReadIdentity(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return null;
            }

            var name = process.ProcessName;
            var startedAt = TryReadStartedAt(process);
            var path = TryReadPath(process);
            var hasWindow = process.MainWindowHandle != IntPtr.Zero;
            return new ProcessRuntimeIdentity(
                processId,
                name,
                path,
                startedAt,
                hasWindow,
                AccessDenied: path is null);
        }
        catch
        {
            return null;
        }
    }

    public bool CloseMainWindow(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && process.CloseMainWindow();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> WaitForExitAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Kill(int processId)
    {
        using var process = Process.GetProcessById(processId);
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: false);
        }
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool OpenLocation(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var explorerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = explorerPath,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("/select,");
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo)?.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DateTimeOffset? TryReadStartedAt(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
