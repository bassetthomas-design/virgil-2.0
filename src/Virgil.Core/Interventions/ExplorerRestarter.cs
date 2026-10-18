using System.Diagnostics;
using Virgil.Core.Processes;
using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public sealed class ExplorerRestarter : IExplorerRestarter
{
    private readonly Func<string, Process[]> _processProvider;
    private readonly IProcessRunner _runner;

    public ExplorerRestarter(
        Func<string, Process[]> processProvider,
        IProcessRunner runner)
    {
        _processProvider = processProvider;
        _runner = runner;
    }

    public async Task<InterventionExecutionResult> RestartAsync(
        InterventionDiagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var before = SafeCountExplorer();
        var status = InterventionStatus.Completed;
        var error = default(string);

        try
        {
            foreach (var process in _processProvider("explorer"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryCloseExplorer(process);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            var result = await _runner.LaunchAsync(
                new ProcessLaunchRequest("explorer.exe", Array.Empty<string>(), UseShellExecute: false),
                cancellationToken).ConfigureAwait(false);
            if (result.LaunchError is not null)
            {
                throw new InvalidOperationException(result.LaunchError);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

            if (SafeCountExplorer() == 0)
            {
                status = InterventionStatus.PartialFailure;
                error = "Explorer n'a pas ete detecte apres relance.";
            }
        }
        catch (OperationCanceledException)
        {
            status = InterventionStatus.Cancelled;
            error = "Relance Explorer annulee avant execution complete.";
        }
        catch
        {
            status = InterventionStatus.Failed;
            error = "Relance Explorer impossible.";
        }

        var after = SafeCountExplorer();
        return new InterventionExecutionResult
        {
            Action = diagnostic.Definition,
            StartedAt = startedAt,
            FinishedAt = DateTimeOffset.Now,
            ExitCode = status is InterventionStatus.Completed ? 0 : -1,
            Status = status,
            SummaryOutput = $"Explorer avant : {before}, apres : {after}.",
            ReadableError = error,
            StateBefore = diagnostic.StateBefore,
            StateAfter = after > 0 ? "Explorer actif." : "Explorer non detecte.",
            WasConfirmed = true,
            WasElevated = false
        };
    }

    private int SafeCountExplorer()
    {
        try
        {
            return _processProvider("explorer").Length;
        }
        catch
        {
            return 0;
        }
    }

    private static void TryCloseExplorer(Process process)
    {
        try
        {
            process.CloseMainWindow();
        }
        catch
        {
            // Never close anything except explorer.exe, and never force-kill here.
        }
    }
}
