using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
using Virgil.Core.Interventions;
using Virgil.Contracts;

namespace Virgil.Core.Resources;

public sealed class ProcessActionService : IProcessActionService
{
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ForcedCloseTimeout = TimeSpan.FromSeconds(3);

    private readonly IConfirmedPlanGate _gate;
    private readonly IProcessRuntime _runtime;
    private readonly ProcessProtectionPolicy _protectionPolicy;
    private readonly IExplorerRestarter _explorerRestarter;
    private readonly IInterventionCatalog _interventionCatalog;
    private readonly Func<DateTimeOffset> _now;

    public ProcessActionService(
        IProcessRuntime runtime,
        ProcessProtectionPolicy protectionPolicy,
        IExplorerRestarter explorerRestarter,
        IInterventionCatalog interventionCatalog,
        Func<DateTimeOffset> now,
        IConfirmedPlanGate gate)
    {
        _gate = gate;
        _runtime = runtime;
        _protectionPolicy = protectionPolicy;
        _explorerRestarter = explorerRestarter;
        _interventionCatalog = interventionCatalog;
        _now = now;
    }

    public bool CanReleaseInactiveMemory => false;

    public async Task<ProcessActionResult> ExecuteAsync(
        ProcessActionKind action,
        ProcessResourceInfo? target,
        ConfirmedPlan? plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (action == ProcessActionKind.ReleaseInactiveMemory)
        {
            return Result(
                action,
                "Mémoire inactive",
                ProcessActionStatus.InformationOnly,
                "Action non exécutée.",
                "Information seulement en V1 : aucune API sûre n'est retenue.");
        }

        if (action == ProcessActionKind.RestartExplorer)
        {
            var refusal = Consume(action, target, plan);
            return refusal ?? await RestartExplorerAsync(cancellationToken).ConfigureAwait(false);
        }

        if (target is null)
        {
            return Refusal(action, "N/A", "Processus cible absent.");
        }

        if (action == ProcessActionKind.OpenLocation)
        {
            return await OpenLocationAsync(target, cancellationToken).ConfigureAwait(false);
        }

        var identity = ValidateIdentity(target);
        if (identity.Error is not null)
        {
            return Refusal(action, TargetName(target), identity.Error);
        }

        if (action == ProcessActionKind.CloseMainWindow && (!target.CanCloseGracefully || !identity.Value!.HasMainWindow))
            return Refusal(action, TargetName(target), "Fermeture propre non autorisée.");
        if (action == ProcessActionKind.KillProcess && !target.CanForceClose)
            return Refusal(action, TargetName(target), "Confirmation renforcée absente ou fermeture forcée interdite.");
        if (action is not (ProcessActionKind.CloseMainWindow or ProcessActionKind.KillProcess))
            return Refusal(action, TargetName(target), "Action non prise en charge.");

        var rejected = Consume(action, target, plan);
        if (rejected is not null) return rejected;

        return action switch
        {
            ProcessActionKind.CloseMainWindow =>
                await CloseGracefullyAsync(target, identity.Value!, cancellationToken).ConfigureAwait(false),
            ProcessActionKind.KillProcess =>
                await KillAsync(target, cancellationToken).ConfigureAwait(false),
            _ => Failure(action, TargetName(target), "Action non prise en charge.")
        };
    }

    private ProcessActionResult? Consume(ProcessActionKind action, ProcessResourceInfo? target, ConfirmedPlan? plan)
    {
        var validation = _gate.TryConsume(plan, ProcessActionPreviewFactory.Create(action, target));
        if (!validation.IsAccepted)
            return Refusal(action, TargetName(target), "Action annulée.\n" + validation.Message);
        if (action == ProcessActionKind.KillProcess && plan?.ConfirmationLevel != ConfirmationLevel.Reinforced)
            return Refusal(action, TargetName(target), "Action annulée.\nConsentement insuffisant pour cette action.");
        return null;
    }

    public ProcessActionResult Skip(ProcessActionKind action, string target)
    {
        return Result(action, target, ProcessActionStatus.Skipped, "Action passée.");
    }

    private async Task<ProcessActionResult> CloseGracefullyAsync(
        ProcessResourceInfo target,
        ProcessRuntimeIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!target.CanCloseGracefully || !identity.HasMainWindow)
        {
            return Failure(ProcessActionKind.CloseMainWindow, TargetName(target), "Fermeture propre non autorisée.");
        }

        if (!_runtime.CloseMainWindow(target.ProcessId))
        {
            return Result(
                ProcessActionKind.CloseMainWindow,
                TargetName(target),
                ProcessActionStatus.PartialFailure,
                "Fermeture propre non confirmée.",
                "La fermeture forcée reste séparée et exige une nouvelle validation.");
        }

