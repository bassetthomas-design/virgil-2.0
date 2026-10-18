namespace Virgil.Infrastructure.Storage;

public sealed class VirgilPaths
{
    public VirgilPaths(string? localAppDataRoot = null, string? appDataRoot = null)
    {
        LogsDirectory = Path.Combine(localAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Virgil", "logs");
        SettingsFile = Path.Combine(appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virgil", "settings.json");
    }

    public string LogsDirectory { get; }
    public string SettingsFile { get; }
    public void EnsureLogsDirectory() => Directory.CreateDirectory(LogsDirectory);
    public void EnsureSettingsDirectory() => Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
}
