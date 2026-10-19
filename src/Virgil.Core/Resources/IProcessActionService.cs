using Virgil.Core.Actions;
using Virgil.Contracts;

namespace Virgil.Core.Resources;

public interface IProcessActionService
{
    bool CanReleaseInactiveMemory { get; }

    /// <summary>
    /// Destructive actions require a matching one-use plan. ReleaseInactiveMemory is
    /// information only and OpenLocation is non destructive; both accept a null plan.
    /// </summary>
    Task<ProcessActionResult> ExecuteAsync(
        ProcessActionKind action,
        ProcessResourceInfo? target,
        ConfirmedPlan? plan,
        CancellationToken cancellationToken);

    ProcessActionResult Skip(ProcessActionKind action, string target);
}
