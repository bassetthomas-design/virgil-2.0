using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class SteamProcessLauncher(IProcessRunner runner, SteamExecutableGuard guard)
{
    public async Task<bool> LaunchAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!guard.Check(path).IsValid) return false;
        var result = await runner.LaunchAsync(new(path, ["-silent"], false), token).ConfigureAwait(false);
        return result.Started && result.LaunchError is null;
    }
}
