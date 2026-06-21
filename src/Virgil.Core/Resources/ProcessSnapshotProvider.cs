using System.Diagnostics;

namespace Virgil.Core.Resources;

public sealed record ProcessObservation
{
    public int ProcessId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? MainWindowTitle { get; init; }

    public string? Path { get; init; }

    public string? Publisher { get; init; }

    public long WorkingSetBytes { get; init; }

    public TimeSpan TotalProcessorTime { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public bool HasMainWindow { get; init; }

    public bool AccessDenied { get; init; }
}

public sealed record ProcessObservationBatch(
    IReadOnlyList<ProcessObservation> Processes,
    IReadOnlyList<string> Errors);

public interface IProcessSnapshotProvider
{
    ProcessObservationBatch Capture();

    string? TryGetPublisher(string? executablePath);
}

public sealed class ProcessSnapshotProvider : IProcessSnapshotProvider
{
    public ProcessObservationBatch Capture()
    {
        var observations = new List<ProcessObservation>();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return new ProcessObservationBatch(observations, ["Liste des processus indisponible."]);
        }

        foreach (var process in processes)
        {
            using (process)
            {
                var observation = TryCapture(process);
                if (observation is not null)
                {
                    observations.Add(observation);
                }
            }
        }

        return new ProcessObservationBatch(observations, Array.Empty<string>());
    }

    public string? TryGetPublisher(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(executablePath).CompanyName;
        }
        catch
        {
            return null;
        }
    }

    private static ProcessObservation? TryCapture(Process process)
    {
        try
        {
            var name = process.ProcessName;
            var processId = process.Id;
            var workingSet = Math.Max(0, process.WorkingSet64);
            var processorTime = process.TotalProcessorTime;
            var title = SafeRead(() => process.MainWindowTitle);
            var startedAt = SafeReadDate(() => process.StartTime.ToUniversalTime());
            var path = SafeRead(() => process.MainModule?.FileName);
            var hasWindow = SafeReadHandle(process);

            return new ProcessObservation
            {
                ProcessId = processId,
                Name = name,
                MainWindowTitle = string.IsNullOrWhiteSpace(title) ? null : title,
                Path = path,
                WorkingSetBytes = workingSet,
                TotalProcessorTime = processorTime,
                StartedAt = startedAt,
                HasMainWindow = hasWindow,
                AccessDenied = path is null && processId > 4
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeRead(Func<string?> reader)
    {
        try
        {
            return reader();
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? SafeReadDate(Func<DateTime> reader)
    {
        try
        {
            return new DateTimeOffset(DateTime.SpecifyKind(reader(), DateTimeKind.Utc));
        }
        catch
        {
            return null;
        }
    }

    private static bool SafeReadHandle(Process process)
    {
        try
        {
            return process.MainWindowHandle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }
}
