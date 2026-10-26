using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class WingetClientModuleInstaller(IProcessRunner runner) : IWingetClientModuleInstaller
{
    public async Task<bool> InstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await runner.RunAsync(new ProcessRunRequest("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; Install-Module -Name Microsoft.WinGet.Client -Repository PSGallery -Scope CurrentUser -Force"], TimeSpan.FromMinutes(5)), cancellationToken).ConfigureAwait(false);
        if (result.Cancelled) throw new OperationCanceledException(cancellationToken);
        return result.ExitCode == 0 && !result.TimedOut && string.IsNullOrWhiteSpace(result.LaunchError);
    }
}
