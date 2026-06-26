using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public interface IApplicationInventorySourceReader
{
    string SourceName { get; }

    Task<ApplicationInventorySourceResult> ReadAsync(CancellationToken cancellationToken);
}

public interface IApplicationInventoryService
{
    Task<ApplicationInventoryReport> InventoryAsync(
        IProgress<ApplicationInventoryProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IApplicationProcessLauncher
{
    Task<ApplicationLaunchResult> LaunchAsync(
        string executable,
        IReadOnlyList<string> arguments,
        bool useShellExecute,
        CancellationToken cancellationToken);
}

public sealed record ApplicationLaunchResult(
    bool Started,
    int? ExitCode,
    string? ReadableError);