        var exited = await _runtime.WaitForExitAsync(
            target.ProcessId,
            GracefulCloseTimeout,
            cancellationToken).ConfigureAwait(false);
        return exited
            ? Result(ProcessActionKind.CloseMainWindow, TargetName(target), ProcessActionStatus.Completed, "Application fermée proprement.")
            : Result(
                ProcessActionKind.CloseMainWindow,
                TargetName(target),
                ProcessActionStatus.PartialFailure,
                "Fermeture propre non confirmée.",
                "Aucune fermeture forcée automatique n'a été lancée.");
    }

    private async Task<ProcessActionResult> KillAsync(
        ProcessResourceInfo target,
        CancellationToken cancellationToken)
    {
        try
        {
            _runtime.Kill(target.ProcessId);
            var exited = await _runtime.WaitForExitAsync(
                target.ProcessId,
                ForcedCloseTimeout,
                cancellationToken).ConfigureAwait(false);
            return exited
                ? Result(ProcessActionKind.KillProcess, TargetName(target), ProcessActionStatus.Completed, "Application fermée de force.")
                : Result(
                    ProcessActionKind.KillProcess,
                    TargetName(target),
                    ProcessActionStatus.PartialFailure,
                    "Fermeture forcée non confirmée.");
        }
        catch
        {
            return Failure(ProcessActionKind.KillProcess, TargetName(target), "Fermeture forcée impossible.");
        }
    }

    private async Task<ProcessActionResult> OpenLocationAsync(ProcessResourceInfo target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target.Path) ||
            !_runtime.FileExists(target.Path) ||
            !await _runtime.OpenLocationAsync(target.Path, cancellationToken).ConfigureAwait(false))
        {
            return Failure(ProcessActionKind.OpenLocation, TargetName(target), "Emplacement inaccessible.");
        }

        return Result(
            ProcessActionKind.OpenLocation,
            TargetName(target),
            ProcessActionStatus.Completed,
            "Emplacement ouvert dans Explorer.");
    }

    private async Task<ProcessActionResult> RestartExplorerAsync(CancellationToken cancellationToken)
    {
        var definition = _interventionCatalog.Get(InterventionId.RestartExplorer);
        var diagnostic = new InterventionDiagnostic
        {
            Definition = definition,
            IsAvailable = true,
            Status = InterventionStatus.Available,
            StateBefore = "Relance demandée depuis Ressources.",
            Recommendation = "Relance Explorer après validation explicite."
        };
        var result = await _explorerRestarter.RestartAsync(diagnostic, cancellationToken).ConfigureAwait(false);
        var status = result.Status switch
        {
            InterventionStatus.Completed => ProcessActionStatus.Completed,
            InterventionStatus.Cancelled => ProcessActionStatus.Cancelled,
            InterventionStatus.PartialFailure => ProcessActionStatus.PartialFailure,
            _ => ProcessActionStatus.Failed
        };
        return Result(
            ProcessActionKind.RestartExplorer,
            "Explorer Windows",
            status,
            result.SummaryOutput,
            result.ReadableError);
    }

    private IdentityValidation ValidateIdentity(ProcessResourceInfo target)
    {
        if (target.IsCriticalSystemProcess || !target.CanForceClose && !target.CanCloseGracefully)
        {
            return IdentityValidation.Failed("Processus protégé : action refusée.");
        }

        var current = _runtime.ReadIdentity(target.ProcessId);
        if (current is null)
        {
            return IdentityValidation.Failed("Processus absent ou inaccessible.");
        }

        if (!string.Equals(current.Name, target.Name, StringComparison.OrdinalIgnoreCase) ||
            target.StartedAt is not null && current.StartedAt != target.StartedAt ||
            target.Path is not null && !string.Equals(current.Path, target.Path, StringComparison.OrdinalIgnoreCase))
        {
            return IdentityValidation.Failed("Identité du processus modifiée depuis l'analyse.");
        }

        var observation = new ProcessObservation
        {
            ProcessId = current.ProcessId,
            Name = current.Name,
            Path = current.Path,
            StartedAt = current.StartedAt,
            HasMainWindow = current.HasMainWindow,
            AccessDenied = current.AccessDenied,
            MainWindowTitle = target.MainWindowTitle,
            WorkingSetBytes = target.WorkingSetBytes,
            Publisher = target.Publisher
        };
        var protection = _protectionPolicy.Evaluate(observation, target.CpuPercent);
        return protection.IsCritical
            ? IdentityValidation.Failed("Processus protégé lors de la revérification.")
            : IdentityValidation.Success(current);
    }

    private ProcessActionResult Refusal(ProcessActionKind action, string target, string message)
    {
        return Failure(action, target, message) with { WasRefused = true };
    }

    private ProcessActionResult Failure(ProcessActionKind action, string target, string message)
    {
        return Result(action, target, ProcessActionStatus.Failed, "Aucune action effectuée.", message);
    }

    private ProcessActionResult Result(
        ProcessActionKind action,
        string target,
        ProcessActionStatus status,
        string summary,
        string? error = null)
    {
        return new ProcessActionResult
        {
            Action = action,
            Target = target,
            Status = status,
            Timestamp = _now(),
            Summary = summary,
            ReadableError = error
        };
    }

    private static string TargetName(ProcessResourceInfo? target)
    {
        return target is null ? "N/A" : $"{target.Name} (PID {target.ProcessId})";
    }

    private sealed record IdentityValidation(ProcessRuntimeIdentity? Value, string? Error)
    {
        public static IdentityValidation Success(ProcessRuntimeIdentity value) => new(value, null);

        public static IdentityValidation Failed(string error) => new(null, error);
    }
}
