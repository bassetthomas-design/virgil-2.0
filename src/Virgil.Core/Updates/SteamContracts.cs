namespace Virgil.Core.Updates;

public sealed record SteamGameState(string Id, string Name, uint StateFlags, string BuildId, string? Error = null);
public sealed record SteamSession(bool IsOpen, string? ExecutablePath);
public sealed record SteamExecutableCheck(bool IsValid, string Reason);
public interface ISteamClient
{
    Task<SteamSession> ReadSessionAsync(CancellationToken token);
    Task<IReadOnlyList<SteamGameState>> ReadGamesAsync(CancellationToken token);
    Task<SteamExecutableCheck> CheckExecutableAsync(string? path, CancellationToken token);
    Task<bool> LaunchSilentAsync(string path, CancellationToken token);
}
public interface ISteamUpdateBackend
{
    ISteamClient SteamClient { get; }
    TimeProvider SteamClock => TimeProvider.System;
    TimeSpan SteamWaitLimit => TimeSpan.FromMinutes(10);
}
public interface ISteamReadAccess
{
    bool ReadHasActiveUser();
    string? ReadExecutablePath();
    bool FileExists(string path);
    string ReadText(string path);
    IReadOnlyList<string> EnumerateManifests(string directory);
}
