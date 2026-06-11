using System.Runtime.InteropServices;
using Microsoft.Win32;
using Virgil.Domain;

namespace Virgil.Core.Scanning;

internal static class ProcessorReader
{
    private const string NotAvailable = "N/A";

    public static async Task<ScanReaderResult<ProcessorScanInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var name = ReadProcessorName();
        var logicalProcessorCount = Math.Max(1, Environment.ProcessorCount);
        var usagePercent = 0d;

        try
        {
            usagePercent = await MeasureUsageAsync(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            errors.Add("Mesure CPU indisponible.");
        }

        var severity = errors.Count == 0
            ? ScanRules.CalculateCpuSeverity(usagePercent)
            : ScanSeverity.Information;

        var status = errors.Count == 0
            ? ScanRules.StatusForSeverity(severity)
            : NotAvailable;

        return new ScanReaderResult<ProcessorScanInfo>(
            new ProcessorScanInfo(name, logicalProcessorCount, usagePercent, severity, status),
            errors);
    }

    public static double MeasureUsageBlocking()
    {
        return MeasureUsageAsync(TimeSpan.FromMilliseconds(250), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static async Task<double> MeasureUsageAsync(TimeSpan sampleDelay, CancellationToken cancellationToken)
    {
        var first = ReadSystemTimes();
        await Task.Delay(sampleDelay, cancellationToken).ConfigureAwait(false);
        var second = ReadSystemTimes();

        var idle = second.Idle - first.Idle;
        var kernel = second.Kernel - first.Kernel;
        var user = second.User - first.User;
        var total = kernel + user;

        if (total <= 0)
        {
            return 0;
        }

        var busy = total - idle;
        var percent = busy <= 0 ? 0 : busy / (double)total * 100;
        return Math.Round(Math.Clamp(percent, 0, 100), 1);
    }

    private static CpuTimes ReadSystemTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            throw new InvalidOperationException("GetSystemTimes failed.");
        }

        return new CpuTimes(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
    }

    private static string ReadProcessorName()
    {
        try
        {
            return Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                    "ProcessorNameString",
                    null)
                ?.ToString()
                ?.Trim() ?? NotAvailable;
        }
        catch
        {
            return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? NotAvailable;
        }
    }

    private static ulong ToUInt64(FileTime fileTime)
    {
        return ((ulong)fileTime.HighDateTime << 32) | fileTime.LowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    private sealed record CpuTimes(ulong Idle, ulong Kernel, ulong User);
}
