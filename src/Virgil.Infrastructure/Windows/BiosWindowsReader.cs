using Virgil.Core.Processes;
using Virgil.Core.Updates;
using System.Text.Json;

namespace Virgil.Infrastructure.Windows;

public sealed class BiosWindowsReader(IProcessRunner runner) : IBiosInformationReader
{
    public async Task<BiosInformation> ReadAsync(CancellationToken token)
    {
        var result = await runner.RunAsync(new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; Get-CimInstance Win32_BIOS -ErrorAction Stop | Select-Object Manufacturer,SMBIOSBIOSVersion | ConvertTo-Json -Compress"], TimeSpan.FromSeconds(20)), token).ConfigureAwait(false);
        if (result.Cancelled) throw new OperationCanceledException(token);
        if (result.ExitCode != 0 || result.TimedOut || result.LaunchError is not null) return new("", "");
        try
        {
            using var json = JsonDocument.Parse(result.StandardOutput);
            return new(json.RootElement.GetProperty("Manufacturer").GetString() ?? "", json.RootElement.GetProperty("SMBIOSBIOSVersion").GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return new("", ""); }
    }
}
