using Microsoft.Win32;
using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class SteamWindowsClient : ISteamClient
{
    private readonly ISteamReadAccess _access;
    private readonly ILauncherShutdownBackend _processes;
    private readonly SteamExecutableGuard _guard;
    private readonly SteamProcessLauncher _launcher;
    public SteamWindowsClient(IProcessRunner runner, ISteamReadAccess? access = null,
        ILauncherShutdownBackend? processes = null, Func<string, bool>? verifySignature = null)
    {
        _access = access ?? new SteamWindowsReadAccess();
        _processes = processes ?? new WindowsLauncherShutdownBackend(runner);
        _guard = new(_access.FileExists, verifySignature ?? SteamAuthenticode.IsValveSigned);
        _launcher = new(runner, _guard);
    }
    public async Task<SteamSession> ReadSessionAsync(CancellationToken token)
    {
        var path = _access.ReadExecutablePath();
        var processes = await _processes.ReadAsync("Steam", token).ConfigureAwait(false);
        var running = processes.Processes.Any(p =>
            string.Equals(p.Name ?? Path.GetFileName(p.ExecutablePath), "steam.exe", StringComparison.OrdinalIgnoreCase));
        return new(running && _access.ReadHasActiveUser(), path);
    }
    public Task<IReadOnlyList<SteamGameState>> ReadGamesAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        return new SteamInventoryReader(_access).Read();
    }, token);
    public Task<SteamExecutableCheck> CheckExecutableAsync(string? path, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(_guard.Check(path)); }
    public Task<bool> LaunchSilentAsync(string path, CancellationToken token) => _launcher.LaunchAsync(path, token);
}

public sealed class SteamWindowsReadAccess : ISteamReadAccess
{
    public bool ReadHasActiveUser()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess", false);
        // Seul le booléen est transmis au métier ; aucune identité n’est conservée.
        return key?.GetValue("ActiveUser") is int value && value != 0;
    }
    public string? ReadExecutablePath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false);
        return key?.GetValue("SteamExe") as string;
    }
    public bool FileExists(string path) => File.Exists(path);
    public string ReadText(string path) => File.ReadAllText(path);
    public IReadOnlyList<string> EnumerateManifests(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, "appmanifest_*.acf", SearchOption.TopDirectoryOnly) : [];
}
