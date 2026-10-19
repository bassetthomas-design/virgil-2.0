using Virgil.Core.Applications;
using Virgil.Core.Processes;

namespace Virgil.Infrastructure.Applications;

public sealed class InstallationLocationService : IInstallationLocationService
{
    private readonly IProcessRunner _processRunner;

    public InstallationLocationService(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<InstallationLocationOpenResult> OpenAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return InstallationLocationOpenResult.Missing;

        try
        {
            string arguments;
            if (Directory.Exists(path))
                arguments = $"\"{path}\"";
            else if (File.Exists(path))
                arguments = $"/select,\"{path}\"";
            else
                return InstallationLocationOpenResult.Missing;

            var result = await _processRunner.LaunchAsync(new ProcessLaunchRequest(
                "explorer.exe", Array.Empty<string>(), UseShellExecute: true, RawArguments: arguments),
                cancellationToken).ConfigureAwait(false);
            return result.LaunchError is null
                ? InstallationLocationOpenResult.Opened
                : InstallationLocationOpenResult.Failed;
        }
        catch
        {
            return InstallationLocationOpenResult.Failed;
        }
    }
}
