using Virgil.Core.Actions;
using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public sealed class InterventionExecutionService : IInterventionExecutionService
{
    private readonly IInterventionElevatedHelperClient _helperClient;
    private readonly IExplorerRestarter _explorerRestarter;
    private readonly IConfirmedPlanGate _gate;

    public InterventionExecutionService(
        IInterventionElevatedHelperClient helperClient,
        IExplorerRestarter explorerRestarter,
        IConfirmedPlanGate gate)
    {
        _gate = gate;
        _helperClient = helperClient;
        _explorerRestarter = explorerRestarter;
    }

    public async Task<InterventionExecutionResult> ExecuteAsync(
        InterventionDiagnostic diagnostic,
        ConfirmedPlan? plan,
        CancellationToken cancellationToken)
        => await ExecuteAsync(diagnostic, plan, null, cancellationToken).ConfigureAwait(false);

    public async Task<InterventionExecutionResult> ExecuteAsync(InterventionDiagnostic diagnostic,
        ConfirmedPlan? plan, IProgress<InterventionProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!diagnostic.IsAvailable)
        {
            return Failure(diagnostic, "Action indisponible selon le diagnostic.");
        }

        var restartExplorer = diagnostic.Definition.Id == InterventionId.RestartExplorer;
        if (!restartExplorer && !diagnostic.Definition.RequiresAdministrator)
        {
            return Failure(diagnostic, "Action locale non prise en charge.");
        }

        var validation = _gate.TryConsume(plan, InterventionActionPreviewFactory.Create(diagnostic.Definition));
        if (!validation.IsAccepted)
            return Failure(diagnostic, "Intervention annulee.\n" + validation.Message);

        if (restartExplorer)
            return await _explorerRestarter.RestartAsync(diagnostic, cancellationToken).ConfigureAwait(false);

        if (InterventionSafety.RequiresRestorePoint(diagnostic.Definition.Id) && !diagnostic.Definition.AllowWithoutRestorePoint)
        {
            progress?.Report(new InterventionProgress { Step = "Création et vérification du point de restauration" });
            var protection = await _helperClient.ExecuteAsync(new InterventionCatalog().Get(InterventionId.CreateRestorePoint),
                progress, cancellationToken).ConfigureAwait(false);
            if (protection.Status != InterventionStatus.Completed)
                return Failure(diagnostic, "Réparation bloquée : point de restauration non créé ou non vérifié. " + protection.ReadableError + "\nActivez la protection du système dans Windows, ou demandez explicitement de continuer sans point de restauration.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _helperClient
            .ExecuteAsync(diagnostic.Definition, progress, cancellationToken)
            .ConfigureAwait(false);

        return new InterventionExecutionResult
        {
            Action = diagnostic.Definition,
            StartedAt = result.StartedAt.ToLocalTime(),
            FinishedAt = result.FinishedAt.ToLocalTime(),
            ExitCode = result.ExitCode,
            Status = result.Status,
            SummaryOutput = result.SummaryOutput,
            ReadableError = result.ReadableError,
            RebootRequired = result.RebootRequired,
            StateBefore = diagnostic.StateBefore,
            StateAfter = StateAfter(result),
            WasConfirmed = true,
            WasElevated = true
        };
    }

    public InterventionExecutionResult Skip(InterventionDiagnostic diagnostic)
    {
        return new InterventionExecutionResult
        {
            Action = diagnostic.Definition,
            Status = InterventionStatus.Skipped,
            ExitCode = 0,
            SummaryOutput = "Action passee par l'utilisateur.",
            StateBefore = diagnostic.StateBefore,
            StateAfter = "Aucune action effectuee.",
            WasConfirmed = false,
            WasElevated = false
        };
    }

    public InterventionExecutionResult Cancel(InterventionDiagnostic diagnostic)
    {
        return new InterventionExecutionResult
        {
            Action = diagnostic.Definition,
            Status = InterventionStatus.Cancelled,
            ExitCode = -1,
            SummaryOutput = "Parcours annule par l'utilisateur.",
            StateBefore = diagnostic.StateBefore,
            StateAfter = "Actions restantes non executees.",
            WasConfirmed = false,
            WasElevated = false
        };
    }

    public InterventionSessionReport CreateReport(
        DateTimeOffset startedAt,
        IReadOnlyList<InterventionDiagnostic> proposedActions,
        IReadOnlyList<InterventionExecutionResult> results,
        IReadOnlyList<string> errors)
    {
        return new InterventionSessionReport
        {
            StartedAt = startedAt,
            Duration = DateTimeOffset.Now - startedAt,
            ProposedActions = proposedActions,
            Results = results,
            Errors = errors
        };
    }

    private static InterventionExecutionResult Failure(InterventionDiagnostic diagnostic, string message)
    {
        var now = DateTimeOffset.Now;
        return new InterventionExecutionResult
        {
            Action = diagnostic.Definition,
            StartedAt = now,
            FinishedAt = now,
            ExitCode = -1,
            Status = InterventionStatus.Failed,
            ReadableError = message,
            StateBefore = diagnostic.StateBefore,
            StateAfter = "Aucune action effectuee.",
            WasConfirmed = false,
            WasElevated = false
        };
    }

    private static string StateAfter(ElevatedInterventionResult result)
    {
        return result.Status switch
        {
            InterventionStatus.Completed => "Action terminee.",
            InterventionStatus.RebootRequired => "Action terminee, redemarrage manuel probablement requis.",
            InterventionStatus.PartialFailure => "Action terminee partiellement.",
            InterventionStatus.Cancelled => "Action annulee avant execution.",
            _ => "Action non terminee."
        };
    }
}
