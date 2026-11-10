using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class SteamProcessLauncher(IProcessRunner runner, SteamExecutableGuard guard)
{
    public Task<bool> LaunchAsync(string path, CancellationToken token)
    {
        _ = runner;
        _ = guard;
        _ = path;
        _ = token;
        return Task.FromResult(false);
    }
}
