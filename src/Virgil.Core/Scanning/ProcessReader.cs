using System.Diagnostics;
using Virgil.Domain;

namespace Virgil.Core.Scanning;

internal static class ProcessReader
{
    public static ScanReaderResult<IReadOnlyList<ProcessScanInfo>> ReadTopMemoryProcesses(int count)
    {
        var processes = new List<ProcessScanInfo>();
        var errors = new List<string>();

        Process[] runningProcesses;
        try
        {
            runningProcesses = Process.GetProcesses();
        }
        catch
        {
            return new ScanReaderResult<IReadOnlyList<ProcessScanInfo>>(processes, ["Liste des processus indisponible."]);
        }

        foreach (var process in runningProcesses)
        {
            using (process)
            {
                TryAddProcess(processes, process);
            }
        }

        return new ScanReaderResult<IReadOnlyList<ProcessScanInfo>>(
            processes
                .OrderByDescending(process => process.WorkingSetBytes)
                .ThenBy(process => process.ProcessId)
                .Take(count)
                .ToList(),
            errors);
    }

    private static void TryAddProcess(ICollection<ProcessScanInfo> processes, Process process)
    {
        try
        {
            var workingSet = process.WorkingSet64;
            if (workingSet <= 0)
            {
                return;
            }

            var path = TryReadPath(process, out var pathStatus);
            processes.Add(new ProcessScanInfo(
                ReadProcessName(process),
                process.Id,
                workingSet,
                path,
                pathStatus));
        }
        catch
        {
            // Protected processes are skipped; the scan remains read-only.
        }
    }

    private static string ReadProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return "Processus protege";
        }
    }

    private static string? TryReadPath(Process process, out string accessStatus)
    {
        try
        {
            accessStatus = "Chemin accessible";
            return process.MainModule?.FileName;
        }
        catch
        {
            accessStatus = "Chemin inaccessible sans elevation";
            return null;
        }
    }
}
