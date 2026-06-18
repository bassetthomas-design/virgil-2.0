using Virgil.Core.Interventions;
using Virgil.Domain;

namespace Virgil.Core.Resources;

public sealed class ProcessActionService : IProcessActionService
{
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ForcedCloseTimeout = TimeSpan.FromSeconds(3);

    private readonly IProcessRuntime _runtime;
    private readonly ProcessProtectionPolicy _protectionPolicy;
    private readonly IExplorerRestarter _explorerRestarter;
    private readonly IInterventionCatalog _interventionCatalog;
    private readonly Func<DateTimeOffset> _now;

    public ProcessActionService()
        : this(
            new ProcessRuntime(),
            new ProcessProtectionPolicy(),
            new ExplorerRestarter(),
            new InterventionCatalog(),
            () => DateTimeOffset.Now)
    {
    }

    public ProcessActionService(
        IProcessRuntime runtime,
        ProcessProtectionPolicy protectionPolicy,
        IExplorerRestarter explorerRestarter,
        IInterventionCatalog interventionCatalog,
        Func<DateTimeOffset> now)
    {
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
        bool confirmed,
        bool reinforcedConfirmation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (action == ProcessActionKind.ReleaseInactiveMemory)
        {
            return Result(
                action,
                "Memoire inactive",
                ProcessActionStatus.InformationOnly,
                "Action non executee.",
                "Information seulement en V1 : aucune API sure n'est retenue.");
        }

        if (!confirmed)
        {
            return Failure(action, TargetName(target), "Validation explicite absente.");
        }

        if (action == ProcessActionKind.RestartExplorer)
        {
            return await RestartExplorerAsync(cancellationToken).ConfigureAwait(false);
        }

        if (target is null)
        {
            return Failure(action, "N/A", "Processus cible absent.");
        }

        var identity = ValidateIdentity(target);
        if (identity.Error is not null)
        {
            return Failure(action, TargetName(target), identity.Error);
        }

        return action switch
        {
            ProcessActionKind.CloseMainWindow =>
                await CloseGracefullyAsync(target, identity.Value!, cancellationToken).ConfigureAwait(false),
            ProcessActionKind.KillProcess =>
                await KillAsync(target, reinforcedConfirmation, cancellationToken).ConfigureAwait(false),
            ProcessActionKind.OpenLocation => OpenLocation(target),
            _ => Failure(action, TargetName(target), "Action non prise en charge.")
        };
    }

    public ProcessActionResult Skip(ProcessActionKind action, string target)
    {
        return Result(action, target, ProcessActionStatus.Skipped, "Action passee.");
    }

    private async Task<ProcessActionResult> CloseGracefullyAsync(
        ProcessResourceInfo target,
        ProcessRuntimeIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!target.CanCloseGracefully || !identity.HasMainWindow)
        {
            return Failure(ProcessActionKind.CloseMainWindow, TargetName(target), "Fermeture propre non autorisee.");
        }

        if (!_runtime.CloseMainWindow(target.ProcessId))
        {
            return Result(
                ProcessActionKind.CloseMainWindow,
                TargetName(target),
                ProcessActionStatus.PartialFailure,
                "Fermeture propre non confirmee.",
                "La fermeture forcee reste separee et exige une nouvelle validation.");
        }

        var exited = await _runtime.WaitForExitAsync(
            target.ProcessId,
            GracefulCloseTimeout,
            cancellationToken).ConfigureAwait(false);
        return exited
            ? Result(ProcessActionKind.CloseMainWindow, TargetName(target), ProcessActionStatus.Completed, "Application fermee proprement.")
            : Result(
                ProcessActionKind.CloseMainWindow,
                TargetName(target),
                ProcessActionStatus.PartialFailure,
                "Fermeture propre non confirmee.",
                "Aucune fermeture forcee automatique n'a ete lancee.");
    }

    private async Task<ProcessActionResult> KillAsync(
        ProcessResourceInfo target,
        bool reinforcedConfirmation,
        CancellationToken cancellationToken)
    {
        if (!target.CanForceClose || !reinforcedConfirmation)
        {
            return Failure(
                ProcessActionKind.KillProcess,
                TargetName(target),
                "Confirmation renforcee absente ou fermeture forcee interdite.");
        }

        try
        {
            _runtime.Kill(target.ProcessId);
            var exited = await _runtime.WaitForExitAsync(
                target.ProcessId,
                ForcedCloseTimeout,
                cancellationToken).ConfigureAwait(false);
            return exited
                ? Result(ProcessActionKind.KillProcess, TargetName(target), ProcessActionStatus.Completed, "Application fermee de force.")
                : Result(
                    ProcessActionKind.KillProcess,
                    TargetName(target),
                    ProcessActionStatus.PartialFailure,
                    "Fermeture forcee non confirmee.");
        }
        catch
        {
            return Failure(ProcessActionKind.KillProcess, TargetName(target), "Fermeture forcee impossible.");
        }
    }

    private ProcessActionResult OpenLocation(ProcessResourceInfo target)
    {
        if (string.IsNullOrWhiteSpace(target.Path) ||
            !_runtime.FileExists(target.Path) ||
            !_runtime.OpenLocation(target.Path))
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
            StateBefore = "Relance demandee depuis Ressources.",
            Recommendation = "Relance Explorer apres validation explicite."
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
            return IdentityValidation.Failed("Processus protege : action refusee.");
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
            return IdentityValidation.Failed("Identite du processus modifiee depuis l'analyse.");
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
            ? IdentityValidation.Failed("Processus protege lors de la reverification.")
            : IdentityValidation.Success(current);
    }

    private ProcessActionResult Failure(ProcessActionKind action, string target, string message)
    {
        return Result(action, target, ProcessActionStatus.Failed, "Aucune action effectuee.", message);
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
