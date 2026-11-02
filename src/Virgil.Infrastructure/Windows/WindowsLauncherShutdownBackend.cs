using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class WindowsLauncherShutdownBackend(IProcessRunner runner) : ILauncherShutdownBackend
{
    // Sans inventaire complet des bibliothèques, la politique refuse toute fermeture.
    public async Task<LauncherSnapshot> ReadAsync(string launcherName, CancellationToken token)
    {
        var result = await runner.RunAsync(new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "$session=(Get-Process -Id $PID).SessionId; @(Get-CimInstance Win32_Process | Where-Object { $_.SessionId -eq $session } | Select-Object @{n='Id';e={[int]$_.ProcessId}},@{n='ParentId';e={[int]$_.ParentProcessId}},ExecutablePath) | ConvertTo-Json -Compress"], TimeSpan.FromSeconds(30)), token).ConfigureAwait(false);
        if (result.ExitCode != 0) return new(false, [], []);
        try
        {
            var processes = JsonSerializer.Deserialize<LauncherProcess[]>(result.StandardOutput) ?? [];
            var (known, libraries) = ReadLibraries(launcherName, processes);
            return new(known && libraries.All(Path.IsPathFullyQualified) && processes.All(p => !string.IsNullOrWhiteSpace(p.ExecutablePath)), processes, libraries);
        }
        catch (JsonException) { return new(false, [], []); }
    }
    private static (bool Known, IReadOnlyList<string> Roots) ReadLibraries(string name, IReadOnlyList<LauncherProcess> processes)
    {
        try
        {
            var roots = new List<string>();
            if (name == "Steam")
            {
                var steam = processes.FirstOrDefault(p => string.Equals(Path.GetFileName(p.ExecutablePath), "steam.exe", StringComparison.OrdinalIgnoreCase));
                if (steam is null) return (true, []);
                var directory = Path.GetDirectoryName(steam.ExecutablePath)!;
                var config = Path.Combine(directory, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(config)) return (false, []);
                const string libraryPathPattern = """
                    "path"\s*"([^"]+)"
                    """;
                foreach (Match match in Regex.Matches(File.ReadAllText(config), libraryPathPattern))
                    roots.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), "steamapps", "common"));
                roots.Add(Path.Combine(directory, "steamapps", "common"));
                return (true, roots);
            }
            if (name == "Epic Games")
            {
                var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
                if (!Directory.Exists(manifests)) return (false, []);
                foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    roots.Add(document.RootElement.GetProperty("InstallLocation").GetString() ?? throw new InvalidOperationException());
                }
                return (true, roots);
            }
            var keyPath = name switch
            {
                "EA app" => @"SOFTWARE\EA Games",
                "Battle.net" => @"SOFTWARE\Blizzard Entertainment",
                "WeGame" => @"SOFTWARE\Tencent\WeGame\Games",
                "Ubisoft Connect" => @"SOFTWARE\Ubisoft\Launcher\Installs",
                _ => null
            };
            if (keyPath is null) return (false, []);
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = machine.OpenSubKey(keyPath, false);
            if (key is null) return (false, []);
            foreach (var child in key.GetSubKeyNames())
            {
                using var game = key.OpenSubKey(child, false);
                var valueName = name switch { "EA app" => "Install Dir", "Battle.net" => "InstallPath", _ => "InstallDir" };
                var path = game?.GetValue(valueName) as string;
                if (string.IsNullOrWhiteSpace(path)) return (false, []);
                roots.Add(path);
            }
            return (true, roots);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.Security.SecurityException)
        { return (false, []); }
    }

    public async Task CloseAsync(string executable, bool force, CancellationToken token)
    {
        if (!LauncherCatalog.Executables.Values.Contains(executable, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Fermeture refusée : hors catalogue.");
        var args = force ? new[] { "/IM", executable, "/F" } : new[] { "/IM", executable };
        var result = await runner.RunAsync(executable == "steam.exe" && !force ? new(executable, ["-shutdown"], TimeSpan.FromSeconds(10)) : new("taskkill.exe", args, TimeSpan.FromSeconds(10)), token).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.TimedOut || result.Cancelled || result.LaunchError is not null) throw new InvalidOperationException("Fermeture du lanceur non confirmée.");
    }
}
